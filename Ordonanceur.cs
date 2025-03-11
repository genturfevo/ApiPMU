using System.Globalization;
using Microsoft.Extensions.Logging;
using ApiPMU.Services;
using ApiPMU.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ApiPMU.Parsers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using OpenQA.Selenium.DevTools.V131.CSS;
using AngleSharp.Dom;

namespace ApiPMU
{
    /// <summary>
    /// Service hébergé qui exécute l'extraction des données des API PMU.
    /// En mode normal, il s'exécute tous les jours à 00h01 pour télécharger les données de j+1.
    /// En mode débogage, si une date forcée ou une plage de dates est spécifiée, 
    /// il exécute immédiatement l'extraction et, éventuellement, le traitement RubPTMN.
    /// À la fin du traitement, un courriel récapitulatif est envoyé.
    /// </summary>
    public class Ordonanceur : BackgroundService
    {
        private readonly IApiPmuService _apiPmuService;
        private readonly ILogger<Ordonanceur> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;

        // Champs pour le mode Debug permettant de forcer l'exécution pour une date ou une plage de dates
        private readonly DateTime? _forcedDate;
        private readonly (DateTime start, DateTime end)? _forcedDateRange;
        // Flag pour activer l'appel de RubPTMN en mode Debug
        private readonly bool _runRubPTMN;
        // Flag pour activer l'appel de ApiPMU en mode Debug
        private readonly bool _apiPMU;

        public Ordonanceur(IApiPmuService apiPmuService,
                           ILogger<Ordonanceur> logger,
                           IServiceProvider serviceProvider,
                           string connectionString)
        {
            _apiPmuService = apiPmuService ?? throw new ArgumentNullException(nameof(apiPmuService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("La chaîne de connexion ne peut pas être vide.", nameof(connectionString));
            }
            _connectionString = connectionString;

            // ******************************************************** //
            //      (utilisable uniquement en mode débogage)            //
            // Paramétrage de la date ou de la plage de dates à traiter //
            // ainsi que l'activation de RubPTMN                        //
            // ******************************************************** //
#if DEBUG
            // Exemple de paramètre à modifier pour forcer une date simple ou une plage :
            // Pour une date unique : "01032025"
            // Pour une plage de dates : "01032025-05032025"
            string forcedParam = "28022025-12032025"; // <-- modifiez cette valeur selon vos besoins
            if (forcedParam.Contains("-"))
            {
                var parts = forcedParam.Split('-');
                DateTime start = DateTime.ParseExact(parts[0], "ddMMyyyy", CultureInfo.InvariantCulture);
                DateTime end = DateTime.ParseExact(parts[1], "ddMMyyyy", CultureInfo.InvariantCulture);
                _forcedDateRange = (start, end);
                _forcedDate = null;
            }
            else
            {
                _forcedDate = DateTime.ParseExact(forcedParam, "ddMMyyyy", CultureInfo.InvariantCulture);
                _forcedDateRange = null;
            }
            // Activation du traitement ApiPMU en mode Debug (si true, l'extraction sera exécuté, sinon false )
            // Activation du traitement RubPTMN en mode Debug (si true, RubPTMN sera exécuté après l'extraction, sinon false )
            _apiPMU = false;
            _runRubPTMN = true;
#else
            _forcedDate = null;
            _forcedDateRange = null;
#endif
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service Ordonanceur démarré.");

#if DEBUG
            // En mode Debug, si une date forcée ou une plage est spécifiée, traiter immédiatement
            if (_forcedDate.HasValue || _forcedDateRange.HasValue)
            {
                List<DateTime> datesToProcess = new List<DateTime>();
                if (_forcedDateRange.HasValue)
                {
                    for (DateTime d = _forcedDateRange.Value.start; d <= _forcedDateRange.Value.end; d = d.AddDays(1))
                    {
                        datesToProcess.Add(d);
                    }
                }
                else
                {
#pragma warning disable CS8629 // Le type valeur Nullable peut avoir une valeur null.
                    datesToProcess.Add(_forcedDate.Value);
#pragma warning restore CS8629 // Le type valeur Nullable peut avoir une valeur null.
                }

                foreach (var date in datesToProcess)
                {
                    _logger.LogInformation("Exécution forcée pour la date {ForcedDate}.", date.ToString("ddMMyyyy"));
                    if (_apiPMU)
                    {
                        _logger.LogInformation("Exécution de ApiPMU pour la date {ForcedDate}.", date.ToString("ddMMyyyy"));
                        await ExecuteExtractionForDate(date, stoppingToken);
                    }
                    if (_runRubPTMN)
                    {
                        // Appel de RubPTMN pour le calcul des indices pour la date en cours
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                            var rubPTMN = new RubPTMN(dbContext);
                            _logger.LogInformation("Exécution de RubPTMN pour la date {ForcedDate}.", date.ToString("ddMMyyyy"));
                            await rubPTMN.ProcessCalculsAsync(date);
                        }
                    }
                }
                _logger.LogInformation("Exécution forcée terminée pour toutes les dates. Arrêt du service.");
                return;
            }
#endif

            // En mode normal, exécution planifiée quotidienne
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calcul du prochain horaire d'exécution à 00h01
                    DateTime now = DateTime.Now;
                    DateTime nextRunTime = new DateTime(now.Year, now.Month, now.Day, 0, 1, 0);
                    if (now >= nextRunTime)
                    {
                        nextRunTime = nextRunTime.AddDays(1);
                    }

                    TimeSpan delay = nextRunTime - now;
                    _logger.LogInformation("Prochaine exécution prévue dans {Delay} à {NextRunTime}.", delay, nextRunTime);

                    // Attente jusqu'au prochain horaire (avec gestion de l'annulation)
                    await Task.Delay(delay, stoppingToken);

                    // Exécution de l'extraction pour la date j+1
                    DateTime dateProno = DateTime.Today.AddDays(1);
                    await ExecuteExtractionForDate(dateProno, stoppingToken);
                    _logger.LogInformation("***** ApiPMU Terminé pour la date {ForcedDate} *****.", dateProno);

                    // Appel de RubPTMN pour le calcul des rubriques pour la date en cours
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                        var rubPTMN = new RubPTMN(dbContext);
                        await rubPTMN.ProcessCalculsAsync(dateProno);
                        _logger.LogInformation("***** RubPTMN Terminé pour la date {ForcedDate} *****.", dateProno);
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'exécution du téléchargement quotidien des données PMU.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Service Ordonanceur arrêté.");
        }
        /// <summary>
        /// Méthode d'extraction pour une date donnée.
        /// À la fin du traitement, un courriel récapitulatif est envoyé.
        /// </summary>
        /// <param name="dateProno">Date pour laquelle extraire les données.</param>
        /// <param name="token">Token d'annulation.</param>
        private async Task ExecuteExtractionForDate(DateTime dateProno, CancellationToken token)
        {
            string dateStr = dateProno.ToString("ddMMyyyy");
            _logger.LogInformation("Début du téléchargement des données pour la date {DateStr}.", dateStr);

            // ************************************************************************** //
            // Api PMU : Chargement du programme de la journée                            //
            // URL : https://online.turfinfo.api.pmu.fr/rest/client/66/programme/JJMMAAAA //
            // ************************************************************************** //
            //
            var programmeData = await _apiPmuService.ChargerProgrammeAsync<dynamic>(dateStr);

            // ******************************************** //
            // JSon : Conversion en chaîne JSON pour parser //
            // ******************************************** //
            //
            string programmeJson = JsonConvert.SerializeObject(programmeData);

            // *************************************************** //
            // Parser : Extraction des données réunions et courses //
            // *************************************************** //
            //
            ListeProgramme programmeParsed;
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                string connectionString = dbContext.Database.GetDbConnection().ConnectionString;
                var parser = new ProgrammeParser(
                    _serviceProvider.GetRequiredService<ILogger<ProgrammeParser>>(),
                    _connectionString);
                programmeParsed = parser.ParseProgramme(programmeJson, dateStr);
            }
            _logger.LogInformation("Programme parsé avec {CountReunions} réunions et {CountCourses} courses.",
                                   programmeParsed.Reunions.Count, programmeParsed.Courses.Count);

