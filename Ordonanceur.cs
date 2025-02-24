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
using System.Text.Json;

namespace ApiPMU
{
    /// <summary>
    /// Service hébergé qui exécute l'extraction des données des API PMU.
    /// En mode normal, il s'exécute tous les jours à 00h01 pour télécharger les données de j+1.
    /// En mode débogage, si une date forcée est spécifiée ci-dessous, il l'exécute immédiatement.
    /// À la fin du traitement, un courriel récapitulatif est envoyé.
    /// </summary>
    public class Ordonanceur : BackgroundService
    {
        private readonly IApiPmuService _apiPmuService;
        private readonly ILogger<Ordonanceur> _logger;
        private readonly DateTime? _forcedDate;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;

        public Ordonanceur(IApiPmuService apiPmuService,
                           ILogger<Ordonanceur> logger,
                           IServiceProvider serviceProvider,
                           string connectionString)
        {
            _apiPmuService = apiPmuService;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectionString = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : throw new ArgumentException("La chaîne de connexion est obligatoire.", nameof(connectionString));

            // ************************************************* //
            //      (utilisable uniquement en mode débogage)     //
            // Paramétrage de la date du programme à télécharger //
            // ************************************************* //

#if DEBUG
            _forcedDate = DateTime.ParseExact("24022025", "ddMMyyyy", CultureInfo.InvariantCulture);
#else
            _forcedDate = null;
#endif
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service Ordonanceur démarré.");

            // Si une date forcée est spécifiée, l'exécuter immédiatement et sortir.
            if (_forcedDate.HasValue) {
                _logger.LogInformation("Exécution forcée pour la date {ForcedDate}.", _forcedDate.Value.ToString("ddMMyyyy"));
                await ExecuteExtractionForDate(_forcedDate.Value, stoppingToken);
                _logger.LogInformation("Exécution forcée terminée. Arrêt du service.");
                return;
            }

            // Sinon, exécution planifiée quotidienne
            while (!stoppingToken.IsCancellationRequested) {
                try {
                    // ********************************************** //
                    // Calcul du prochain horaire d'exécution à 00h01 //
                    // ********************************************** //
                    //
                    DateTime now = DateTime.Now;
                    DateTime nextRunTime = new DateTime(now.Year, now.Month, now.Day, 0, 1, 0);
                    if (now >= nextRunTime) {
                        nextRunTime = nextRunTime.AddDays(1);
                    }

                    TimeSpan delay = nextRunTime - now;
                    _logger.LogInformation("Prochaine exécution prévue dans {Delay} à {NextRunTime}.", delay, nextRunTime);

                    // Attendre jusqu'au prochain horaire (avec gestion de l'annulation)
                    await Task.Delay(delay, stoppingToken);

                    // Exécuter l'extraction pour la date j+1
                    DateTime targetDate = DateTime.Today.AddDays(1);
                    await ExecuteExtractionForDate(targetDate, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'exécution du téléchargement quotidien des données PMU.");
                    // En cas d'erreur, attendre 1 minute avant de retenter
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("Service Ordonanceur arrêté.");
        }

        /// <summary>
        /// Méthode d'extraction pour une date donnée.
        /// À la fin du traitement, un courriel récapitulatif est envoyé.
        /// </summary>
        /// <param name="targetDate">Date pour laquelle extraire les données.</param>
        /// <param name="token">Token d'annulation.</param>
        private async Task ExecuteExtractionForDate(DateTime targetDate, CancellationToken token)
        {
            string dateStr = targetDate.ToString("ddMMyyyy");
            string discipline = string.Empty;
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
                    await dbSvc.SaveOrUpdateReunionAsync(reunion, updateColumns: true);
                }

                // Pour chaque course parsée, effectue un upsert et met à jour l'âge moyen.
                foreach (var course in programmeParsed.Courses)
                {
                    await dbSvc.SaveOrUpdateCourseAsync(course, updateColumns: true);
                }

                _logger.LogInformation("Les données Réunions et Courses ont été enregistrées dans la base de données via DbService.");
            }

            // ******************************************* //
            // FRANCE-GALOP : Paramètre annuel (year=AAAA) //
            // ******************************************* //
            //
            string annee = targetDate.ToString("yyyy");

            // **************************************************************************************************************** //
            // FRANCE-GALOP : Chargement des entraineurs - typeIndividu=Entraineur                                              //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Entraineur&year=2025 //
            //       &specialty=0&racetrack=null&category=6&nbResult=1000                                                       //
            // **************************************************************************************************************** //
            //
            var entGalop = new ListeParticipants();
            entGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync("Entraineur", annee);
            _logger.LogInformation("Extraction du classement des entraineurs galop terminée.");

            // ************************************************************************************************************ //
            // FRANCE-GALOP : Chargement des jockeys - typeIndividu=Entraineur                                              //
            // Url : https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu=Jockey&year=2025 //
            //       &specialty=0&racetrack=null&category=6&nbResult=1000                                                   //
            // ************************************************************************************************************ //
            //
            var jokGalop = new ListeParticipants();
            jokGalop.EntraineurJokeys = await ExtractEntraineurJockeyGalopRankingAsync("Jockey", annee);
            _logger.LogInformation("Extraction du classement des jockeys galop terminée.");

            // ********************************************************************************************************************* //
            // LE TROT : Chargement des entraineurs                                                                                  //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=1000&ranking_type=ENTR-NEW&sort_by=rank&order_by=asc //
            // ********************************************************************************************************************* //
            //
            var entTrot = new ListeParticipants();
            entTrot.EntraineurJokeys = await ExtractEntraineurJockeyTrotRankingAsync("ENTR");
            _logger.LogInformation("Extraction du classement des entraineurs trot terminée.");

            // ********************************************************************************************************************* //
            // LE TROT : Chargement des jockeys - typeIndividu=Entraineur                                                            //
            // Url : https://www.letrot.com/v1/api/rankings/person?page=1&limit=1000&ranking_type=COOR-NEW&sort_by=rank&order_by=asc //
            // ********************************************************************************************************************* //
            //
            var jokTrot = new ListeParticipants();
            jokTrot.EntraineurJokeys = await ExtractEntraineurJockeyTrotRankingAsync("COOR");
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

            var reunions = await dbService.GetReunionsByDateAsync(targetDate);
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
                    if (course.NumCourse == 1) { discipline = course.Discipline; }
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
                    await dbService.SaveOrUpdateChevauxAsync(participantsParsed.Chevaux, updateColumns: true);
                    _logger.LogInformation($"Détail de course enregistré pour la course n° {numCourse} de la réunion n° {numReunion}");

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

                // ***************************************************************************** //
                // Récupération des entraîneurs et jockeys d'une réunion dans la base de données //
                // ***************************************************************************** //
                //
                List<string> listeEntraineurs = await GetEntraineursOrJockeysAsync(numGeny, "Entraineur");
                List<string> listeJockeys = await GetEntraineursOrJockeysAsync(numGeny, "Jokey");

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
                    string subjectPrefix = "ApiPMU Fin de traitement";
                    string log = "Traitement terminé avec succès."; // Vous pouvez construire ce log en fonction des traitements effectués
                    string serveur = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "PRESLESMU";
                    var courrielService = new CourrielService(
                         _serviceProvider.GetRequiredService<ILogger<CourrielService>>(),
                         _connectionString);
                    await courrielService.SendCompletionEmailAsync(targetDate, flagTRT, subjectPrefix, log, serveur.ToLower(), dbContext);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi du courriel récapitulatif.");
            }
        }

