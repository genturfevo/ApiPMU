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
    public class ParticipantsParser
    {
        private readonly string _connectionString;
        private string numGeny= string.Empty;

        /// <summary>
        /// Constructeur nécessitant la chaîne de connexion (pour la vérification en BDD).
        /// </summary>
        /// <param name="connectionString">Chaîne de connexion SQL.</param>
        public ParticipantsParser(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("La chaîne de connexion ne peut être nulle ou vide.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Parse l'intégralité du JSON et retourne un objet Programme regroupant réunions et courses.
        /// </summary>
        /// <param name="json">Chaîne JSON à parser.</param>
        /// <param name="dateStr">Paramètre additionnel (ex. date d'extraction) – non utilisé ici mais conservé pour la signature.</param>
        /// <returns>Un objet Programme contenant les listes de réunions et de courses.</returns>
        public Participants ParseParticipants(string json, string numGeny, short numReunion, short numCourse, string disc)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            Participants participantsResult = new Participants();
            JObject data = JObject.Parse(json);
            JToken? participants = data["participants"];
            if (participants == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return participantsResult;
            }

            foreach (JToken chevalToken in participants)
            {
                Cheval? chevalObj = ProcessCheval(chevalToken, numGeny, numReunion, numCourse, disc);
                if (chevalObj != null)
                {
                    participantsResult.Chevaux.Add(chevalObj);
                }
            }
            return participantsResult;
        }

        /// <summary>
        /// Transforme un token JSON en une instance de Reunion.
        /// </summary>
        /// <param name="participants">Token JSON de la réunion.</param>
        /// <returns>Instance de Reunion ou null en cas d'erreur.</returns>
        private Cheval? ProcessCheval(JToken participants, string numGeny, short numReunion, short numCourse, string disc)
        {
            try
            {
                short numero = participants?["numPmu"]?.Value<short>() ?? 0;
                string nom = participants?["nom"]?.ToString() ?? string.Empty;
                JToken? gainsCarriere = participants?["gainsParticipant"];
                int gains = 0;
                if (gainsCarriere != null && gainsCarriere["gainsCarriere"] != null && int.TryParse(gainsCarriere["gainsCarriere"].ToString(), out int g)) {gains = g / 100;}
                string corde = participants?["placeCorde"]?.ToString() ?? "0";
                string sexe = participants?["sexe"]?.ToString().FirstOrDefault().ToString() ?? "H";
                string age = participants?["age"]?.ToString() ?? "0";
                string jokey = participants?["driver"]?.ToString() ?? "NON PARTANT";
                string entraineur = participants?["entraineur"]?.ToString() ?? "NON PARTANT";
                string zoneABC = numero < 7 ? "A" : numero < 13 ? "B" : "C";
                int nbCourses = participants?["nombreCourses"]?.Value<int>() ?? 0;
                int nbVictoires = participants?["nombreVictoires"]?.Value<int>() ?? 0;
                int nbPlaces = participants?["nombrePlaces"]?.Value<int>() ?? 0;
                //
                // Colonnes variables selon la discipline
                //
                Single distpoid = 0;
                string deferre = "0";
                if (disc == "ATTELE" || disc == "MONTE")
                {
                    distpoid = participants?["handicapDistance"]?.Value<Single>() ?? 0;
                    deferre = participants?["deferre"]?.ToString() switch {
                        "DEFERRE_ANTERIEURS" => "DA",
                        "DEFERRE_POSTERIEURS" => "DP",
                        "DEFERRE_ANTERIEURS_POSTERIEURS" => "D4",
                        "PROTEGE_ANTERIEURS" => "PA",
                        "PROTEGE_POSTERIEURS" => "PP",
                        "PROTEGE_ANTERIEURS_POSTERIEURS" => "P4",
                        "PROTEGE_ANTERIEURS_DEFERRRE_POSTERIEURS" => "PA DP",
                        "DEFERRRE_ANTERIEURS_PROTEGE_POSTERIEURS" => "DA PP",
                        _ => "0"
                    };
                }
                else
                {
                    distpoid = (Single)Math.Floor((participants?["poidsConditionMonte"]?.Value<Single>() ?? 0) / 10);
                    deferre = participants?["handicapValeur"]?.ToString() ?? "0";
                }
                // avis_1.png : vert, avis_2.png : jaune, avis_3.png : rouge
                string avisEntraineur = participants?["avisEntraineur"]?.ToString() ?? string.Empty;
                string avis = avisEntraineur switch {
                    "POSITIF" => "avis_1.png",
                    "NEUTRE" => "avis_2.png",
                    "NEGATIF" => "avis_3.png",
                    _ => string.Empty
                };
                return new Cheval
                {
                    NumGeny = numGeny,
                    NumCourse = numCourse,
                    Numero = numero,
                    Nom = nom,
                    Corde = corde,
                    SexAge = sexe + age,
                    DistPoid = distpoid,
                    Deferre = deferre,
                    Jokey = jokey,
                    Entraineur = entraineur,
                    Gains = gains,
                    ZoneABC = zoneABC,
                    NbCourses = nbCourses,
                    NbVictoires = nbVictoires,
                    NbPlaces = nbPlaces,
                    Avis = avis,
                    DateModif = DateTime.Now
                };
            }
            catch { return null; }
        }
    }
}