            // ******************************** //
            // BDD : Enregistrement des données //
            // Tables : Reunions et Courses     //
            // ******************************** //
            //
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbSvc = scope.ServiceProvider.GetRequiredService<IDbService>();

                // Pour chaque réunion parsée, effectue un upsert via DbService.
                foreach (var reunion in programmeParsed.Reunions)
                {
                    await dbSvc.SaveOrUpdateReunionAsync(reunion, deleteAndRecreate: true);
                }

                // Pour chaque course parsée, effectue un upsert et met à jour l'âge moyen.
                foreach (var course in programmeParsed.Courses)
                {
                    await dbSvc.SaveOrUpdateCourseAsync(course, deleteAndRecreate: true);
                }

                _logger.LogInformation("Les données Réunions et Courses ont été enregistrées dans la base de données via DbService.");
            }

            // ******************************************************* //
            // FRANCE-GALOP : Paramètre annuel entraineurs (year=AAAA) //
            // ******************************************************* //
            //
            string annee = dateProno.ToString("yyyy");

            // ******************************************************************************************************************************************************************** //
            // FRANCE-GALOP : Chargement des entraineurs - typeIndividu=Entraineur                                                                                                  //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Entraineur&year=2025&specialty=0&racetrack=null&category=6&nbResult=1000 //                                                  //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Entraineur&year=2025&specialty=1&racetrack=null&category=6&nbResult=1000 //                                                    //
            // ******************************************************************************************************************************************************************** //
            //
            var entGalop = new ListeParticipants();
            // Fonction pour essayer d'extraire les données et ajuster l'année si nécessaire
            async Task<ListeParticipants> ExtraireEntraineurAvecAjustementAnnee(string typeIndividu, string annee)
            {
                // Tentative d'extraction des données
                entGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync(typeIndividu, annee);

                // Si la collection est vide, réduire l'année de 1 et essayer à nouveau
                if (entGalop.EntraineurJokeys.Count == 0)
                {
                    _logger.LogWarning($"Aucun entraîneur trouvé pour l'année {annee}. Tentative avec l'année précédente.");

                    // Soustraire 1 an à l'année
                    annee = (int.Parse(annee) - 1).ToString();

                    // Relancer l'extraction avec la nouvelle année
                    entGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync(typeIndividu, annee);
                }

                return entGalop;
            }

            // Appeler la méthode avec l'année initiale pour les entraîneurs
            entGalop = await ExtraireEntraineurAvecAjustementAnnee("Entraineur", annee);

            _logger.LogInformation("Extraction du classement des entraineurs galop terminée.");

            // *************************************************** //
            // FRANCE-GALOP : Paramètre annuel jockeys (year=AAAA) //
            // *************************************************** //
            //
            annee = dateProno.ToString("yyyy");

            // **************************************************************************************************************************************************************** //
            // FRANCE-GALOP : Chargement des jockeys - typeIndividu=Entraineur                                                                                                  //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Jockey&year=2025&specialty=0&racetrack=null&category=6&nbResult=1000 //                                                  //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Jockey&year=2025&specialty=1&racetrack=null&category=6&nbResult=1000 //                                                  //
            // **************************************************************************************************************************************************************** //
            //
            var jokGalop = new ListeParticipants();
            // Fonction pour essayer d'extraire les données et ajuster l'année si nécessaire
            async Task<ListeParticipants> ExtraireJockeyAvecAjustementAnnee(string typeIndividu, string annee)
            {
                // Tentative d'extraction des données
                jokGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync(typeIndividu, annee);

                // Si la collection est vide, réduire l'année de 1 et essayer à nouveau
                if (jokGalop.EntraineurJokeys.Count == 0)
                {
                    _logger.LogWarning($"Aucun entraîneur trouvé pour l'année {annee}. Tentative avec l'année précédente.");

                    // Soustraire 1 an à l'année
                    annee = (int.Parse(annee) - 1).ToString();

                    // Relancer l'extraction avec la nouvelle année
                    jokGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync(typeIndividu, annee);
                }

                return jokGalop;
            }

            // Appeler la méthode avec l'année initiale pour les entraîneurs
            jokGalop = await ExtraireJockeyAvecAjustementAnnee("Jockey", annee);

            _logger.LogInformation("Extraction du classement des jockeys galop terminée.");

            // *********************************************************************************************************************** //
            // LE TROT : Chargement des entraineurs                                                                                    //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=ENTR-A-NEW&sort_by=rank&order_by=asc //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=ENTR-M-NEW&sort_by=rank&order_by=asc //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=ENTR-NEW&sort_by=rank&order_by=asc   //
            // *********************************************************************************************************************** //
            //
            var entTrot = new ListeParticipants();
            // Chargement successif avec les trois types "ENTR-A", "ENTR-M" et "ENTR"
            var typesIndividu = new[] { "ENTR-A", "ENTR-M", "ENTR" };
            foreach (var typeIndividu in typesIndividu)
            {
                var entraineurs = await ExtractEntraineurJockeyTrotRankingAsync(typeIndividu);

                // Ajout des entraîneurs au classement si ils n'existent pas déjà
                foreach (var entraineur in entraineurs)
                {
                    var existingEntraineur = entTrot.EntraineurJokeys
                        .FirstOrDefault(e => e.NumGeny == entraineur.NumGeny
                                          && e.Entjok == entraineur.Entjok
                                          && e.Nom == entraineur.Nom);

                    if (existingEntraineur == null)
                    {
                        entTrot.EntraineurJokeys.Add(entraineur);
                    }
                }
            }

