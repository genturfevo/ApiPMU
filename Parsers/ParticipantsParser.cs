using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient; // Veillez à installer le package NuGet Microsoft.Data.SqlClient.
using Newtonsoft.Json.Linq;
using ApiPMU.Models;
using System.Diagnostics.Eventing.Reader;
using ApiPMU.Services;
using Microsoft.Extensions.Logging;

namespace ApiPMU.Parsers
{
    /// <summary>
    /// Parser qui transforme le JSON de l'API PMU en instances de modèles métier.
    /// </summary>
    public class ParticipantsParser
    {
        private readonly ILogger<ParticipantsParser> _logger;
        private readonly string _connectionString;

        public ParticipantsParser(ILogger<ParticipantsParser> logger, string connectionString)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = !string.IsNullOrWhiteSpace(connectionString)
                ? connectionString
                : throw new ArgumentException("La chaîne de connexion est obligatoire.", nameof(connectionString));
        }
        /// <summary>
        /// Parse l'intégralité du JSON et retourne un objet Programme regroupant réunions et courses.
        /// </summary>
        /// <param name="json">Chaîne JSON à parser.</param>
        /// <param name="dateStr">Paramètre additionnel (ex. date d'extraction) – non utilisé ici mais conservé pour la signature.</param>
        /// <returns>Un objet Programme contenant les listes de réunions et de courses.</returns>
        public ListeParticipants ParseParticipants(string json, string numGeny, short numReunion, short numCourse, string disc)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            ListeParticipants participantsResult = new ListeParticipants();
            JObject data = JObject.Parse(json);
            JToken? participants = data["participants"];
            if (participants == null)
            {
                _logger.LogError("La clé 'reunions' est absente du JSON.");
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
                string jokey = FormatNom(participants?["driver"]?.ToString() ?? string.Empty);
                string entraineur = FormatNom(participants?["entraineur"]?.ToString() ?? string.Empty);
                string zoneABC = numero < 7 ? "A" : numero < 13 ? "B" : "C";
                int nbCourses = participants?["nombreCourses"]?.Value<int>() ?? 0;
                int nbVictoires = participants?["nombreVictoires"]?.Value<int>() ?? 0;
                int nbPlaces = participants?["nombrePlaces"]?.Value<int>() ?? 0;
                //
                // Colonnes variables selon la discipline
                //
                Single distpoid = 0;
                string deferre = string.Empty;
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
                        _ => string.Empty
                    };
                }
                else
                {
                    distpoid = (Single)Math.Floor((participants?["poidsConditionMonte"]?.Value<Single>() ?? 0) / 10);
                    if (distpoid == 0) { distpoid = (Single)Math.Floor((participants?["handicapPoids"]?.Value<Single>() ?? 0) / 10); }
                    deferre = participants?["handicapValeur"]?.ToString() ?? string.Empty;
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
        // 
        // Fonction pour formater le nom
        // 
        string FormatNom(string nom)
        {
            if (string.IsNullOrEmpty(nom))
                return "NON PARTANT";

            // Vérifier si le nom contient au moins un "."
            if (nom.Contains("."))
            {
                return nom.Trim(); // Retourne le nom tel quel si déjà formaté correctement
            }
            else
            {
                // Formater le nom sous la forme "initiale.prenom. initiale2.nom"
                var nomParts = nom.Split(' '); // Sépare le prénom(s) et le nom

                if (nomParts.Length == 1)
                {
                    // Si le nom ne contient qu'un seul mot (nom), ajouter "NON PARTANT"
                    return $"{nom.Trim()} NON PARTANT";
                }
                else if (nomParts.Length == 2)
                {
                    // Si un prénom et un nom, formater en "initiale.prenom. nom"
                    return $"{nomParts[0][0]}. {nomParts[1]}".ToUpper(); // Initiale + nom
                }
                else if (nomParts.Length >= 3)
                {
                    // Si plusieurs prénoms et un nom composé, formater en "initiale1.initiale2. nom composé"
                    // Ici, nous séparons prénoms et nom de famille
                    var prenoms = nomParts.Take(nomParts.Length - 1).ToList();
                    var nomDeFamille = nomParts.Skip(nomParts.Length - 1).First();

                    // Ajouter initiales des prénoms
                    string initiales = string.Join(".", prenoms.Select(p => p[0].ToString()).ToArray()) + ".";

                    // Retourner le nom avec initiales et nom de famille
                    return $"{initiales} {nomDeFamille}".ToUpper();
                }
                return "NON PARTANT"; // Si aucun format valable trouvé
            }
        }
    }
}
