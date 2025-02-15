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
        public Participants ParseParticipants(string json, string numGeny, short numReunion, short numCourse)
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
                Cheval? chevalObj = ProcessCheval(chevalToken, numGeny, numReunion, numCourse);
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
        private Cheval? ProcessCheval(JToken participants, string numGeny, short numReunion, short numCourse)
        {
            try
            {
                JToken? hippodrome = participants["hippodrome"];
                string libelleCourt = hippodrome?["libelleCourt"]?.ToString() ?? string.Empty;
                return new Cheval {
                    NumGeny = numGeny,
                    NumCourse = numCourse,
                    DateModif = DateTime.Now
                };
            }
            catch { return null; }
        }
    }
}