            _logger.LogInformation("Extraction du classement des entraineurs trot terminée.");
            // *********************************************************************************************************************** //
            // LE TROT : Chargement des jockeys - typeIndividu=Entraineur                                                              //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=SUOR-NEW&sort_by=rank&order_by=asc   //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=ETOR-NEW&sort_by=rank&order_by=asc   //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=COOR-NEW&sort_by=rank&order_by=asc   //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=ALJ-A-NEW&sort_by=rank&order_by=asc  //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type=AMAT-A-NEW&sort_by=rank&order_by=asc //
            // *********************************************************************************************************************** //
            //
            var jokTrot = new ListeParticipants();
            // Chargement successif avec les cinq types "SUOR", "ETOR", "COOR", "ALJ-A" et "AMAT-A"
            typesIndividu = new[] { "SUOR", "ETOR", "COOR", "ALJ-A", "AMAT-A" };
            foreach (var typeIndividu in typesIndividu)
            {
                var jockeys = await ExtractEntraineurJockeyTrotRankingAsync(typeIndividu);

                // Ajout des jockeys au classement si ils n'existent pas déjà
                foreach (var jockey in jockeys)
                {
                    var existingJockey = entTrot.EntraineurJokeys
                        .FirstOrDefault(e => e.NumGeny == jockey.NumGeny
                                          && e.Entjok == jockey.Entjok
                                          && e.Nom == jockey.Nom);

                    if (existingJockey == null)
                    {
                        jokTrot.EntraineurJokeys.Add(jockey);
                    }
                }
            }
            _logger.LogInformation("Extraction du classement des jockeys trot terminée.");

            // *************************************** //
            // BDD : Lecture de la journée enregistrée //
            // Boucle sur les réunions et courses      //
            // *************************************** //
            //
            var dbService = _serviceProvider.GetService<IDbService>();
            if (dbService == null)
            {
                _logger.LogError("Le service de base de données (IDbService) n'est pas disponible.");
                return;
            }

