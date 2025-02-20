using System.Globalization;
using Microsoft.Extensions.Logging;
using ApiPMU.Services;
using ApiPMU.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ApiPMU.Parsers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

        public Ordonanceur(IApiPmuService apiPmuService,
                           ILogger<Ordonanceur> logger,
                           IServiceProvider serviceProvider)
        {
            _apiPmuService = apiPmuService;
            _logger = logger;
            _serviceProvider = serviceProvider;
            
            // ************************************************* //
            //      (utilisable uniquement en mode débogage)     //
            // Paramétrage de la date du programme à télécharger //
            // ************************************************* //
            
#if DEBUG
            _forcedDate = DateTime.ParseExact("17022025", "ddMMyyyy", CultureInfo.InvariantCulture);
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
            _logger.LogInformation("Début du téléchargement des données pour la date {DateStr}.", dateStr);

            // *********************************************** //
            // Api PMU : Chargement du programme de la journée //
            // *********************************************** //
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
                var parser = new ProgrammeParser(connectionString);
                programmeParsed = parser.ParseProgramme(programmeJson, dateStr);
            }
            _logger.LogInformation("Programme parsé avec {CountReunions} réunions et {CountCourses} courses.",
                                   programmeParsed.Reunions.Count, programmeParsed.Courses.Count);

            // **************************************************** //
            // BDD : Enregistrement des données réunions et courses //
            // **************************************************** //
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

            // ********************************************************************* //
            // BDD : Lecture des réunions et courses enregistrées pour cette journée //
            // ********************************************************************* //
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
            // ************************** //
            // Itération sur les réunions // 
            // ************************** //
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
                // ************************* //
                // Itération sur les courses // 
                // ************************* //
                //
                foreach (var course in courses)
                {
                    short numCourse = course.NumCourse;
                    string disc = course.Discipline;
                    _logger.LogInformation($"Chargement du détail pour la course n° {numCourse} de la réunion n° {numReunion}");

                    // ************************************************** //
                    // Api PMU : Chargement des participants d'une course //
                    // ************************************************** //
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
                        var parser = new ParticipantsParser(connectionString);
                        participantsParsed = parser.ParseParticipants(courseJson, numGeny, numReunion, numCourse, disc);
                    }
                    _logger.LogInformation("Course parsé avec {CountChevaux}.", participantsParsed.Chevaux.Count);

                    // *************************************************** //
                    // BDD : Enregistrement des données courses et chevaux //
                    // *************************************************** //
                    //
                    await dbService.SaveOrUpdateChevauxAsync(participantsParsed.Chevaux, updateColumns: true);
                    _logger.LogInformation($"Détail de course enregistré pour la course n° {numCourse} de la réunion n° {numReunion}");

                    // ********************************************************** //
                    // Mise à jour de l'âge moyen des participants dans la course //
                    // ********************************************************** //
                    //
                    await dbService.UpdateCourseAgeMoyenAsync(numGeny, numCourse);
                    _logger.LogInformation($"Age moyen mis à jour pour la course Numero : {numCourse} de la réunion NumGeny : {numGeny}");

                    // ************************* //
                    // Itération sur les chevaux // 
                    // ************************* //
                    //
                    foreach (var cheval in participantsParsed.Chevaux)
                    {
                        _logger.LogInformation($"Chargement de l'historique des chevaux pour la course n° {numCourse} de la réunion n° {numReunion}");

                        // ****************************************************************** //
                        // Api PMU : Chargement de l'historique des participants d'une course //
                        // ****************************************************************** //
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
                            var parser = new PerformancesParser(connectionString);
                            performancesParsed = parser.ParsePerformances(performancesJson, disc);
                        }
                        _logger.LogInformation("Performances parsées avec {CountPerformances} entrées.", performancesParsed.Performances.Count);

                        // ************************************************************************** //
                        // Compléter les performances à partir du programme des dates de performances //
                        // ************************************************************************** //
                        //
                        // Pour chaque performance historique, associer le programme correspondant
                        foreach (var perf in performancesParsed.Performances)
                        {
                            // Utiliser la date propre à la performance pour charger le programme du jour correspondant
                            string perfDateStr = perf.DatePerf.ToString("ddMMyyyy");
                            // Video contient temporairement le nom du prix (nomPrix)
                            // Pour la recherche de la course historique d'un partant
                            string nomPrixTemp = perf.Video;
                            // Pour la recherche du cheval dans la course
                            string nomCh = perf.Nom;

                            // ************************************************************************ //
                            // Api PMU : Charger le programme correspondant à la date de la performance //
                            // ************************************************************************ //
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
                            if (Jsonprog == null) {  Console.WriteLine("La clé 'programme' est absente du programmeJsonForPerfJson.");  }
                            JToken? Jsonreun = Jsonprog["reunions"];
                            if (Jsonreun == null) { Console.WriteLine("La clé 'reunions' est absente du programmeJsonForPerfJson."); }

                            foreach (JToken reunionToken in Jsonreun)
                            {
                                if (reunionToken["courses"] != null)
                                {
                                    JToken? Jsoncour = reunionToken["courses"];
                                    if (courses != null)
                                    {
                                        foreach (JToken courseToken in Jsoncour)
                                        {
                                            string? libelle = courseToken["libelle"]?.ToString();
                                            if (libelle == nomPrixTemp)
                                            {
                                                numReunion = (short)(short.TryParse(courseToken["numReunion"]?.ToString(), out short nr) ? nr : 0);
                                                numCourse = (short)(short.TryParse(courseToken["numExterne"]?.ToString(), out short nc) ? nc : 0);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            // ************************************************** //
                            // Api PMU : Chargement des participants d'une course //
                            // ************************************************** //
                            //
                            var courseJsonForPerfData = await _apiPmuService.ChargerCourseAsync<dynamic>(perfDateStr, numReunion, numCourse, "participants");

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
                            if (Jsondcou == null) { Console.WriteLine("La clé 'participants' est absente du courseJsonForPerfJson."); }

                            foreach (JToken dcourseToken in Jsondcou)
                            {
                                string? nom = dcourseToken["nom"]?.ToString();
                                if (nomCh == nom)
                                {
                                    perf.Gains = (int)(int.TryParse(dcourseToken["numReunion"]?.ToString(), out int nr) ? nr : 0);
                                    perf.Cordage = "GAUCHE"; // A rechercher dans la liste des hippodromes ou dans le programme de la journée (DatePerf)
                                    perf.TypeCourse = "F"; // A rechercher dans le programme de la journée (DatePerf)
                                    perf.Cote = 0; // A rechercher dans le programme de la journée (DatePerf)
                                    perf.Deferre = string.Empty;
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
                                    perf.Avis = string.Empty;
                                    perf.Video = dcourseToken?["nomPrix"]?.ToString() ?? string.Empty;
                                    break;
                                }
                            }

                            // ************************************************* //
                            // BDD : Enregistrement des performances des chevaux //
                            // ************************************************* //
                            //
                            await dbService.SaveOrUpdatePerformanceAsync(performancesParsed.Performances, updateColumns: true);
                        _logger.LogInformation($"Performances enregistrées pour la course n° {numCourse} de la réunion n° {numReunion}");
                    }
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
                    string subjectPrefix = "ApiPMU Fin de traitement";
                    string log = "Traitement terminé avec succès."; // Vous pouvez construire ce log en fonction des traitements effectués
                    string serveur = Environment.GetEnvironmentVariable("COMPUTERNAME") ?? "preslesmu";
                    await EmailService.SendCompletionEmailAsync(targetDate, flagTRT, subjectPrefix, log, serveur, dbContext);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'envoi du courriel récapitulatif.");
            }
        }
    }
}
