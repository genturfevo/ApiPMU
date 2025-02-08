using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using ApiPMU.Models; // Utilisation des classes de domaine centralisées

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Parser du JSON pour transformer les données en instances du modèle de domaine.
    /// </summary>
    public class ProgrammeParser
    {
        private static readonly Dictionary<string, string> Correspondances = new Dictionary<string, string>
        {
            // Exemple : { "PARIS LONGCHAMP", "PARIS-LONGCHAMP" }
        };

        private readonly string _connectionString;

        public ProgrammeParser(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("La chaîne de connexion ne peut être nulle ou vide.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Parse l'ensemble du JSON et retourne un objet Programme regroupant réunions et courses.
        /// </summary>
        /// <param name="json">Le JSON à parser</param>
        /// <param name="dateStr">Paramètre additionnel (ex. date d'extraction)</param>
        /// <returns>Un objet Programme contenant les réunions et les courses valides</returns>
        public Programme ParseProgramme(string json, string dateStr)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            Programme programmeResult = new Programme();

            JObject data = JObject.Parse(json);
            JToken programme = data["programme"];
            if (programme == null)
            {
                Console.WriteLine("La clé 'programme' est absente du JSON.");
                return programmeResult;
            }

            JToken reunions = programme["reunions"];
            if (reunions == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return programmeResult;
            }

            foreach (JToken reunionToken in reunions)
            {
                var reunionObj = ProcessReunion(reunionToken);
                if (reunionObj != null)
                {
                    programmeResult.Reunions.Add(reunionObj);

                    JToken courses = reunionToken["courses"];
                    if (courses != null)
                    {
                        foreach (JToken courseToken in courses)
                        {
                            var courseObj = ProcessCourse(courseToken, reunionObj.NumGeny, reunionObj.DateModif);
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

        private Reunion? ProcessReunion(JToken reunion)
        {
            try
            {
                int numGeny = reunion.Value<int?>("NumGeny") ?? 0;
                int numReunion = reunion.Value<int?>("NumReunion") ?? 0;
                DateTime dateReunion = reunion.Value<DateTime?>("DateReunion") ?? DateTime.MinValue;
                DateTime dateModif = reunion.Value<DateTime?>("DateModif") ?? DateTime.MinValue;

                JToken? hippodrome = reunion["hippodrome"];
                if (hippodrome == null)
                {
                    Console.WriteLine($"La réunion NumReunion {numReunion} ne contient pas de section 'hippodrome'.");
                    return null;
                }
                string libelleCourt = hippodrome.Value<string?>("libelleCourt") ?? string.Empty;
                string normalizedLibelleCourt = NormalizeString(libelleCourt);
                if (Correspondances.ContainsKey(normalizedLibelleCourt))
                {
                    normalizedLibelleCourt = Correspondances[normalizedLibelleCourt];
                }

                if (!IsLieuCoursePresent(normalizedLibelleCourt))
                {
                    Console.WriteLine($"La réunion avec libelléCourt '{libelleCourt}' (normalisé : '{normalizedLibelleCourt}') n'est pas présente en BDD.");
                    return null;
                }

                return new Reunion
                {
                    NumGeny = numGeny,
                    NumReunion = numReunion,
                    LieuCourse = normalizedLibelleCourt,
                    DateReunion = dateReunion,
                    DateModif = dateModif
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du traitement d'une réunion : {ex.Message}");
                return null;
            }
        }

        private Course? ProcessCourse(JToken course, int numGeny, DateTime reunionDateModif)
        {
            try
            {
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
                DateTime dateModif = course.Value<DateTime?>("DateModif") ?? reunionDateModif;

                return new Course
                {
                    NumGeny = numGeny,
                    NumCourse = numCourse,
                    Discipline = discipline,
                    Jcouples = jCouples,
                    Jtrio = jTrio,
                    Jmulti = jMulti,
                    Jquinte = jQuinte,
                    Autostart = autostart,
                    TypeCourse = typeCourse,
                    Cordage = cordage,
                    Allocation = allocation,
                    Distance = distance,
                    Partants = partants,
                    Libelle = libelle,
                    DateModif = dateModif
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du traitement d'une course : {ex.Message}");
                return null;
            }
        }

        private string NormalizeString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            string result = input.ToUpperInvariant().Replace("'", " ");
            result = RemoveAccents(result);
            return result;
        }

        private string RemoveAccents(string text)
        {
            string normalizedString = text.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (char c in normalizedString)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

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
    }
}
