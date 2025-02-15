using System.Globalization;
using Microsoft.Extensions.Logging;
using ApiPMU.Services;
using ApiPMU.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ApiPMU.Parsers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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
            _forcedDate = DateTime.ParseExact("16022025", "ddMMyyyy", CultureInfo.InvariantCulture);
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
            // *********************************************** //
            // Api PMU : Chargement du programme de la journée //
            // *********************************************** //
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
            Programme programmeParsed;
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
                var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();

                // Ajout des réunions et courses dans la base de données.
                // Selon votre logique, vous pouvez vérifier l'existence d'enregistrements pour éviter les doublons.
                dbContext.Reunions.AddRange(programmeParsed.Reunions);
                dbContext.Courses.AddRange(programmeParsed.Courses);

                await dbContext.SaveChangesAsync(token);
                _logger.LogInformation("Les données Réunions et Courses ont été enregistrées dans la base de données.");
            }

            // ********************************************************************* //
            // ********************************************************************* //
            // BDD : Lecture des réunions et courses enregistrées pour cette journée //
            // ********************************************************************* //
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
                    Participants participantsParsed;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApiPMUDbContext>();
                        string connectionString = dbContext.Database.GetDbConnection().ConnectionString;
                        var parser = new ParticipantsParser(connectionString);
                        participantsParsed = parser.ParseParticipants(courseJson, numGeny, numReunion, numCourse, disc);
                    }
                    _logger.LogInformation("Course parsé avec {CountChevaux}.", participantsParsed.Chevaux.Count);

                    // Enregistrement du détail dans la base
                    await dbService.SaveCourseChevauxAsync(numGeny, numCourse, participantsParsed.Chevaux);
                    _logger.LogInformation($"Détail de course enregistré pour la course n° {numCourse} de la réunion n° {numReunion}");
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
