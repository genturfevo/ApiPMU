using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Data; // Pour les conversions si besoin.
using Microsoft.Data.SqlClient; // Veillez à installer le package NuGet Microsoft.Data.SqlClient.
using Newtonsoft.Json.Linq;
using ApiPMU.Models; // On utilise ici ProgrammeData, Reunion et Course.

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
            // Exemple : { "PARIS LONGCHAMP", "PARIS-LONGCHAMP" }
        };

        private readonly string _connectionString;

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
        /// Parse l'intégralité du JSON et retourne un objet ProgrammeData regroupant réunions et courses.
        /// </summary>
        /// <param name="json">Chaîne JSON à parser.</param>
        /// <param name="dateStr">Paramètre additionnel (ex. date d'extraction) – non utilisé ici mais conservé pour la signature.</param>
        /// <returns>Un objet ProgrammeData contenant les listes de réunions et de courses.</returns>
        public ProgrammeData ParseProgramme(string json, string dateStr)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            ProgrammeData programmeResult = new ProgrammeData();

            JObject data = JObject.Parse(json);
            JToken prog = data["programme"];
            if (prog == null)
            {
                Console.WriteLine("La clé 'programme' est absente du JSON.");
                return programmeResult;
            }

            JToken reunions = prog["reunions"];
            if (reunions == null)
            {
                Console.WriteLine("La clé 'reunions' est absente du JSON.");
                return programmeResult;
            }

            foreach (JToken reunionToken in reunions)
            {
                Reunion? reunionObj = ProcessReunion(reunionToken);
                if (reunionObj != null)
                {
                    programmeResult.Reunions.Add(reunionObj);

                    JToken courses = reunionToken["courses"];
                    if (courses != null)
                    {
                        foreach (JToken courseToken in courses)
                        {
                            Course? courseObj = ProcessCourse(courseToken, reunionObj.NumGeny, reunionObj.DateModif);
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
                int? numGeny = int.TryParse(reunion["NumGeny"]?.ToString(), out int ng) ? (int?)ng : null;
                int? numReunion = int.TryParse(reunion["NumReunion"]?.ToString(), out int nr) ? (int?)nr : null;
                DateTime dateReunion = DateTime.TryParse(reunion["DateReunion"]?.ToString(), out DateTime dr) ? dr : DateTime.MinValue;
                DateTime dateModif = DateTime.TryParse(reunion["DateModif"]?.ToString(), out DateTime dm) ? dm : DateTime.MinValue;

                JToken? hippodrome = reunion["hippodrome"];
                if (hippodrome == null)
                {
                    Console.WriteLine("La réunion ne contient pas de section 'hippodrome'.");
                    return null;
                }
                string libelleCourt = hippodrome["libelleCourt"]?.ToString() ?? string.Empty;
                string normalizedLibelleCourt = NormalizeString(libelleCourt);
                if (Correspondances.ContainsKey(normalizedLibelleCourt))
                {
                    normalizedLibelleCourt = Correspondances[normalizedLibelleCourt];
                }

                if (!IsLieuCoursePresent(normalizedLibelleCourt))
                {
                    Console.WriteLine($"La réunion avec libelléCourt '{libelleCourt}' (normalisé: '{normalizedLibelleCourt}') n'est pas présente en BDD.");
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
                Console.WriteLine("Erreur lors du traitement d'une réunion: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Transforme un token JSON en une instance de Course.
        /// </summary>
        /// <param name="course">Token JSON de la course.</param>
        /// <param name="numGeny">NumGeny hérité de la réunion (peut être null).</param>
        /// <param name="reunionDateModif">Date de modification de la réunion (utilisée par défaut si absente dans la course).</param>
        /// <returns>Instance de Course ou null en cas d'erreur.</returns>
        private Course? ProcessCourse(JToken course, int? numGeny, DateTime reunionDateModif)
        {
            try
            {
                int? numCourse = int.TryParse(course["NumCourse"]?.ToString(), out int nc) ? (int?)nc : null;
                string discipline = course["Discipline"]?.ToString() ?? string.Empty;
                string jCouples = course["Jcouples"]?.ToString() ?? string.Empty;
                string jTrio = course["Jtrio"]?.ToString() ?? string.Empty;
                string jMulti = course["Jmulti"]?.ToString() ?? string.Empty;
                string jQuinte = course["Jquinte"]?.ToString() ?? string.Empty;
                bool? autostart = bool.TryParse(course["Autostart"]?.ToString(), out bool a) ? (bool?)a : null;
                string typeCourse = course["TypeCourse"]?.ToString() ?? string.Empty;
                string cordage = course["Cordage"]?.ToString() ?? string.Empty;
                string allocation = course["Allocation"]?.ToString() ?? string.Empty;
                int? distance = int.TryParse(course["Distance"]?.ToString(), out int dist) ? (int?)dist : null;
                short? partants = short.TryParse(course["Partants"]?.ToString(), out short pt) ? (short?)pt : null;
                string libelle = course["Libelle"]?.ToString() ?? string.Empty;
                DateTime dateModif = DateTime.TryParse(course["DateModif"]?.ToString(), out DateTime cdm) ? cdm : reunionDateModif;

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
                Console.WriteLine("Erreur lors du traitement d'une course: " + ex.Message);
                return null;
            }
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
            string result = input.ToUpperInvariant().Replace("'", " ");
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
    }
}