        /// <summary>
        /// Méthode d'extraction des entraineurs et jockeys sur le site france-galop.
        /// si pas de données, on essaie l'annee precedente.
        /// </summary>
        /// <param name="typeIndividu">Extraction pour les jockeys ou les entraineurs.</param>
        /// <param name="annee">Année des statistiques.</param>
        private async Task<ICollection<EntraineurJokey>> ExtractEntraineurJockeyGalopRankingAsync(string typeIndividu, string annee)
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");

            int retryCount = 2;  // Nombre de tentatives (année actuelle et année précédente)
            int attempt = 0;
            bool dataFound = false;

            while (attempt < retryCount && !dataFound)
            {
                string currentYear = (int.Parse(annee) - attempt).ToString(); // Essai avec l'année, puis année-1
                string url = $"https://www.france-galop.com/fr/frglp-global/ajax?module=individu_filter&typeIndividu={typeIndividu}&year={currentYear}&specialty=0&racetrack=null&category=6&nbResult=1000";

                try
                {
                    string response = await client.GetStringAsync(url);
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(response);

                    var rows = doc.DocumentNode.SelectNodes("//tr");

                    if (rows == null)
                    {
                        _logger.LogWarning($"Aucun classement de {typeIndividu} trouvé pour l'année {currentYear}. Réessai avec {int.Parse(annee) - 1}...");
                        attempt++;
                        continue; // Passe à l'année précédente
                    }

                    ListeParticipants participants = new ListeParticipants();

                    foreach (var row in rows)
                    {
                        var cols = row.SelectNodes("td");

                        if (cols != null && cols.Count > 10)
                        {
                            string nomIndividu = cols[1].InnerText.Trim().ToUpper();

                            EntraineurJokey entJok = new EntraineurJokey
                            {
                                NumGeny = string.Empty,
                                Entjok = char.ToUpper(typeIndividu[0]).ToString(),
                                Nom = nomIndividu,
                                NbCourses = int.Parse(cols[3].InnerText.Trim()),
                                NbVictoires = int.Parse(cols[4].InnerText.Trim()),
                                NbCR = short.Parse(cols[5].InnerText.Trim()),
                                Ecart = 0
                            };

                            participants.EntraineurJokeys.Add(entJok);
                            dataFound = true;
                        }
                    }

                    _logger.LogInformation($"Extraction terminée pour {typeIndividu} en {currentYear} avec {participants.EntraineurJokeys.Count} entrées.");
                    return participants.EntraineurJokeys;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erreur lors de l'extraction du classement des {typeIndividu}s ({currentYear}) : {ex.Message}");
                    break; // Stoppe la boucle en cas d'erreur réseau ou serveur
                }
            }

