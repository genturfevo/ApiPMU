using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient; // Veillez à installer le package NuGet Microsoft.Data.SqlClient.
using Newtonsoft.Json.Linq;
using ApiPMU.Models;

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Parser qui transforme le JSON de l'API PMU en instances de modèles métier.
    /// </summary>
    public class ProgrammeParser
    {
        // Dictionnaire pour ajuster manuellement certaines correspondances entre libellés.
        private static readonly Dictionary<string, string> Correspondances = new Dictionary<string, string>
        {
            { "AGEN LA GARENNE", "AGEN LE PASSAGE" },
            { "CAGNES/MER", "CAGNES SUR MER" },
            { "CHATILLON/CHALARONNE", "CHATILLON SUR CHALARONNE" },
            { "LE LION D'ANGERS", "LE LION D ANGERS" },
            { "LYON-PARILLY", "LYON PARILLY" },
            { "MONS (GHLIN)", "MONS" },
            { "PONT CHATEAU", "PONTCHATEAU" },
            { "PONT DE VIVAUX", "MARSEILLE VIVAUX" },
            { "PARIS LONGCHAMP", "PARISLONGCHAMP" },
            { "PARIS-LONGCHAMP", "PARISLONGCHAMP" }
        };
        private readonly string _connectionString;
        private string numGeny= string.Empty;

        /// <summary>
        /// Constructeur nécessitant la chaîne de connexion (pour la vérification en BDD).
        /// </summary>
        /// <param name="connectionString">Chaîne de connexion SQL.</param>
        public ProgrammeParser(string connectionString)
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
        public Programme ParseProgramme(string json, string dateStr)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            Programme programmeResult = new Programme();
            JObject data = JObject.Parse(json);
            JToken? prog = data["programme"];
            if (prog == null)
            {
                Console.WriteLine("La clé 'programme' est absente du JSON.");
                return programmeResult;
            }
            JToken? reunions = prog["reunions"];
            if (reunions == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return programmeResult;
            }

            foreach (JToken reunionToken in reunions)
            {
                numGeny = "PMU" + dateStr + "R";
                Reunion? reunionObj = ProcessReunion(reunionToken);
                if (reunionObj != null)
                {
                    programmeResult.Reunions.Add(reunionObj);
                    JToken? courses = reunionToken["courses"];
                    if (courses != null)
                    {
                        foreach (JToken courseToken in courses)
                        {
                            // Construction d'un identifiant course sous forme de chaîne.
                            Course? courseObj = ProcessCourse(courseToken);
                            if (courseObj != null)
                            {
                                programmeResult.Courses.Add(courseObj);
                            }
                        }
                    }
                }
            }
            return programmeResult;
        }

        /// <summary>
        /// Transforme un token JSON en une instance de Reunion.
        /// </summary>
        /// <param name="reunion">Token JSON de la réunion.</param>
        /// <returns>Instance de Reunion ou null en cas d'erreur.</returns>
        private Reunion? ProcessReunion(JToken reunion)
        {
            try
            {
                // Conversion numérique avec int.TryParse.
                int numReunion = int.TryParse(reunion["numOfficiel"]?.ToString(), out int nr) ? nr : 0;
                numGeny = numGeny + numReunion;
                long timestamp = long.TryParse(reunion["dateReunion"]?.ToString(), out long ts) ? ts : 0;
                DateTime dateReunion = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                string formattedDateReunion = dateReunion.ToString("yyyy-MM-dd");
                JToken? hippodrome = reunion["hippodrome"];
                string libelleCourt = hippodrome?["libelleCourt"]?.ToString() ?? string.Empty;
                string normalizedLibelleCourt = NormalizeString(libelleCourt);
                if (Correspondances.ContainsKey(normalizedLibelleCourt)) { normalizedLibelleCourt = Correspondances[normalizedLibelleCourt]; }
                if (!IsLieuCoursePresent(normalizedLibelleCourt)) { return null; }
                return new Reunion {
                    NumGeny = numGeny,
                    NumReunion = numReunion,
                    LieuCourse = normalizedLibelleCourt,
                    DateReunion = dateReunion,
                    DateModif = DateTime.Now
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Transforme un token JSON en une instance de Course.
        /// </summary>
        /// <param name="course">Token JSON de la course.</param>
        /// <param name="numGeny">Identifiant de course généré (sous forme de chaîne).</param>
        /// <param name="reunionDateModif">Date de modification de la réunion (utilisée par défaut si absente dans la course).</param>
        /// <returns>Instance de Course ou null en cas d'erreur.</returns>
        private Course? ProcessCourse(JToken course)
        {
            try
            {
                int numCourse = int.TryParse(course["numExterne"]?.ToString(), out int nc) ? nc : 0;
                // Récupération des valeurs des deux propriétés (ou chaîne vide si null)
                string specialiteField = course["specialite"]?.ToString() ?? "";
                string disciplineField = course["discipline"]?.ToString() ?? "";
                // Combinaison des deux champs (séparés par un espace)
                string combinedValue = specialiteField + " " + disciplineField;
                // Utilisation du switch expression pour déterminer la discipline recherchée
                string discipline = combinedValue switch
                {
                    var s when s.Contains("ATTELE") => "ATTELE",
                    var s when s.Contains("MONTE") => "MONTE",
                    var s when s.Contains("PLAT") => "PLAT",
                    var s when s.Contains("HAIE") => "HAIES",
                    var s when s.Contains("STEEPLE") => "STEEPLE",
                    var s when s.Contains("CROSS") => "CROSS",
                    _ => string.Empty
                };
                long timestamp = long.TryParse(course["heureDepart"]?.ToString(), out long ts) ? ts : 0;
                DateTime dateReunion = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                string formattedDepart = dateReunion.ToString("HH:mm");
                JToken? paris = course["paris"];
                bool jCouples = paris is JArray parisArrayjC && parisArrayjC.Any(pari =>
                {
                    string type = pari["typePari"]?.ToString() ?? "";
                    return type.Contains("COUPLE_PLACE", StringComparison.OrdinalIgnoreCase);
                });
                bool jTrio = false;
                if (jCouples)
                {
                    jTrio = paris is JArray parisArrayTr && parisArrayTr.Any(pari =>
                    {
                        string type = pari["typePari"]?.ToString() ?? "";
                        return type.Contains("TRIO", StringComparison.OrdinalIgnoreCase);
                    });
                };
                bool jMulti = false;
                if (jCouples)
                {
                    jMulti = paris is JArray parisArrayjM && parisArrayjM.Any(pari =>
                    {
                        string type = pari["typePari"]?.ToString() ?? "";
                        return type.Contains("MULTI", StringComparison.OrdinalIgnoreCase);
                    });
                };
                bool jQuinte = false;
                if (jCouples)
                {
                    jQuinte = paris is JArray parisArrayjQ && parisArrayjQ.Any(pari =>
                    {
                        string type = pari["typePari"]?.ToString() ?? "";
                        return type.Contains("QUINTE", StringComparison.OrdinalIgnoreCase);
                    });
                };
                bool? autostart = course["categorieParticularite"]?.ToString().Contains("AUTOSTART");
                string cordage = course["corde"]?.ToString() switch
                {
                    string s when s.Contains("GAUCHE") => "GAUCHE",
                    string s when s.Contains("DROITE") => "DROITE",
                    _ => "GAUCHE"
                };
                int allocation = int.TryParse(course["montantPrix"]?.ToString(), out int alloc) ? alloc : 0;
                string conditions = course["conditions"]?.ToString() ?? string.Empty;
                if (conditions.Length > 242)
                {
                    conditions = conditions.Substring(0, 240);
                }
                string libelle = "Depart " + formattedDepart.Replace(":", "h") + " - " + NormalizeString(conditions);
                string typeCourse = CategorieCourseScraping(libelle, allocation);
                int? distance = int.TryParse(course["distance"]?.ToString(), out int dist) ? (int?)dist : null;
                short? partants = short.TryParse(course["nombreDeclaresPartants"]?.ToString(), out short pt) ? (short?)pt : null;
                return new Course
                {
                    NumGeny = numGeny,
                    NumCourse = (short)numCourse,
                    Discipline = discipline,
                    JCouples = jCouples,
                    JTrio = jTrio,
                    JMulti = jMulti,
                    JQuinte = jQuinte,
                    Autostart = autostart,
                    TypeCourse = typeCourse,
                    Cordage = cordage,
                    Allocation = allocation,
                    Distance = distance,
                    Partants = partants,
                    Libelle = libelle,
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
        /// <summary>
        /// Vérifie en base si le lieu (après normalisation) est présent dans la table dbo.Reunions.
        /// </summary>
        /// <param name="lieuCourse">Lieu normalisé.</param>
        /// <returns>True si le lieu existe, sinon false.</returns>
        private bool IsLieuCoursePresent(string lieuCourse)
        {
            string query = "SELECT COUNT(*) FROM dbo.Reunions WHERE UPPER(REPLACE(LieuCourse, '''', ' ')) = @LieuCourse";
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LieuCourse", lieuCourse);
                    int count = (int)command.ExecuteScalar();
                    return count > 0;
                }
            }
        }
        /// <summary>
        /// Détermine la catégorie d'une course en fonction d'un libellé et du montant de l'allocation.
        /// </summary>
        /// <param name="myLib">Libellé décrivant la course.</param>
        /// <param name="myAlloc">Montant de l'allocation.</param>
        /// <returns>Une chaîne représentant la catégorie de la course.</returns>
        public static string CategorieCourseScraping(string myLib, int myAlloc)
        {
            if (string.IsNullOrWhiteSpace(myLib))
            {
                return "G";
            }

            myLib = myLib.ToLowerInvariant();

            if (myLib.Contains("groupe 3") || myLib.Contains("groupe iii"))
                return "3";
            if (myLib.Contains("groupe 2") || myLib.Contains("groupe ii"))
                return "2";
            if (myLib.Contains("groupe 1") || myLib.Contains("groupe i"))
                return "1";
            if (myLib.Contains("course a ") || myLib.Contains("classe 1"))
                return "A";
            if (myLib.Contains("course b ") || myLib.Contains("classe 2"))
                return "B";
            if (myLib.Contains("listed"))
                return "L";
            if (myLib.Contains("course c ") || myLib.Contains("classe 3"))
                return "C";
            if (myLib.Contains("course d ") || myLib.Contains("classe 4"))
                return "D";
            if (myLib.Contains("course e "))
                return "E";
            if (myLib.Contains("course f "))
                return "F";
            if (myLib.Contains("course g ") || myLib.Contains("course h"))
                return "G";
            if (myLib.Contains("reclamer") || myLib.Contains("réclamer"))
                return "R";
            if (myLib.Contains("apprentis") || myLib.Contains("lads"))
                return "W";
            if (myLib.Contains("amateur") || myLib.Contains("gentlemen-riders") || myLib.Contains("cavalières"))
                return "X";

            // Si aucun indicateur textuel n'est retrouvé, déterminer la catégorie selon l'allocation.
            if (myAlloc > 0)
            {
                if (myAlloc >= 50000)
                    return "A";
                if (myAlloc >= 40000)
                    return "B";
                if (myAlloc >= 30000)
                    return "C";
                if (myAlloc >= 20000)
                    return "D";
                if (myAlloc >= 10000)
                    return "E";
                if (myAlloc >= 8000)
                    return "F";
                return "G";
            }
            else
            {
                return "G";
            }
        }
    }
}
