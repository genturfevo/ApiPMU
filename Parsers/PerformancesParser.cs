using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient; // Veillez à installer le package NuGet Microsoft.Data.SqlClient.
using Newtonsoft.Json.Linq;
using ApiPMU.Models;
using System.Diagnostics.Eventing.Reader;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Parser qui transforme le JSON de l'API PMU en instances de modèles métier.
    /// </summary>
    public class PerformancesParser
    {
        private readonly ILogger<PerformancesParser> _logger;
        private readonly string _connectionString;

        public PerformancesParser(ILogger<PerformancesParser> logger, string connectionString)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : throw new ArgumentException("La chaîne de connexion est obligatoire.", nameof(connectionString));
        }

        // Dictionnaire pour ajuster manuellement certaines correspondances entre hippodromes.
        private static readonly Dictionary<string, string> Correspondances = new Dictionary<string, string>
        {
            { "AGEN LA GARENNE", "AGEN LE PASSAGE" },
            { "MARSEILLE (A BORELY)", "MARSEILLE BORELY" },
            { "MARSEILLE PONT DE VIVAUX", "MARSEILLE VIVAUX" },
            { "MARSEILLE PONT DE VIVAUX MIDI", "MARSEILLE VIVAUX" },
            { "MARSEILLE (A VIVAUX)", "MARSEILLE VIVAUX" },
            { "LA CEPIERE", "TOULOUSE" },
            { "LA TESTE BASSIN ARCACHON", "LA TESTE DE BUCH" },
            { "LE MONT SAINT MICHEL PONTORSON", "LE MONT SAINT MICHEL" },
            { "LYON (A LA SOIE)", "LYON LA SOIE" },
            { "LYON (A PARILLY)", "LYON PARILLY" },
            { "MAUQUENCHY", "ROUEN MAUQUENCHY" },
            { "VIRE NORMANDIE", "VIRE" }
        };

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
                _logger.LogError("La clé 'reunions' est absente du JSON.");
                return performancesResult;
            }

            try
            {
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
                _logger.LogError("La clé 'coursesCourues' est absente du JSON.");
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
                string libelle = course?["hippodrome"]?.ToString().ToUpper() ?? "INCONNU";
                string lieu = NormalizeString(libelle);
                if (Correspondances.ContainsKey(lieu)) { lieu = Correspondances[lieu]; }
                // Récupération et traitement de la discipline
                string? discipline = course["discipline"] != null ? course["discipline"].ToString().ToUpper() : "INCONNUE";
                if (discipline.Contains("HAIE")) { discipline = "HAIES"; }
                discipline = disciplinesAutorisees.Any(d => discipline.Contains(d)) ? disciplinesAutorisees.FirstOrDefault(d => discipline.Contains(d)) : "INCONNUE";
                int allocation = course?["allocation"]?.Value<int>() ?? 0;
                short partants = course?["nbParticipants"]?.Value<short>() ?? 0;
                JToken? participants = course?["participants"]?.FirstOrDefault(p => p["itsHim"]?.Value<bool>() == true);
                int gains = 0; // A rechercher dans le programme de la journée (DatePerf)
                string corde = string.IsNullOrEmpty(participants?["corde"]?.ToString()) ? "0" : participants["corde"].ToString();
                string cordage = string.Empty; // A rechercher dans la liste des hippodromes ou dans le programme de la journée (DatePerf)
                string typeCourse = "G"; // A rechercher dans le programme de la journée (DatePerf)
                float poid = participants?["poidsJockey"]?.Value<float?>() ?? 0f;
                float cote = 0; // A rechercher dans le programme de la journée (DatePerf)
                JToken? jplace = participants?["place"];
                string place = (jplace?["place"]?.Value<int>() > 0 && jplace?["place"]?.Value<int>() < 10) ? jplace["place"].ToString() : "0";
                //
                // Colonnes variables selon la discipline
                //
                Single dist = 0;
                string deferre = string.Empty;
                string redKDist = string.Empty;
                if (disc == "ATTELE" || disc == "MONTE")
                {
                    dist = participants?["distanceParcourue"]?.Value<Single>() ?? 0;
                    long centisecondes = participants["reductionKilometrique"]?.Value<long>() ?? 0;
                    TimeSpan temps = TimeSpan.FromSeconds(centisecondes / 100.0);
                    redKDist = (temps.ToString(@"m\:ss\.f")).Replace(".", "::");
                }
                // avis_1.png : vert, avis_2.png : jaune, avis_3.png : rouge
                string avis = string.Empty;
                string video = course?["nomPrix"]?.ToString() ?? string.Empty;
                return new Performance
                {
                    Nom = nom,
                    DatePerf = datePerf,
                    Lieu = lieu,
                    Dist = dist,
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
        /// <summary>
        /// Convertit une chaîne en majuscules, remplace les apostrophes par des espaces et supprime les accents.
        /// </summary>
        /// <param name="input">Chaîne à normaliser.</param>
        /// <returns>Chaîne normalisée (non nulle).</returns>
        private string NormalizeString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
            string result = input.ToUpperInvariant().Replace("'", " ").Replace("-", " ");
            result = RemoveAccents(result);
            return result;
        }
        /// <summary>
        /// Supprime les accents d'une chaîne.
        /// </summary>
        /// <param name="text">Texte à traiter.</param>
        /// <returns>Texte sans accents.</returns>
        private string RemoveAccents(string text)
        {
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in normalizedString)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