            if (!dataFound)
            {
                _logger.LogError($"Aucune donnée trouvée pour {typeIndividu} en {annee} et {int.Parse(annee) - 1}. Annulation de l'extraction.");
            }

            // Retourne une collection vide si aucune donnée n'a été trouvée ou en cas d'erreur.
            return new List<EntraineurJokey>();
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

            string url = $"https://www.letrot.com/v1/api/rankings/person?page=1&limit=1000&ranking_type={typeIndividu}-NEW&sort_by=rank&order_by=asc";

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

                foreach (var ranking in rankings)
                {
                    EntraineurJokey entJok = new EntraineurJokey
                    {
                        NumGeny = string.Empty,
                        Entjok = char.ToUpper(typeIndividu[0]).ToString(),
                        Nom = ranking.PersonLabel.ToUpper(),
                        NbCourses = ranking.NbRaces,
                        NbVictoires = ranking.NbVictories,
                        NbCR = 0, // Valeur par défaut (pas disponible dans le JSON)
                        Ecart = 0
                    };

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
        private async Task<List<string>> GetEntraineursOrJockeysAsync(string numGeny, string type)
        {
            List<string> noms = new List<string>();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();

                string query = type == "Entraineur"
                    ? "SELECT DISTINCT(entraineur) FROM chevaux WHERE NumGeny = @numGeny"
                    : "SELECT DISTINCT(jokey) FROM chevaux WHERE NumGeny = @numGeny";

                using var command = dbContext.Database.GetDbConnection().CreateCommand();
                command.CommandText = query;
                command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@numGeny", numGeny));

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
