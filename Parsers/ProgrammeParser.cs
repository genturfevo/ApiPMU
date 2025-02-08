using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient; // Assure-toi d'installer le package NuGet Microsoft.Data.SqlClient
using Newtonsoft.Json.Linq;

namespace ApiPMU
{
    /// <summary>
    /// Classe responsable du parsing du JSON et de l'injection des données dans la base.
    /// </summary>
    public class ProgrammeParser
    {
        /// <summary>
        /// Dictionnaire de correspondances manuelles entre le libelléCourt extrait du JSON
        /// et le LieuCourse attendu en base.
        /// Permet d'ajuster manuellement certains liens si besoin.
        /// </summary>
        private static readonly Dictionary<string, string> Correspondances = new Dictionary<string, string>
        {
            // Exemple d'ajustement : { "PARIS LONGCHAMP", "PARIS-LONGCHAMP" }
            // Ajoute ici d'autres correspondances au besoin.
        };

        private readonly string _connectionString;

        /// <summary>
        /// Constructeur qui prend en paramètre la chaîne de connexion à la BDD.
        /// Exemple d'appel :
        ///     var parser = new ProgrammeParser("Data Source=...;Initial Catalog=...;User Id=...;Password=...");
        /// </summary>
        /// <param name="connectionString">Chaîne de connexion SQL</param>
        public ProgrammeParser(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("La chaîne de connexion ne peut être nulle ou vide.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Méthode principale pour parser le JSON et traiter les réunions et courses.
        /// </summary>
        /// <param name="json">La chaîne JSON à parser</param>
        public void Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            // Charger le JSON dans un objet JObject
            JObject data = JObject.Parse(json);

            // Vérifier que la clé "programme" existe
            JToken programme = data["programme"];
            if (programme == null)
            {
                Console.WriteLine("La clé 'programme' est absente du JSON.");
                return;
            }

            // Vérifier que la clé "reunions" existe
            JToken reunions = programme["reunions"];
            if (reunions == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return;
            }

            // Boucler sur toutes les réunions
            foreach (JToken reunion in reunions)
            {
                ProcessReunion(reunion);
            }
        }

        /// <summary>
        /// Méthode wrapper pour compatibilité avec d'anciennes invocations.
        /// Appelle simplement la méthode Parse.
        /// </summary>
        /// <param name="json">La chaîne JSON à parser</param>
        public void ParseProgramme(string json)
        {
            // Cette méthode ne doit recevoir qu'un seul argument (la chaîne JSON)
            Parse(json);
        }

        /// <summary>
        /// Traite une réunion : vérifie la présence du lieu en BDD puis insère la réunion et ses courses.
        /// </summary>
        /// <param name="reunion">Token JSON correspondant à une réunion</param>
        private void ProcessReunion(JToken reunion)
        {
            try
            {
                // Extraction des informations de la réunion avec gestion des valeurs null
                int numGeny = reunion.Value<int?>("NumGeny") ?? 0;
                int numReunion = reunion.Value<int?>("NumReunion") ?? 0;
                DateTime dateReunion = reunion.Value<DateTime?>("DateReunion") ?? DateTime.MinValue;
                DateTime dateModif = reunion.Value<DateTime?>("DateModif") ?? DateTime.MinValue;

                // Extraction du libellé court depuis "hippodrome"
                JToken? hippodrome = reunion["hippodrome"];
                if (hippodrome == null)
                {
                    Console.WriteLine($"La réunion NumReunion {numReunion} ne contient pas de section 'hippodrome'.");
                    return;
                }
                string libelleCourt = hippodrome.Value<string?>("libelleCourt") ?? string.Empty;

                // Normaliser le libellé pour comparaison
                string normalizedLibelleCourt = NormalizeString(libelleCourt);

                // Vérification de correspondance manuelle si définie
                if (Correspondances.ContainsKey(normalizedLibelleCourt))
                {
                    normalizedLibelleCourt = Correspondances[normalizedLibelleCourt];
                }

                // Vérifier si le lieu est présent dans la BDD
                if (!IsLieuCoursePresent(normalizedLibelleCourt))
                {
                    Console.WriteLine($"La réunion avec libelléCourt '{libelleCourt}' (normalisé : '{normalizedLibelleCourt}') n'est pas présente dans la BDD.");
                    return;
                }

                // Insertion de la réunion dans la BDD
                InsertReunion(numGeny, numReunion, normalizedLibelleCourt, dateReunion, dateModif);

                // Traitement des courses associées à cette réunion
                JToken? courses = reunion["courses"];
                if (courses == null)
                {
                    Console.WriteLine($"Aucune course trouvée pour la réunion NumReunion {numReunion}.");
                    return;
                }

                foreach (JToken course in courses)
                {
                    ProcessCourse(course, numGeny, dateModif);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du traitement d'une réunion : {ex.Message}");
            }
        }

        /// <summary>
        /// Traite une course et insère ses données dans la BDD.
        /// </summary>
        /// <param name="course">Token JSON correspondant à une course</param>
        /// <param name="numGeny">NumGeny hérité de la réunion</param>
        /// <param name="reunionDateModif">DateModif de la réunion (à utiliser si la course n'en fournit pas)</param>
        private void ProcessCourse(JToken course, int numGeny, DateTime reunionDateModif)
        {
            try
            {
                // Extraction des données de la course avec gestion des valeurs null
                int numCourse = course.Value<int?>("NumCourse") ?? 0;
                string discipline = course.Value<string?>("Discipline") ?? string.Empty;
                string jCouples = course.Value<string?>("Jcouples") ?? string.Empty;
                string jTrio = course.Value<string?>("Jtrio") ?? string.Empty;
                string jMulti = course.Value<string?>("Jmulti") ?? string.Empty;
                string jQuinte = course.Value<string?>("Jquinte") ?? string.Empty;
                bool autostart = course.Value<bool?>("Autostart") ?? false;
                string typeCourse = course.Value<string?>("TypeCourse") ?? string.Empty;
                string cordage = course.Value<string?>("Cordage") ?? string.Empty;
                string allocation = course.Value<string?>("Allocation") ?? string.Empty;
                int distance = course.Value<int?>("Distance") ?? 0;
                int partants = course.Value<int?>("Partants") ?? 0;
                string libelle = course.Value<string?>("Libelle") ?? string.Empty;
                // Utiliser la DateModif de la course si présente, sinon celle de la réunion
                DateTime dateModif = course.Value<DateTime?>("DateModif") ?? reunionDateModif;

                // Insertion de la course dans la BDD
                InsertCourse(numGeny, numCourse, discipline, jCouples, jTrio, jMulti, jQuinte,
                             autostart, typeCourse, cordage, allocation, distance, partants, libelle, dateModif);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du traitement d'une course : {ex.Message}");
            }
        }

        /// <summary>
        /// Normalise une chaîne en la mettant en majuscules, en supprimant les accents et en remplaçant les apostrophes par des espaces.
        /// </summary>
        /// <param name="input">Chaîne à normaliser (peut être nulle)</param>
        /// <returns>Chaîne normalisée (non nulle)</returns>
        private string NormalizeString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Conversion en majuscules
            string result = input.ToUpperInvariant();

            // Remplacement des apostrophes par des espaces
            result = result.Replace("'", " ");

            // Suppression des accents
            result = RemoveAccents(result);

            return result;
        }

        /// <summary>
        /// Supprime les accents d'une chaîne en utilisant la normalisation Unicode.
        /// </summary>
        /// <param name="text">Chaîne à traiter</param>
        /// <returns>Chaîne sans accents</returns>
        private string RemoveAccents(string text)
        {
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            foreach (char c in normalizedString)
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Vérifie si le lieu (après normalisation) est présent dans la BDD.
        /// La comparaison se fait en majuscules, sans accents et en remplaçant les apostrophes.
        /// </summary>
        /// <param name="lieuCourse">Libellé du lieu à vérifier</param>
        /// <returns>Vrai si le lieu est présent, faux sinon</returns>
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
        /// Insère une réunion dans la table dbo.Reunions.
        /// </summary>
        private void InsertReunion(int numGeny, int numReunion, string lieuCourse, DateTime dateReunion, DateTime dateModif)
        {
            string query = "INSERT INTO dbo.Reunions (NumGeny, NumReunion, LieuCourse, DateReunion, DateModif) " +
                           "VALUES (@NumGeny, @NumReunion, @LieuCourse, @DateReunion, @DateModif)";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@NumGeny", numGeny);
                    command.Parameters.AddWithValue("@NumReunion", numReunion);
                    command.Parameters.AddWithValue("@LieuCourse", lieuCourse);
                    command.Parameters.AddWithValue("@DateReunion", dateReunion);
                    command.Parameters.AddWithValue("@DateModif", dateModif);
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Insère une course dans la table dbo.Courses.
        /// </summary>
        private void InsertCourse(int numGeny, int numCourse, string discipline, string jCouples, string jTrio, string jMulti, string jQuinte,
                                  bool autostart, string typeCourse, string cordage, string allocation, int distance, int partants, string libelle, DateTime dateModif)
        {
            string query = "INSERT INTO dbo.Courses (NumGeny, NumCourse, Discipline, Jcouples, Jtrio, Jmulti, Jquinte, Autostart, TypeCourse, Cordage, Allocation, Distance, Partants, Libelle, DateModif) " +
                           "VALUES (@NumGeny, @NumCourse, @Discipline, @Jcouples, @Jtrio, @Jmulti, @Jquinte, @Autostart, @TypeCourse, @Cordage, @Allocation, @Distance, @Partants, @Libelle, @DateModif)";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@NumGeny", numGeny);
                    command.Parameters.AddWithValue("@NumCourse", numCourse);
                    command.Parameters.AddWithValue("@Discipline", discipline);
                    command.Parameters.AddWithValue("@Jcouples", jCouples);
                    command.Parameters.AddWithValue("@Jtrio", jTrio);
                    command.Parameters.AddWithValue("@Jmulti", jMulti);
                    command.Parameters.AddWithValue("@Jquinte", jQuinte);
                    command.Parameters.AddWithValue("@Autostart", autostart);
                    command.Parameters.AddWithValue("@TypeCourse", typeCourse);
                    command.Parameters.AddWithValue("@Cordage", cordage);
                    command.Parameters.AddWithValue("@Allocation", allocation);
                    command.Parameters.AddWithValue("@Distance", distance);
                    command.Parameters.AddWithValue("@Partants", partants);
                    command.Parameters.AddWithValue("@Libelle", libelle);
                    command.Parameters.AddWithValue("@DateModif", dateModif);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