            var reunions = await dbService.GetReunionsByDateAsync(dateProno);
            if (reunions == null || !reunions.Any())
            {
                _logger.LogInformation($"Aucune réunion trouvée pour la date {dateStr}");
                return;
            }
            // *********************** //
            // Boucle sur les réunions // 
            // *********************** //
            //
            foreach (var reunion in reunions)
            {
                string numGeny = reunion.NumGeny;
                short numReunion = (short)reunion.NumReunion;
                _logger.LogInformation($"Traitement de la réunion n° {numReunion} pour la date {dateStr}");

                var courses = await dbService.GetCoursesByReunionAsync(reunion.NumGeny);
                if (courses == null || !courses.Any())
                {
                    _logger.LogInformation($"Aucune course trouvée pour la réunion n° {numReunion}");
                    continue;
                }
                // ************************************ //
                // Boucle sur les courses de la réunion // 
                // ************************************ //
                //
                foreach (var course in courses)
                {
                    // Recherche de la discipline pour ciblage des entraineurs et drivers en fin de traitement
                    short numCourse = course.NumCourse;
                    string disc = course.Discipline;
                    _logger.LogInformation($"Chargement du détail pour la course n° {numCourse} de la réunion n° {numReunion}");

                    // ********************************************************************************************* //
                    // Api PMU : Chargement des participants d'une course                                            //
                    // URL : https://online.turfinfo.api.pmu.fr/rest/client/66/programme/JJMMAAAA/Rx/Cx/participants //
                    // ********************************************************************************************* //
                    //
                    var courseData = await _apiPmuService.ChargerCourseAsync<dynamic>(dateStr, numReunion, numCourse, "participants");

                    // ******************************************** //
                    // JSon : Conversion en chaîne JSON pour parser //
                    // ******************************************** //
                    //
                    string courseJson = JsonConvert.SerializeObject(courseData);

                    // ******************************************** //
                    // Parser : Extraction des données participants //
                    // ******************************************** //
                    //
                    ListeParticipants participantsParsed;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                        string connectionString = dbContext.Database.GetDbConnection().ConnectionString;
                        var parser = new ParticipantsParser(
                            _serviceProvider.GetRequiredService<ILogger<ParticipantsParser>>(),
                            _connectionString);
                        participantsParsed = parser.ParseParticipants(courseJson, numGeny, numReunion, numCourse, disc);
                    }
                    _logger.LogInformation("Course parsé avec {CountChevaux}.", participantsParsed.Chevaux.Count);

                    // ******************************** //
                    // BDD : Enregistrement des données //
                    // Table : Chevaux                  //
                    // ******************************** //
                    //
                    await dbService.SaveOrUpdateChevauxAsync(participantsParsed.Chevaux, deleteAndRecreate: true);
                    _logger.LogInformation($"Détail des chevaux enregistré pour la course n° {numCourse} de la réunion n° {numReunion}");

                    // ******************************** //
                    // BDD : Enregistrement des données //
                    // Table : Courses - MAJ age moyen  //
                    // ******************************** //
                    //
                    await dbService.UpdateCourseAgeMoyenAsync(numGeny, numCourse);
                    _logger.LogInformation($"Age moyen mis à jour pour la course Numero : {numCourse} de la réunion NumGeny : {numGeny}");

                    // *************************************************************************************************************** //
                    // Api PMU : Chargement de l'historique des participants d'une course                                              //
                    // URL : https://online.turfinfo.api.pmu.fr/rest/client/66/programme/JJMMAAAA/Rx/Cx/performances-detaillees/pretty //
                    // *************************************************************************************************************** //
                    //
                    var performancesData = await _apiPmuService.ChargerPerformancesAsync<dynamic>(dateStr, numReunion, numCourse, "performances-detaillees/pretty");

                    // Parfois l'url performances-detaillees/pretty est absente
                    // Impossible de poursuivre, on passe directement aux entraineurs et jockeys
                    if (performancesData != null)
                    {
                        // ******************************************** //
                        // JSon : Conversion en chaîne JSON pour parser //
                        // ******************************************** //
                        //
                        string performancesJson = JsonConvert.SerializeObject(performancesData);

                        // ******************************************** //
                        // Parser : Extraction des données participants //
                        // ******************************************** //
                        //
                        ListeParticipants performancesParsed;
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                            string connectionString = dbContext.Database.GetDbConnection().ConnectionString;
                            var parser = new PerformancesParser(
                                _serviceProvider.GetRequiredService<ILogger<PerformancesParser>>(),
                                _connectionString);
                            performancesParsed = parser.ParsePerformances(performancesJson, disc);
                        }
                        _logger.LogInformation("Performances parsées avec {CountPerformances} entrées.", performancesParsed.Performances.Count);

                        // ***************************************************** //
                        // Complément des performances : Accès Api PMU programme //
                        // ***************************************************** //
                        //
                        short perfR;
                        short perfC;
                        foreach (var perf in performancesParsed.Performances)
                        {
                            _logger.LogInformation($"Historique du cheval R{numReunion}C{numCourse}-{perf.Nom}-{perf.DatePerf}");
                            // Utiliser la date propre à la performance pour charger le programme du jour correspondant
                            string perfDateStr = perf.DatePerf.ToString("ddMMyyyy");
                            // Video contient temporairement le nom du prix (nomPrix)
                            // Pour la recherche de la course historique d'un partant
                            string nomPrixTemp = perf.Video;
                            // Pour la recherche du cheval dans la course
                            string nomCh = perf.Nom;
                            perfR = 0;
                            perfC = 0;

                            // ***************************************************************************************** //
                            // Api PMU : (Performances) Charger le programme correspondant à la date de la performance   //
                            // URL : https://online.turfinfo.api.pmu.fr/rest/client/66/programme/JJMMAAAA                //
                            // ***************************************************************************************** //
                            //
                            var programmeJsonForPerfData = await _apiPmuService.ChargerProgrammeAsync<dynamic>(perfDateStr);

                            // ******************************************** //
                            // JSon : Conversion en chaîne JSON pour parser //
                            // ******************************************** //
                            //
                            string programmeJsonForPerfJson = JsonConvert.SerializeObject(programmeJsonForPerfData);

                            // ************************************************ //
                            // Parser : se caller sur la course de l'historique //
                            // ************************************************ //
                            //
                            JObject rdata = JObject.Parse(programmeJsonForPerfJson);
                            JToken? Jsonprog = rdata["programme"];
                            if (Jsonprog == null) { 
                                _logger.LogError("La clé 'programme' est absente du programmeJsonForPerfJson.");
                            }
                            JToken? Jsonreun = Jsonprog["reunions"];
                            if (Jsonreun == null) { 
                                _logger.LogError("La clé 'reunions' est absente du programmeJsonForPerfJson.");
                            }
                            int allocation = 0;
                            string cordage = string.Empty;
                            string conditions = string.Empty;
                            short partants = 0;
                            int distance = 0;

                            bool found = false;
                            foreach (JToken reunionToken in Jsonreun)
                            {
                                if (reunionToken["courses"] != null)
                                {
                                    JToken? Jsoncour = reunionToken["courses"];
                                    if (courses != null)
                                    {
                                        foreach (JToken courseToken in Jsoncour)
                                        {
                                            string? libC = courseToken["libelle"]?.ToString();
                                            string? libL = courseToken["libelleCourt"]?.ToString();
                                            //
                                            // Minimum de 60% de correspondance entre :
                                            // - le nom du prix de l'historique
                                            // - le libelle de la course
                                            //
                                            if ((libC != null && ContainsApproximately(libC, nomPrixTemp, 0.6)) ||
                                                (libL != null && ContainsApproximately(libL, nomPrixTemp, 0.6)))
                                            {
                                                perfR = (short)(short.TryParse(courseToken["numReunion"]?.ToString(), out short nr) ? nr : 0);
                                                perfC = (short)(short.TryParse(courseToken["numExterne"]?.ToString(), out short nc) ? nc : 0);
                                                allocation = (int)(int.TryParse(courseToken["montantTotalOffert"]?.ToString(), out int al) ? al : 0);
                                                cordage = courseToken["corde"]?.ToString() switch
                                                {
                                                    string s when s.Contains("GAUCHE") => "GAUCHE",
                                                    string s when s.Contains("DROITE") => "DROITE",
                                                    _ => string.Empty
                                                };
                                                conditions = courseToken["conditions"]?.ToString() ?? string.Empty;
                                                partants = (short)(short.TryParse(courseToken["nombreDeclaresPartants"]?.ToString(), out short pa) ? pa : 0);
                                                distance = (int)(int.TryParse(courseToken["distance"]?.ToString(), out int di) ? di : 0);
                                                found = true;
                                                break; // Sort de la boucle interne
                                            }
                                        }
                                    }
                                }
                                if (found)
                                    break; // Sort de la boucle externe
                            }
                            if (found && perfR != 0 && perfC != 0)
                            {
                                // ********************************************************************************************* //
                                // Api PMU : (Performances) Charger les participants d'une course                                //
                                // URL : https://online.turfinfo.api.pmu.fr/rest/client/66/programme/JJMMAAAA/Rx/Cx/participants //
                                // ********************************************************************************************* //
                                //
                                var courseJsonForPerfData = await _apiPmuService.ChargerCourseAsync<dynamic>(perfDateStr, perfR, perfC, "participants");

                                // ******************************************** //
                                // JSon : Conversion en chaîne JSON pour parser //
                                // ******************************************** //
                                //
                                string courseJsonForPerfJson = JsonConvert.SerializeObject(courseJsonForPerfData);

                                // ********************************************* //
                                // Parser : se caller sur le cheval de la course //
                                // ********************************************* //
                                //
                                JObject cdata = JObject.Parse(courseJsonForPerfJson);
                                JToken? Jsondcou = cdata["participants"];
                                if (Jsondcou != null)
                                {
                                    foreach (JToken dcourseToken in Jsondcou)
                                    {
                                        string? nom = dcourseToken["nom"]?.ToString();
                                        if (nomCh == nom)
                                        {
                                            JToken? gainsCarriere = dcourseToken?["gainsParticipant"];
                                            int gains = 0;
                                            if (gainsCarriere != null && gainsCarriere["gainsCarriere"] != null && int.TryParse(gainsCarriere["gainsCarriere"].ToString(), out int g))
                                            {
                                                gains = g / 100;
                                            }
                                            perf.Gains = gains;
                                            perf.Cordage = cordage;
                                            perf.Partants = partants;
                                            perf.Dist = distance;
                                            string typeCourse = ProgrammeParser.CategorieCourseScraping(conditions, allocation);
                                            perf.TypeCourse = typeCourse;
                                            JToken? dCote = dcourseToken["dernierRapportDirect"];
                                            float cote = dCote?["rapport"]?.Value<float?>() ?? 0f;
                                            perf.Cote = cote;
                                            perf.Deferre = string.Empty;
                                            if (perf.Discipline == "ATTELE" || perf.Discipline == "MONTE")
                                            {
                                                perf.Deferre = dcourseToken?["deferre"]?.ToString() switch
                                                {
                                                    "DEFERRE_ANTERIEURS" => "DA",
                                                    "DEFERRE_POSTERIEURS" => "DP",
                                                    "DEFERRE_ANTERIEURS_POSTERIEURS" => "D4",
                                                    "PROTEGE_ANTERIEURS" => "PA",
                                                    "PROTEGE_POSTERIEURS" => "PP",
                                                    "PROTEGE_ANTERIEURS_POSTERIEURS" => "P4",
                                                    "PROTEGE_ANTERIEURS_DEFERRRE_POSTERIEURS" => "PA DP",
                                                    "DEFERRRE_ANTERIEURS_PROTEGE_POSTERIEURS" => "DA PP",
                                                    _ => string.Empty
                                                };
                                            }
                                            else
                                            {
                                                perf.Deferre = dcourseToken?["handicapValeur"]?.ToString() ?? string.Empty;
                                            }
                                            // avis_1.png : vert, avis_2.png : jaune, avis_3.png : rouge
                                            string avisEntraineur = dcourseToken?["avisEntraineur"]?.ToString() ?? string.Empty;
                                            string avis = avisEntraineur switch
                                            {
                                                "POSITIF" => "avis_1.png",
                                                "NEUTRE" => "avis_2.png",
                                                "NEGATIF" => "avis_3.png",
                                                _ => string.Empty
                                            };
                                            perf.Avis = avis;
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    perf.Gains = 0;
                                    perf.Cordage = string.Empty;
                                    if (allocation > 0)
                                    {
                                        perf.TypeCourse = ProgrammeParser.CategorieCourseScraping("", allocation);
                                    }
                                    else
                                    {
                                        perf.TypeCourse = "G";
                                    }
                                    perf.Cote = 0;
                                    perf.Deferre = string.Empty;
                                    perf.Avis = string.Empty;
                                    _logger.LogError("La clé 'participants' est absente du courseJsonForPerfJson.");
                                }
                            }
                            else
                            {
                                perf.Gains = 0;
                                perf.Cordage = string.Empty;
                                perf.TypeCourse = "G";
                                perf.Cote = 0;
                                perf.Partants = 0;
                                perf.Deferre = string.Empty;
                                perf.Avis = string.Empty;
                                _logger.LogError($"La réunion/course est absente {!found} && numReunion {perfR} && numCourse {perfC}.");
                            }
                        }

                        // ******************************** //
                        // BDD : Enregistrement des données //
                        // Table : Performances             //
                        // ******************************** //
                        //
                        await dbService.SaveOrUpdatePerformanceAsync(performancesParsed.Performances, updateColumns: true, deleteAndRecreate: true);
                        _logger.LogInformation($"Performances enregistrées pour la course n° {numCourse} de la réunion n° {numReunion}");

                    }
                    else{
                        _logger.LogError($"Erreur lors de l'appel API pour l'URL 'performances-detaillees/pretty' R{numReunion}C{numCourse}");
                    }
                    // ************************************************************************************* //
                    // Récupération des entraîneurs et jockeys de la course en cours dans la base de données //
                    // ************************************************************************************* //
                    List<string> listeEntraineurs = await GetEntraineursOrJockeysAsync(numGeny, numCourse, "Entraineur");
                    List<string> listeJockeys = await GetEntraineursOrJockeysAsync(numGeny, numCourse, "Jokey");

                    // ******************************************************************************** //
                    // Chargement du DataSet EntraineurJokey à partir des tables FranceGalop ou Le Trot //
                    // ******************************************************************************** //
                    ListeParticipants participants = new ListeParticipants();
                    List<string> entraineursNonTrouves = new List<string>();
                    List<string> jockeysNonTrouves = new List<string>();

                    if (disc == "ATTELE" || disc == "MONTE")
                    {
                        // 🔹 Comparer listeEntraineurs avec entTrot.EntraineurJokeys
                        foreach (string entraineur in listeEntraineurs)
                        {
                            // Nettoyer le nom de l'entraîneur recherché
                            string entraineurNettoye = NettoyerNom(entraineur);

                            // Chercher le meilleur match parmi les entraîneurs
                            var match = TrouverMeilleurMatch(entraineurNettoye, entTrot.EntraineurJokeys.Select(e => NettoyerNom(e.Nom)).ToList());

                            if (match != null)
                            {
                                // Trouver l'entraîneur correspondant dans la liste
                                var entraineurMatch = entTrot.EntraineurJokeys.FirstOrDefault(e => NettoyerNom(e.Nom) == match);

                                if (entraineurMatch != null)
                                {
                                    participants.EntraineurJokeys.Add(new EntraineurJokey
                                    {
                                        NumGeny = numGeny,
                                        Entjok = "E", // "E" pour Entraîneur
                                        Nom = entraineurMatch.Nom,
                                        NbCourses = entraineurMatch.NbCourses,
                                        NbVictoires = entraineurMatch.NbVictoires,
                                        NbCR = entraineurMatch.NbCR,
                                        Ecart = entraineurMatch.Ecart,
                                        DateModif = DateTime.Now
                                    });
                                }
                            }
                            else
                            {
                                // Ajouter un enregistrement avec des valeurs par défaut si aucune correspondance n'est trouvée
                                participants.EntraineurJokeys.Add(new EntraineurJokey
                                {
                                    NumGeny = numGeny,
                                    Entjok = "E", // "E" pour Entraîneur
                                    Nom = entraineur, // Nom recherché
                                    NbCourses = 0,
                                    NbVictoires = 0,
                                    NbCR = 0,
                                    Ecart = 0,
                                    DateModif = DateTime.Now
                                });
                                entraineursNonTrouves.Add(entraineur);
                            }
                        }

                        // 🔹 Comparer listeJockeys avec jokTrot.EntraineurJokeys
                        foreach (string jockey in listeJockeys)
                        {
                            // Nettoyer le nom de l'entraîneur recherché
                            string jockeyNettoye = NettoyerNom(jockey);

                            // Chercher le meilleur match parmi les entraîneurs
                            var match = TrouverMeilleurMatch(jockeyNettoye, jokTrot.EntraineurJokeys.Select(e => NettoyerNom(e.Nom)).ToList());

                            if (match != null)
                            {
                                // Trouver l'entraîneur correspondant dans la liste
                                var jockeyMatch = jokTrot.EntraineurJokeys.FirstOrDefault(e => NettoyerNom(e.Nom) == match);

                                if (jockeyMatch != null)
                                {
                                    participants.EntraineurJokeys.Add(new EntraineurJokey
                                    {
                                        NumGeny = numGeny,
                                        Entjok = "J", // "J" pour Driver/Jockey
                                        Nom = jockeyMatch.Nom,
                                        NbCourses = jockeyMatch.NbCourses,
                                        NbVictoires = jockeyMatch.NbVictoires,
                                        NbCR = jockeyMatch.NbCR,
                                        Ecart = jockeyMatch.Ecart,
                                        DateModif = DateTime.Now
                                    });
                                }
                            }
                            else
                            {
                                // Ajouter un enregistrement avec des valeurs par défaut si aucune correspondance n'est trouvée
                                participants.EntraineurJokeys.Add(new EntraineurJokey
                                {
                                    NumGeny = numGeny,
                                    Entjok = "J", // "J" pour Driver/Jockey
                                    Nom = jockey, // Nom recherché
                                    NbCourses = 0,
                                    NbVictoires = 0,
                                    NbCR = 0,
                                    Ecart = 0,
                                    DateModif = DateTime.Now
                                });
                                jockeysNonTrouves.Add(jockey);
                            }
                        }

                    }
                    else // 🔹 Pour les autres disciplines (Galop)
                    {
                        // 🔹 Comparer listeEntraineurs avec entGalop.EntraineurJokeys
                        foreach (string entraineur in listeEntraineurs)
                        {
                            // Nettoyer le nom de l'entraîneur recherché
                            string entraineurNettoye = NettoyerNom(entraineur);

                            // Chercher le meilleur match parmi les entraîneurs
                            var match = TrouverMeilleurMatch(entraineurNettoye, entGalop.EntraineurJokeys.Select(e => NettoyerNom(e.Nom)).ToList());

                            if (match != null)
                            {
                                // Trouver l'entraîneur correspondant dans la liste
                                var entraineurMatch = entGalop.EntraineurJokeys.FirstOrDefault(e => NettoyerNom(e.Nom) == match);

                                if (entraineurMatch != null)
                                {
                                    participants.EntraineurJokeys.Add(new EntraineurJokey
                                    {
                                        NumGeny = numGeny,
                                        Entjok = "E", // "E" pour Entraîneur
                                        Nom = entraineurMatch.Nom,
                                        NbCourses = entraineurMatch.NbCourses,
                                        NbVictoires = entraineurMatch.NbVictoires,
                                        NbCR = entraineurMatch.NbCR,
                                        Ecart = entraineurMatch.Ecart,
                                        DateModif = DateTime.Now
                                    });
                                }
                            }
                            else
                            {
                                // Ajouter un enregistrement avec des valeurs par défaut si aucune correspondance n'est trouvée
                                participants.EntraineurJokeys.Add(new EntraineurJokey
                                {
                                    NumGeny = numGeny,
                                    Entjok = "E", // "E" pour Entraîneur
                                    Nom = entraineur, // Nom recherché
                                    NbCourses = 0,
                                    NbVictoires = 0,
                                    NbCR = 0,
                                    Ecart = 0,
                                    DateModif = DateTime.Now
                                });
                                entraineursNonTrouves.Add(entraineur);
                            }
                        }

                        // 🔹 Comparer listeJockeys avec jokGalop.EntraineurJokeys
                        foreach (string jockey in listeJockeys)
                        {
                            // Nettoyer le nom de l'entraîneur recherché
                            string jockeyNettoye = NettoyerNom(jockey);

                            // Chercher le meilleur match parmi les entraîneurs
                            var match = TrouverMeilleurMatch(jockeyNettoye, jokGalop.EntraineurJokeys.Select(e => NettoyerNom(e.Nom)).ToList());

                            if (match != null)
                            {
                                // Trouver l'entraîneur correspondant dans la liste
                                var jockeyMatch = jokGalop.EntraineurJokeys.FirstOrDefault(e => NettoyerNom(e.Nom) == match);

                                if (jockeyMatch != null)
                                {
                                    participants.EntraineurJokeys.Add(new EntraineurJokey
                                    {
                                        NumGeny = numGeny,
                                        Entjok = "J", // "J" pour Driver/Jockey
                                        Nom = jockeyMatch.Nom,
                                        NbCourses = jockeyMatch.NbCourses,
                                        NbVictoires = jockeyMatch.NbVictoires,
                                        NbCR = jockeyMatch.NbCR,
                                        Ecart = jockeyMatch.Ecart,
                                        DateModif = DateTime.Now
                                    });
                                }
                            }
                            else
                            {
                                // Ajouter un enregistrement avec des valeurs par défaut si aucune correspondance n'est trouvée
                                participants.EntraineurJokeys.Add(new EntraineurJokey
                                {
                                    NumGeny = numGeny,
                                    Entjok = "J", // "J" pour Driver/Jockey
                                    Nom = jockey, // Nom recherché
                                    NbCourses = 0,
                                    NbVictoires = 0,
                                    NbCR = 0,
                                    Ecart = 0,
                                    DateModif = DateTime.Now
                                });
                                jockeysNonTrouves.Add(jockey);
                            }
                        }

                    }

                    // 🔹 Logger les entraîneurs et jockeys non trouvés
                    if (entraineursNonTrouves.Count > 0)
                    {
                        _logger.LogWarning($"Entraîneurs non trouvés dans {disc}: {string.Join(", ", entraineursNonTrouves)}");
                    }

                    if (jockeysNonTrouves.Count > 0)
                    {
                        _logger.LogWarning($"Jockeys non trouvés dans {disc}: {string.Join(", ", jockeysNonTrouves)}");
                    }

                    // ******************************** //
                    // BDD : Enregistrement des données //
                    // Table : Entraineurs et Jockeys   //
                    // ******************************** //
                    //
                    await dbService.SaveOrUpdateEntraineurJokeyAsync(participants.EntraineurJokeys, updateColumns: true, deleteAndRecreate: true);
                    _logger.LogInformation($"Performances enregistrées pour la course n° {numCourse} de la réunion n° {numReunion}");

                }
            }
            _logger.LogInformation("Téléchargement des données terminé pour la date {DateStr}.", dateStr);

