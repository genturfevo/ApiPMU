using System.Globalization;
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
        /// Parse l'intégralité du JSON et retourne un objet de 5 Performances par cheval
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

            foreach (JToken perfToken in perfs)
            {
                Performance? perfObj = ProcessPerformances(perfToken, disc);
                if (perfObj != null)
                {
                    performancesResult.Performances.Add(perfObj);
                }
            }
            return performancesResult;
        }

        /// <summary>
        /// Transforme un token JSON en une instance de Reunion.
        /// </summary>
        /// <param name="performances">Token JSON de la réunion.</param>
        /// <returns>Instance de Reunion ou null en cas d'erreur.</returns>
        private Performance? ProcessPerformances(JToken performances, string disc)
        {
            try
            {
                DateTime datePerf = performances?["numPmu"]?.Value<DateTime>() ?? DateTime.MinValue;
                string nom = performances?["nom"]?.ToString() ?? string.Empty;
                JToken? gainsCarriere = performances?["gainsParticipant"];
                int gains = 0;
                if (gainsCarriere != null && gainsCarriere["gainsCarriere"] != null && int.TryParse(gainsCarriere["gainsCarriere"].ToString(), out int g)) { gains = g / 100; }
                string corde = performances?["placeCorde"]?.ToString() ?? "0";
                string lieu = performances?["sexe"]?.ToString().FirstOrDefault().ToString() ?? "H";
                string cordage = performances?["age"]?.ToString() ?? "0";
                string discipline = performances?["driver"]?.ToString() ?? "NON PARTANT";
                string typeCourse = performances?["entraineur"]?.ToString() ?? "NON PARTANT";
                short partants = performances?["nombreCourses"]?.Value<short>() ?? 0;
                float poid = performances?["nombreVictoires"]?.Value<float>() ?? 0;
                float cote = performances?["nombreVictoires"]?.Value<float>() ?? 0;
                int allocation = performances?["nombrePlaces"]?.Value<int>() ?? 0;
                string place = performances?["nombrePlaces"]? .ToString() ?? string.Empty;
                //
                // Colonnes variables selon la discipline
                //
                Single distpoid = 0;
                string deferre = string.Empty;
                if (disc == "ATTELE" || disc == "MONTE")
                {
                    distpoid = performances?["handicapDistance"]?.Value<Single>() ?? 0;
                    deferre = performances?["deferre"]?.ToString() switch
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
                    distpoid = (Single)Math.Floor((performances?["poidsConditionMonte"]?.Value<Single>() ?? 0) / 10);
                    if (distpoid == 0) { distpoid = (Single)Math.Floor((performances?["handicapPoids"]?.Value<Single>() ?? 0) / 10); }
                    deferre = performances?["handicapValeur"]?.ToString() ?? string.Empty;
                }
                // avis_1.png : vert, avis_2.png : jaune, avis_3.png : rouge
                string redKDist = performances?["avisEntraineur"]?.ToString() ?? string.Empty;
                string avis = performances?["avisEntraineur"]?.ToString() ?? string.Empty;
                string video = performances?["avisEntraineur"]?.ToString() ?? string.Empty;
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
