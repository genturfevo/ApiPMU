using System;
using System.Globalization;
using System.Collections.Generic;
using ApiPMU.Models;

namespace ApiPMU.Parsers
{
    public class ProgrammeParser
    {
        /// <summary>
        /// Extrait la première réunion du JSON et construit le NumGeny ainsi que la liste des courses associées.
        /// Le NumGeny est formaté comme : "PMU" + dateProno (ddMMyyyy) + "R" + réunionJson.numReunion.
        /// </summary>
        /// <param name="programmeJson">Le JSON récupéré depuis l'API PMU</param>
        /// <param name="datePronoStr">La date du pronostic au format "ddMMyyyy" (par exemple "09022025")</param>
        /// <returns>Un objet ParsedProgramme contenant la réunion et la liste des courses associées</returns>
        public ParsedProgramme ParseFirstReunionAndCourses(dynamic programmeJson, string datePronoStr)
        {
            // Vérifier que le JSON contient au moins une réunion
            if (programmeJson.reunions == null || programmeJson.reunions.Count == 0)
                throw new Exception("Aucune réunion trouvée dans le JSON.");

            // Extraction de la première réunion
            var reunionJson = programmeJson.reunions[0];

            // Formatage du NumGeny : "PMU" + datePronoStr + "R" + numéro de réunion
            string numReunionStr = reunionJson.numReunion.ToString();
            string numGeny = $"PMU{datePronoStr}R{numReunionStr}";

            // Création de l'entité Reunion (sans collection Courses, car elle n'existe pas dans votre modèle)
            Reunion reunion = new Reunion
            {
                // Supposons que la propriété NumReunion existe et correspond au numéro de la réunion
                NumReunion = Convert.ToInt32(reunionJson.numReunion),
                DateReunion = DateTime.ParseExact((string)reunionJson.dateReunion, "ddMMyyyy", CultureInfo.InvariantCulture),
                LieuCourse = reunionJson.lieuCourse,
                NumGeny = numGeny
            };

            // Création de la liste des courses associées
            List<Course> courses = new List<Course>();
            foreach (var courseJson in reunionJson.courses)
            {
                Course course = new Course
                {
                    Libelle = courseJson.libelle,
                    // Assurez-vous d'ajouter les autres affectations nécessaires en fonction du JSON
                    NumGeny = numGeny
                };

                courses.Add(course);
            }

            return new ParsedProgramme
            {
                Reunion = reunion,
                Courses = courses
            };
        }
    }
}