            // ********************************** //
            // Envoi du courriel de récapitulatif //
            // ********************************** //
            //
            try
            {
                // Création d'un scope pour obtenir une instance de ApiPMUDbContext
                using (var scope = _serviceProvider.CreateScope())
                {
                    // Récupération du DbContext généré par le scaffolding
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();

                    // Paramètres pour l'envoi du courriel
                    bool flagTRT = false; // Ajustez selon votre logique (exemple : définir à true en cas d'incident)
                    string serveur = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "PRESLESMU";
                    string subjectPrefix = $"{serveur} : ApiPMU Fin de traitement";
                    string log = "Traitement terminé avec succès."; // Vous pouvez construire ce log en fonction des traitements effectués
                    var courrielService = new CourrielService(
                         _serviceProvider.GetRequiredService<ILogger<CourrielService>>(),
                         _connectionString);
                    await courrielService.SendCompletionEmailAsync(dateProno, flagTRT, subjectPrefix, log, serveur.ToLower(), dbContext);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi du courriel récapitulatif.");
            }
        }

        /// <summary>
        /// Méthode d'extraction des entraineurs et jockeys sur le site france-galop.
        /// </summary>
        /// <param name="typeIndividu">Extraction pour les jockeys ou les entraineurs.</param>
        /// <param name="annee">Année des statistiques.</param>
        private async Task<ICollection<EntraineurJokey>> ExtractEntraineurJockeyGalopRankingAsync(string typeIndividu, string annee)
        {
            // 📌 1️⃣ Configurer WebDriverManager pour télécharger ChromeDriver
            new DriverManager().SetUpDriver(new ChromeConfig());

            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--headless");  // Mode sans interface
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("window-size=1920x1080");

            using IWebDriver driver = new ChromeDriver(options);

            ListeParticipants participants = new ListeParticipants();
            bool dataFound = false;

            // 📌 2️⃣ Charger les résultats pour spc = 0 et spc = 1
            for (int spc = 0; spc <= 1; spc++)
            {
                string url = $"https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu={typeIndividu}&year={annee}&specialty={spc}&racetrack=null&category=6&nbResult=1000";

                try
                {
                    // 📌 3️⃣ Charger l'URL dans Selenium WebDriver
                    driver.Navigate().GoToUrl(url);

                    // 📌 4️⃣ Attendre quelques secondes pour que le JavaScript s'exécute
                    await Task.Delay(5000);

                    // 📌 5️⃣ Récupérer le HTML après exécution JavaScript
                    string pageSource = driver.PageSource;

                    // 📌 6️⃣ Parser le HTML avec HtmlAgilityPack
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(pageSource);

                    // 📌 7️⃣ Extraire les liens pour récupérer les noms
                    var nomsNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/fr/entraineur/') or contains(@href, '/fr/jockey/')]");
                    if (nomsNodes == null)
                    {
                        _logger.LogWarning($"Aucun classement trouvé pour {typeIndividu} en {annee} (spc={spc}).");
                        continue; // Passe à spc = 1
                    }

                    var rawData = doc.DocumentNode.SelectNodes("//body")?.FirstOrDefault()?.InnerText?.Split("\n");

                    if (rawData == null || rawData.Length < 10)
                    {
                        _logger.LogWarning($"Impossible de récupérer les données de {typeIndividu} pour l'année {annee} (spc={spc}).");
                        continue;
                    }

                    int index = 0;
                    foreach (var nomNode in nomsNodes)
                    {
                        string nomIndividu = nomNode.InnerText.Trim().ToUpper().Replace("&AMP;","&");

                        if (index + 10 >= rawData.Length) break; // Vérification pour éviter les erreurs d'index

                        // 📌 8️⃣ Extraire les données à partir du texte brut
                        int.TryParse(rawData[index + 9]?.Trim(), out int nbCourses);
                        int.TryParse(rawData[index + 10]?.Trim(), out int nbVictoires);
                        short.TryParse(rawData[index + 11]?.Trim(), out short nbCR);

                        // Vérifier si l'individu existe déjà dans la collection
                        var existingEntr = participants.EntraineurJokeys
                            .FirstOrDefault(e => e.Nom.Equals(nomIndividu, StringComparison.OrdinalIgnoreCase));

                        if (existingEntr != null)
                        {
                            // Si trouvé, cumuler les valeurs
                            existingEntr.NbCourses += nbCourses;
                            existingEntr.NbVictoires += nbVictoires;
                            existingEntr.NbCR = (short?)(existingEntr.NbCR.GetValueOrDefault(0) + nbCR);
                        }
                        else
                        {
                            // Sinon, ajouter une nouvelle entrée
                            EntraineurJokey entJok = new EntraineurJokey
                            {
                                NumGeny = string.Empty,
                                Entjok = char.ToUpper(typeIndividu[0]).ToString().Replace("'", " "),
                                Nom = nomIndividu,
                                NbCourses = nbCourses,
                                NbVictoires = nbVictoires,
                                NbCR = nbCR,
                                Ecart = 0
                            };

                            participants.EntraineurJokeys.Add(entJok);
                        }
                        index += 21; // Déplacement vers la prochaine entrée
                        dataFound = true;
                    }

                    _logger.LogInformation($"Extraction terminée pour {typeIndividu} en {annee} (spc={spc}) avec {participants.EntraineurJokeys.Count} entrées.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erreur lors de l'extraction du classement des {typeIndividu}s ({annee}, spc={spc}) : {ex.Message}");
                }
            }

            if (!dataFound)
            {
                _logger.LogError($"Aucune donnée trouvée pour {typeIndividu} en {annee}. Annulation de l'extraction.");
            }

            driver.Quit(); // Ferme le navigateur
            return participants.EntraineurJokeys;
        }

