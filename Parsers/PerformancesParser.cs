﻿using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient; // Veillez à installer le package NuGet Microsoft.Data.SqlClient.
using Newtonsoft.Json.Linq;
using ApiPMU.Models;
using System.Diagnostics.Eventing.Reader;

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Parser qui transforme le JSON de l'API PMU en instances de modèles métier.
    /// </summary>
    public class PerformancesParser
    {
        private readonly string _connectionString;
        private string numGeny= string.Empty;

        /// <summary>
        /// Constructeur nécessitant la chaîne de connexion (pour la vérification en BDD).
        /// </summary>
        /// <param name="connectionString">Chaîne de connexion SQL.</param>
        public PerformancesParser(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("La chaîne de connexion ne peut être nulle ou vide.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Parse l'intégralité du JSON et retourne une liste de 5 Performances pour chaque cheval.
        /// </summary>
        /// <param name="json">Chaîne JSON à parser.</param>
        /// <returns>Un objet Performances contenant la liste de perfs des chevaux.</returns>
        public ListeParticipants ParsePerformances(string json, string disc)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            ListeParticipants performancesResult = new ListeParticipants();
            JObject data = JObject.Parse(json);
            JToken? perfs = data["participants"];
            if (perfs == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return performancesResult;
            }

            try
            {
                // On s'attend à ce que "coursesCourues" soit un tableau JSON contenant l'historique des courses.
                var partantsArray = perfs as JArray;
                if (partantsArray != null)
                {
                    foreach (JToken? partant in partantsArray)
                    {
                        string nom = partant?["nomCheval"]?.ToString() ?? string.Empty;
                        JToken? coursesCourues = partant?["coursesCourues"];
                        if (coursesCourues != null)
                        {
                            foreach (JToken? course in coursesCourues)
                            {
                                Performance? perfObj = ProcessPerformances(course, disc, nom);
                                if (perfObj != null)
                                {
                                    performancesResult.Performances.Add(perfObj);
                                }
                            }
                        }
                    }
                }
            }catch
            {
                Console.WriteLine("La clé 'coursesCourues' est absente du JSON.");
            }
            return performancesResult;
        }
        /// <summary>
        /// Transforme un token JSON en une instance de Reunion.
        /// </summary>
        /// <param name="performances">Token JSON de la réunion.</param>
        /// <returns>Instance de Reunion ou null en cas d'erreur.</returns>
        private Performance? ProcessPerformances(JToken course, string disc, string nom)
        {
            try
            {
                // Liste des valeurs autorisées pour les disciplines
                var disciplinesAutorisees = new HashSet<string> {"ATTELE", "MONTE", "PLAT", "HAIES", "STEEPLE", "CROSS"};
                // Récupération des valeurs des deux propriétés (ou chaîne vide si null)
                long timestamp = long.TryParse(course?["date"]?.ToString(), out long ts) ? ts : 0;
                DateTime datePerf = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                string lieu = course?["hippodrome"]?.ToString().ToUpper() ?? "INCONNU";
                // Récupération et traitement de la discipline
                string? discipline = course["discipline"] != null ? course["discipline"].ToString().ToUpper() : "INCONNUE";
                if (discipline.Contains("HAIE")) { discipline = "HAIES"; }
                discipline = disciplinesAutorisees.Any(d => discipline.Contains(d)) ? disciplinesAutorisees.FirstOrDefault(d => discipline.Contains(d)) : "INCONNUE";
                int allocation = course?["allocation"]?.Value<int>() ?? 0;
                short partants = course?["nbParticipants"]?.Value<short>() ?? 0;
                JToken? participants = course?["participants"]?.FirstOrDefault(p => p["itsHim"]?.Value<bool>() == true);
                int gains = 0; // A rechercher dans le programme de la journée (DatePerf)
                string corde = string.IsNullOrEmpty(participants?["corde"]?.ToString()) ? "0" : participants["corde"].ToString();
                string cordage = "GAUCHE"; // A rechercher dans la liste des hippodromes ou dans le programme de la journée (DatePerf)
                string typeCourse = "F"; // A rechercher dans le programme de la journée (DatePerf)
                float poid = participants?["poidsJockey"]?.Value<float?>() ?? 0f;
                float cote = 0; // A rechercher dans le programme de la journée (DatePerf)
                JToken? jplace = participants?["place"];
                string place = (jplace?["place"]?.Value<int>() > 0 && jplace?["place"]?.Value<int>() < 10) ? jplace["place"].ToString() : "0";
                //
                // Colonnes variables selon la discipline
                //
                Single distpoid = 0;
                string deferre = string.Empty;
                string redKDist = string.Empty;
                if (disc == "ATTELE" || disc == "MONTE")
                {
                    distpoid = participants?["distanceParcourue"]?.Value<Single>() ?? 0;
                    //deferre = participants?["deferre"]?.ToString() switch
                    //{
                    //    "DEFERRE_ANTERIEURS" => "DA",
                    //    "DEFERRE_POSTERIEURS" => "DP",
                    //    "DEFERRE_ANTERIEURS_POSTERIEURS" => "D4",
                    //    "PROTEGE_ANTERIEURS" => "PA",
                    //    "PROTEGE_POSTERIEURS" => "PP",
                    //    "PROTEGE_ANTERIEURS_POSTERIEURS" => "P4",
                    //    "PROTEGE_ANTERIEURS_DEFERRRE_POSTERIEURS" => "PA DP",
                    //    "DEFERRRE_ANTERIEURS_PROTEGE_POSTERIEURS" => "DA PP",
                    //    _ => string.Empty
                    //};
                    redKDist = participants?["reductionKilometrique"] != null
                        ? TimeSpan.FromMilliseconds(participants["reductionKilometrique"]?.Value<long>() ?? 0).ToString(@"m\:ss\.f")
                        : string.Empty;
                    redKDist = redKDist.Replace(".", "::");
                }
                else
                {
                    distpoid = (Single)Math.Floor((participants?["poidsJockey"]?.Value<Single>() ?? 0) / 10);
                    if (distpoid == 0) { distpoid = (Single)Math.Floor((participants?["poidsJockey"]?.Value<Single>() ?? 0) / 10); }
                    //deferre = participants?["handicapValeur"]?.ToString() ?? string.Empty;
                }
                // avis_1.png : vert, avis_2.png : jaune, avis_3.png : rouge
                string avis = string.Empty;
                string video = string.Empty;
                return new Performance
                {
                    Nom = nom,
                    DatePerf = datePerf,
                    Lieu = lieu,
                    Dist = distpoid,
                    Gains = gains,
                    Partants = partants,
                    Corde = corde,
                    Cordage = cordage,
                    Deferre = deferre,
                    Poid = poid,
                    Discipline = discipline,
                    TypeCourse = typeCourse,
                    Allocation = allocation,
                    Place = place,
                    RedKDist = redKDist,
                    Cote = cote,
                    Avis = avis,
                    Video = video,
                    DateModif = DateTime.Now
                };
            }
            catch { return null; }
        }
    }
}