        /// <summary>
        /// Méthode d'extraction des entraineurs et jockeys sur le site Le Trot.
        /// </summary>
        /// <param name="typeIndividu">Extraction pour les jockeys ou les entraineurs.</param>
        private async Task<ICollection<EntraineurJokey>> ExtractEntraineurJockeyTrotRankingAsync(string typeIndividu)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");

            string url = $"https://www.letrot.com/v1/api/rankings/person?page=1&limit=2000&ranking_type={typeIndividu}-NEW&sort_by=rank&order_by=asc";

            try
            {
                string response = await client.GetStringAsync(url);

                // Désérialisation du JSON
                var rankings = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TrotRanking>>(response);

                if (rankings == null || rankings.Count == 0)
                {
                    _logger.LogError($"Aucun classement de {typeIndividu} trouvé !");
                    return new List<EntraineurJokey>();
                }

                ListeParticipants participants = new ListeParticipants();

                // Définir des tranches pour le ratio NbCourses / NbVictoires
                var bins = new List<int> { 0, 10, 20, 50, 100, 200, 500, 1000 };
                var labels = new List<string> { "0-10", "11-20", "21-50", "51-100", "101-200", "201-500", "501-1000" };

                foreach (var ranking in rankings)
                {
                    EntraineurJokey entJok = new EntraineurJokey
                    {
                        NumGeny = string.Empty,
                        Entjok = char.ToUpper(typeIndividu[0]).ToString(),
                        Nom = ranking.PersonLabel.ToUpper().Replace("&AMP;", "&").Replace("'", " "),
                        NbCourses = ranking.NbRaces ?? 0,
                        NbVictoires = ranking.NbVictories ?? 0,
                        NbCR = (short?)(ranking.NbRaces.HasValue ? (short)Math.Round(0.3 * ranking.NbRaces.Value) : (short?)null),
                        Ecart = 0
                    };

                    // Ajouter l'entraîneur à la liste
                    participants.EntraineurJokeys.Add(entJok);
                }

                _logger.LogInformation($"Extraction terminée pour {typeIndividu} avec {participants.EntraineurJokeys.Count} entrées.");
                return participants.EntraineurJokeys;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de l'extraction du classement des {typeIndividu}s : {ex.Message}");
                return new List<EntraineurJokey>();
            }
        }
        private class TrotRanking
        {
            public string? Rank { get; set; }
            public string? PersonId { get; set; }
            public string? PersonLastname { get; set; }
            public string? PersonFirstname { get; set; }
            public string? PersonLabel { get; set; }
            public int? NbVictories { get; set; }
            public int? NbRaces { get; set; }
            public int? TotalGain { get; set; }
        }

        /// <summary>
        /// Méthode d'extraction des entraineurs ou jockeys sur une réunion dans la base de données.
        /// </summary>
        /// <param name="numGeny">Clef d'accès pour les réunions.</param>
        /// <param name="type">entraineur ou jokey.</param>
        private async Task<List<string>> GetEntraineursOrJockeysAsync(string numGeny, short numCourse, string type)
        {
            List<string> noms = new List<string>();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();

                string query = type == "Entraineur"
                    ? "SELECT DISTINCT(entraineur) FROM chevaux WHERE NumGeny = @numGeny AND NumCourse = @numCourse"
                    : "SELECT DISTINCT(jokey) FROM chevaux WHERE NumGeny = @numGeny AND NumCourse = @numCourse";

                using var command = dbContext.Database.GetDbConnection().CreateCommand();
                command.CommandText = query;

                // 📌 Ajout correct des paramètres
                var paramNumGeny = new Microsoft.Data.SqlClient.SqlParameter("@numGeny", numGeny);
                var paramNumCourse = new Microsoft.Data.SqlClient.SqlParameter("@numCourse", numCourse);

                command.Parameters.Add(paramNumGeny);
                command.Parameters.Add(paramNumCourse);

                dbContext.Database.OpenConnection();
                using var reader = await command.ExecuteReaderAsync();

                while (reader.Read())
                {
                    noms.Add(reader.GetString(0).ToUpper().Trim());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erreur lors de la récupération des {type}s : {ex.Message}");
            }

            return noms;
        }

        /// <summary>
        /// Fonction pour nettoyer les noms (retirer "MME", "MMLE", etc.).
        /// </summary>
        /// <param name="nom">Nom de l'entraineur ou du jokey.</param>
        public static string NettoyerNom(string nom)
        {
            // Retirer les préfixes MME ou MMLE
            nom = nom.Replace("MME ", "").Replace("MMLE ", "").Trim();
            // Traiter les initiales et prénoms composés, enlever les espaces inutiles
            nom = nom.Replace(". ", ".");
            // Gérer les caractères "&" (associations)
            nom = nom.Replace("&", "and");
            return nom;
        }
        
        /// <summary>
        /// Calcul de la distance de Levenshtein entre deux chaînes.
        /// </summary>
        /// <param name="a">Nom de l'entraineur ou du jokey recherché.</param>
        /// <param name="b">Nom de l'entraineur ou du jokey de la liste de recherche.</param>
        public static int CalculerDistanceLevenshtein(string a, string b)
        {
            int n = a.Length;
            int m = b.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int substitutionCost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + substitutionCost);
                }
            }

            return d[n, m];
        }

        /// <summary>
        /// Fonction pour trouver la meilleure correspondance entre deux chaînes.
        /// </summary>
        /// <param name="nomRecherche">Nom de l'entraineur ou du jokey recherché.</param>
        /// <param name="listeEntraineurs">liste des noms d'entraineur ou jokey pour la recherche.</param>
        public static string TrouverMeilleurMatch(string nomRecherche, List<string> listeEntraineurs)
        {
            string meilleurMatch = string.Empty;
            int scoreMax = int.MaxValue; // Plus la distance est faible, mieux c'est

            foreach (var entraineur in listeEntraineurs)
            {
                int score = CalculerDistanceLevenshtein(nomRecherche, entraineur);
                if (score < scoreMax)
                {
                    scoreMax = score;
                    meilleurMatch = entraineur;
                }
            }

            // ✅ 1️⃣ Vérifier si le meilleur match est suffisamment proche
            if (!string.IsNullOrEmpty(meilleurMatch))
            {
                int longueurMax = Math.Max(nomRecherche.Length, meilleurMatch.Length);
                int seuil = longueurMax / 3; // ⚠ Si la distance > 1/3 de la longueur, ce n'est pas un bon match

                if (scoreMax > seuil)
                {
                    return string.Empty; // 🔴 Pas de correspondance fiable
                }
            }

            return meilleurMatch; // 🟢 Correspondance valide
        }

        /// <summary>
        /// Méthode de calcul de similarité entre 2 chaînes de caractères 
        /// </summary>
        /// <param name="chaine">Chaine où s'effectue la recherche.</param>
        /// <param name="recherche">Chaine recherchée.</param>
        private bool ContainsApproximately(string chaine, string recherche, double threshold)
        {
            if (string.IsNullOrEmpty(recherche))
                return false;

            // Test direct complet
            if (chaine.IndexOf(recherche, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Vérifier si le dernier mot de 'recherche' est présent dans 'chaine'
            var words = recherche.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                string lastWord = words[words.Length - 1];
                if (!string.IsNullOrEmpty(lastWord) &&
                    chaine.IndexOf(lastWord, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            int requiredLength = (int)Math.Ceiling(recherche.Length * threshold);

            // Parcourt toutes les sous-chaînes de "recherche" d'une longueur égale ou supérieure à requiredLength
            for (int i = 0; i <= recherche.Length - requiredLength; i++)
            {
                string sub = recherche.Substring(i, requiredLength);
                if (chaine.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
