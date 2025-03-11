using Microsoft.EntityFrameworkCore;
using ApiPMU.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

namespace ApiPMU.Services
{
    public class RubPTMN
    {
        private readonly ApiPMUDbContext _context;

        public RubPTMN(ApiPMUDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Extrait l'âge du cheval à partir du champ SexAge (ex: "H5", "M4", "F3").
        /// </summary>
        private int GetAge(string sexAge)
        {
            // On retire les lettres "H", "M" et "F" et on convertit le reste en entier.
            return int.Parse(sexAge.Replace("H", "").Replace("M", "").Replace("F", ""));
        }

        /// <summary>
        /// Classe les scores stockés dans tabCourse pour un indice donné et attribue un classement avec gestion des égalités.
        /// </summary>
        /// <param name="tabCourse">Tableau 2D contenant les scores et résultats.</param>
        /// <param name="nbPart">Nombre de participants (le tableau est indexé de 1 à nbPart).</param>
        /// <param name="scoreColumn">Colonne contenant le score à classer.</param>
        /// <param name="rankColumn">Colonne dans laquelle écrire le classement.</param>
        /// <param name="descending">Si true, le classement est décroissant (meilleur score = plus grand score).</param>
        private void RankScores(float[,] tabCourse, int nbPart, int scoreColumn, int rankColumn, bool descending)
        {
            // Création d'une liste d'éléments (index et score) pour les chevaux (indices 1 à nbPart)
            var scores = new List<(int index, float score)>();
            for (int i = 1; i <= nbPart; i++)
            {
                scores.Add((i, tabCourse[i, scoreColumn]));
            }
            // Tri de la liste selon le score selon l'ordre désiré
            scores = descending
                ? scores.OrderByDescending(s => s.score).ToList()
                : scores.OrderBy(s => s.score).ToList();

            // Attribution des rangs en gérant les égalités
            tabCourse[scores[0].index, rankColumn] = 1;
            for (int i = 1; i < scores.Count; i++)
            {
                // Si le score courant est égal au précédent, on garde le même rang
                tabCourse[scores[i].index, rankColumn] = scores[i].score == scores[i - 1].score
                    ? tabCourse[scores[i - 1].index, rankColumn]
                    : i + 1;
            }
        }

        public async Task<List<Reunion>> GetReunionsDuJourAsync(DateTime dateProno)
        {
            return await _context.Reunions
                .Where(r => r.DateReunion == dateProno)
                .OrderBy(r => r.NumReunion)
                .ToListAsync();
        }

        public async Task<List<Course>> GetCoursesParReunionAsync(string numGeny)
        {
            return await _context.Courses
                .Where(c => c.NumGeny == numGeny)
                .OrderBy(c => c.NumCourse)
                .ToListAsync();
        }

        public async Task<List<Cheval>> GetChevauxParCourseAsync(string numGeny, int numCourse)
        {
            return await _context.Chevaux
                .Where(ch => ch.NumGeny == numGeny && ch.NumCourse == numCourse)
                .OrderBy(ch => ch.Numero)
                .ToListAsync();
        }

        public async Task<List<Performance>> GetPerformancesChevalTrotAsync(string nomCheval, DateTime dateRef)
        {
            return await _context.Performances
                .Where(p => p.Nom == nomCheval && p.DatePerf > dateRef && (p.Discipline == "ATTELE" || p.Discipline == "MONTE"))
                .OrderByDescending(p => p.DatePerf)
                .Take(10)
                .ToListAsync();
        }

        public async Task<List<Performance>> GetPerformancesChevalGalopAsync(string nomCheval, DateTime dateRef)
        {
            return await _context.Performances
                .Where(p => p.Nom == nomCheval && p.DatePerf > dateRef && (p.Discipline != "ATTELE" && p.Discipline != "MONTE"))
                .OrderByDescending(p => p.DatePerf)
                .Take(10)
                .ToListAsync();
        }

        /// <summary>
        /// Processus principal de calculs des indices et mise à jour des données pour chaque cheval.
        /// </summary>
        public async Task ProcessCalculsAsync(DateTime dateProno)
        {
            // Récupération des réunions pour la date de pronostic
            var reunions = await GetReunionsDuJourAsync(dateProno);
            foreach (var reunion in reunions)
            {
                // Récupération des courses pour la réunion
                var courses = await GetCoursesParReunionAsync(reunion.NumGeny);
                foreach (var course in courses)
                {
                    // Initialisation des variables de la course
                    int cAllocation = course.Allocation ?? 0;
                    string disciplineCourse = course.Discipline.ToUpper()[0].ToString();
                    int nbPartants = course?.Partants ?? 0;

                    // Tableaux pour stocker les scores de la course et les historiques
                    // Les tableaux sont indexés de 1 à nbPartants (la case 0 n'est pas utilisée)
                    float[,] tabCourse = new float[nbPartants + 1, 18];
                    string[,] basesHisto = new string[nbPartants + 1, 39];
                    for (int i = 0; i <= nbPartants; i++)
                    {
                        for (int j = 0; j < 39; j++)
                        {
                            basesHisto[i, j] = "0";
                        }
                    }

                    // Récupération des chevaux participant à la course
                    var chevaux = await GetChevauxParCourseAsync(course.NumGeny, course.NumCourse);
                    foreach (var cheval in chevaux)
                    {
                        // Calcul de l'âge du cheval et ajustement pour définir l'historique pertinent
                        int chAge = GetAge(cheval.SexAge);
                        chAge = (chAge - 2) * -1;
                        var refDate = new DateTime(dateProno.Year, 1, 1).AddYears(chAge);

                        // Sélection des performances en fonction de la discipline (TROT ou GALOP)
                        List<Performance> performances;
                        if (disciplineCourse == "A" || disciplineCourse == "M")
                        {
                            performances = await GetPerformancesChevalTrotAsync(cheval.Nom, refDate);
                        }
                        else
                        {
                            performances = await GetPerformancesChevalGalopAsync(cheval.Nom, refDate);
                        }

                        // Initialisation des tableaux pour les calculs des indices
                        int[] placesCoef = new int[6];
                        int[] placesIndice = new int[6];
                        int perfPlace1 = 0, perfPlace2 = 0;
                        int x = 0, y = 0, j = 0;
                        basesHisto[cheval.Numero, 5] = "0";
                        basesHisto[cheval.Numero, 4] = "Inédit";
                        int cptPerf = 1;
                        int derPlace1 = 0;
                        int derPartants = 0;
                        int derAllocation = 0;
                        string derDiscipline = string.Empty;
                        bool flagIF = true;

                        if (performances.Any())
                        {
                            // Utilisation de la première performance pour récupérer certaines infos
                            derAllocation = performances.FirstOrDefault()?.Allocation ?? 0;
                            derPartants = performances.FirstOrDefault()?.Partants ?? 0;
                            derDiscipline = performances.FirstOrDefault()?.Discipline ?? string.Empty;
                            if (int.TryParse(performances.FirstOrDefault()?.Place, out int place))
                            {
                                derPlace1 = place;
                            }
                            // Initialisation de quelques valeurs de l'historique
                            basesHisto[cheval.Numero, 1] = cheval.Numero.ToString();
                            basesHisto[cheval.Numero, 2] = cheval.Nom;
                            basesHisto[cheval.Numero, 3] = GetAge(cheval.SexAge).ToString();
                            basesHisto[cheval.Numero, 4] = "";
                            basesHisto[cheval.Numero, 29] = "15";

                            // Parcours des performances pour remplir l'historique et calculer divers indices
                            foreach (var performance in performances)
                            {
                                string derCateg = "15";
                                string disciplinePerf = string.IsNullOrEmpty(performance.Discipline)
                                    ? disciplineCourse
                                    : performance.Discipline.ToUpper()[0].ToString();
                                string pDisc = string.IsNullOrEmpty(performance.Discipline)
                                    ? disciplineCourse.ToLower()[0].ToString()
                                    : performance.Discipline.ToLower()[0].ToString();

                                // Calcul du coefficient de réussite sur les 5 dernières courses de la spécialité
                                if (x < 6 && disciplineCourse == disciplinePerf)
                                {
                                    if (int.TryParse(performance.Place, out int xPlace))
                                    {
                                        placesCoef[x] = (xPlace > 0 && xPlace < 10) ? xPlace : 10;
                                    }
                                    else
                                    {
                                        placesCoef[x] = 10;
                                    }
                                    x++;
                                }
                                // Calcul de l'indice de forme pour le TROT
                                if ((disciplineCourse == "A" || disciplineCourse == "M") && (disciplinePerf == "A" || disciplinePerf == "M"))
                                {
                                    if (j == 1 && performance.DatePerf.AddMonths(2) < dateProno)
                                        flagIF = false;
                                    if (performance.DatePerf.AddMonths(3) < dateProno)
                                        flagIF = false;
                                    if (flagIF && y < 6)
                                    {
                                        if (int.TryParse(performance.Place, out int xPlace))
                                        {
                                            placesIndice[y] = (xPlace > 0 && xPlace < 10) ? xPlace : 10;
                                        }
                                        else
                                        {
                                            placesIndice[y] = 10;
                                        }
                                        y++;
                                    }
                                    j++;
                                }
                                // Calcul de l'indice de forme pour le GALOP
                                if ((disciplineCourse == "P" || disciplineCourse == "H" || disciplineCourse == "S" || disciplineCourse == "C")
                                    && disciplinePerf != "A" && disciplinePerf != "M")
                                {
                                    if (j == 1 && performance.DatePerf.AddMonths(2) < dateProno)
                                        flagIF = false;
                                    if (performance.DatePerf.AddMonths(5) < dateProno)
                                        flagIF = false;
                                    if (flagIF && y < 6)
                                    {
                                        if (int.TryParse(performance.Place, out int xPlace))
                                        {
                                            placesIndice[y] = (xPlace > 0 && xPlace < 10) ? xPlace : 10;
                                        }
                                        else
                                        {
                                            placesIndice[y] = 10;
                                        }
                                        y++;
                                    }
                                    j++;
                                }

                                // Mise à jour de l'historique (extraction de la place et catégorisation)
                                int derPlace = int.TryParse(performance.Place, out int tempPlace) ? tempPlace : 0;
                                derCateg = performance.TypeCourse switch
                                {
                                    "1" => "1",
                                    "2" => "2",
                                    "3" => "3",
                                    "L" => "4",
                                    "A" => "5",
                                    "B" => "6",
                                    "C" => "7",
                                    "D" => "8",
                                    "E" => "9",
                                    "F" => "10",
                                    "G" => "11",
                                    "W" => "13",
                                    "X" => "14",
                                    "R" => "15",
                                    _ => "15",
                                };

                                DateTime derDate = performance.DatePerf.AddYears(1);
                                if (perfPlace1 < 11 && derDate > dateProno)
                                {
                                    basesHisto[cheval.Numero, 4] += derPlace.ToString();
                                }
                                if (perfPlace1 < 11)
                                {
                                    basesHisto[cheval.Numero, 38] += derPlace.ToString();
                                }

                                // Mise à jour de la meilleure allocation
                                if (long.TryParse(basesHisto[cheval.Numero, 26], out long bestAlloc)
                                    && bestAlloc < (performance.Allocation ?? 0))
                                {
                                    basesHisto[cheval.Numero, 26] = performance.Allocation?.ToString() ?? "0";
                                    basesHisto[cheval.Numero, 27] = derPlace.ToString();
                                }
                                else if (bestAlloc == (performance.Allocation ?? 0))
                                {
                                    if (derPlace > 0 && int.TryParse(basesHisto[cheval.Numero, 27], out int bestPlace)
                                        && bestPlace > derPlace)
                                    {
                                        basesHisto[cheval.Numero, 27] = derPlace.ToString();
                                    }
                                }

                                // Mise à jour de la meilleure catégorie
                                int myCat = performance.TypeCourse != null
                                    ? performance.TypeCourse switch
                                    {
                                        "1" => 1,
                                        "2" => 2,
                                        "3" => 3,
                                        "L" => 4,
                                        "A" => 5,
                                        "B" => 6,
                                        "C" => 7,
                                        "D" => 8,
                                        "E" => 9,
                                        "F" => 10,
                                        "G" => 11,
                                        "W" => 13,
                                        "X" => 14,
                                        "R" => 15,
                                        _ => 15
                                    }
                                    : 15;

                                if (int.TryParse(basesHisto[cheval.Numero, 29], out int bestCategory)
                                    && bestCategory > myCat)
                                {
                                    basesHisto[cheval.Numero, 29] = myCat.ToString();
                                    basesHisto[cheval.Numero, 30] = derPlace.ToString();
                                    basesHisto[cheval.Numero, 22] = (performance.Allocation?.ToString() ?? "0");
                                }
                                else if (int.TryParse(basesHisto[cheval.Numero, 29], out int parsedValue)
                                         && parsedValue == myCat)
                                {
                                    if (derPlace > 0 && int.TryParse(basesHisto[cheval.Numero, 30], out int bestCategoryPlace)
                                        && bestCategoryPlace > derPlace)
                                    {
                                        basesHisto[cheval.Numero, 30] = derPlace.ToString();
                                        basesHisto[cheval.Numero, 22] = (performance.Allocation?.ToString() ?? "0");
                                    }
                                }

                                // Mise à jour des meilleures performances
                                if (derPlace > 0)
                                {
                                    if (cptPerf < 6 && (perfPlace1 < 1 || perfPlace2 < 1))
                                    {
                                        if (derPlace > 0 && derPlace < 8)
                                        {
                                            if (perfPlace1 > 0)
                                            {
                                                if (perfPlace2 > 0)
                                                {
                                                    if (derPlace < perfPlace1)
                                                    {
                                                        perfPlace2 = perfPlace1;
                                                        perfPlace1 = derPlace;
                                                    }
                                                    else if (derPlace < perfPlace2)
                                                    {
                                                        perfPlace2 = derPlace;
                                                    }
                                                }
                                                else
                                                {
                                                    perfPlace2 = derPlace;
                                                }
                                            }
                                            else
                                            {
                                                perfPlace1 = derPlace;
                                            }
                                        }
                                    }
                                }

                                // Mise à jour du nombre de performances
                                basesHisto[cheval.Numero, 5] = cptPerf.ToString();

                                // Mise à jour du classement des meilleures performances (colonne 37)
                                if (perfPlace1 > 0 && perfPlace2 > 0)
                                {
                                    basesHisto[cheval.Numero, 37] = (perfPlace2 > perfPlace1)
                                        ? $"{perfPlace1}{perfPlace2}"
                                        : $"{perfPlace2}{perfPlace1}";
                                }
                                else
                                {
                                    basesHisto[cheval.Numero, 37] = perfPlace1 > 0 ? $"{perfPlace1}9" : "99";
                                }

                                // --- Traduction de la partie "Meilleur place dans les 5 premiers par catégorie sur les 10 dernières courses" ---
                                //
                                // Les colonnes concernées sont :
                                //  - Colonne 33 : Montant allocation
                                //  - Colonne 34 : Place catégorie
                                //  - Colonne 35 : Catégorie
                                //
                                // Les conditions s'appliquent uniquement si DerPlace est > 0 et < 6.
                                if (derPlace > 0 && derPlace < 6)
                                {
                                    // On récupère la catégorie déjà stockée (colonne 35), en convertissant la valeur en entier (0 si vide)
                                    int currentCategory = 0;
                                    int.TryParse("0" + basesHisto[cheval.Numero, 35], out currentCategory);

                                    // On convertit la nouvelle catégorie (DerCateg) en entier
                                    int newCategory = 0;
                                    int.TryParse(derCateg, out newCategory);

                                    if (newCategory < currentCategory)
                                    {
                                        // Mise à jour : nouvelle allocation, nouvelle place et nouvelle catégorie
                                        long allocationValue = (long)(performance.Allocation ?? 0);
                                        basesHisto[cheval.Numero, 33] = allocationValue.ToString();
                                        basesHisto[cheval.Numero, 34] = derPlace.ToString();
                                        basesHisto[cheval.Numero, 35] = newCategory.ToString();
                                    }
                                    else if (newCategory == currentCategory)
                                    {
                                        // Si la catégorie est identique, on compare la place
                                        int currentPlace = 0;
                                        int.TryParse(basesHisto[cheval.Numero, 34], out currentPlace);
                                        if (derPlace < currentPlace)
                                        {
                                            long allocationValue = (long)(performance.Allocation ?? 0);
                                            basesHisto[cheval.Numero, 33] = allocationValue.ToString();
                                            basesHisto[cheval.Numero, 34] = derPlace.ToString();
                                            basesHisto[cheval.Numero, 35] = newCategory.ToString();
                                        }
                                        else if (derPlace == currentPlace)
                                        {
                                            // Si la place est également identique, on met à jour si l'allocation de la nouvelle performance est supérieure
                                            long currentAllocation = 0;
                                            long.TryParse("0" + basesHisto[cheval.Numero, 33], out currentAllocation);
                                            long newAllocation = (long)(performance.Allocation ?? 0);
                                            if (newAllocation > currentAllocation)
                                            {
                                                basesHisto[cheval.Numero, 33] = newAllocation.ToString();
                                                basesHisto[cheval.Numero, 34] = derPlace.ToString();
                                                basesHisto[cheval.Numero, 35] = newCategory.ToString();
                                            }
                                        }
                                    }
                                    else if (currentCategory == 0)
                                    {
                                        // Si aucune catégorie n'était définie, on enregistre la nouvelle allocation, la place et la catégorie
                                        long allocationValue = (long)(performance.Allocation ?? 0);
                                        basesHisto[cheval.Numero, 33] = allocationValue.ToString();
                                        basesHisto[cheval.Numero, 34] = derPlace.ToString();
                                        basesHisto[cheval.Numero, 35] = newCategory.ToString();
                                    }
                                }

                                cptPerf++;
                            }
                        }

                        // Calcul du coefficient de réussite selon le nombre de courses prises en compte
                        // Utilise les 5 dernières performances (pondération dégressive : 5,4,3,2,1)
                        // Si x est inférieur ou égal à 5, on divise par x, sinon on prend les 5 performances les plus récentes
                        switch (x)
                        {
                            case 0:
                                tabCourse[cheval.Numero, 2] = 1; // Aucun résultat disponible
                                break;
                            case 1:
                                tabCourse[cheval.Numero, 2] = 5 * (11 - placesCoef[0]);
                                break;
                            case 2:
                                tabCourse[cheval.Numero, 2] = (5 * (11 - placesCoef[0]) + 4 * (11 - placesCoef[1])) / 2.0f;
                                break;
                            case 3:
                                tabCourse[cheval.Numero, 2] = (5 * (11 - placesCoef[0]) + 4 * (11 - placesCoef[1]) + 3 * (11 - placesCoef[2])) / 3.0f;
                                break;
                            case 4:
                                tabCourse[cheval.Numero, 2] = (5 * (11 - placesCoef[0]) + 4 * (11 - placesCoef[1]) + 3 * (11 - placesCoef[2]) + 2 * (11 - placesCoef[3])) / 4.0f;
                                break;
                            default: // x >= 5
                                tabCourse[cheval.Numero, 2] = (5 * (11 - placesCoef[0]) + 4 * (11 - placesCoef[1]) + 3 * (11 - placesCoef[2]) + 2 * (11 - placesCoef[3]) + 1 * (11 - placesCoef[4])) / 5.0f;
                                break;
                        }

                        // Calcul de l'indice de forme
                        // Il s'agit simplement de la moyenne des places obtenues sur les courses éligibles
                        switch (y)
                        {
                            case 0:
                                tabCourse[cheval.Numero, 4] = 10; // Aucun résultat disponible
                                break;
                            case 1:
                                tabCourse[cheval.Numero, 4] = placesIndice[0];
                                break;
                            case 2:
                                tabCourse[cheval.Numero, 4] = (placesIndice[0] + placesIndice[1]) / 2.0f;
                                break;
                            case 3:
                                tabCourse[cheval.Numero, 4] = (placesIndice[0] + placesIndice[1] + placesIndice[2]) / 3.0f;
                                break;
                            case 4:
                                tabCourse[cheval.Numero, 4] = (placesIndice[0] + placesIndice[1] + placesIndice[2] + placesIndice[3]) / 4.0f;
                                break;
                            default: // y >= 5
                                tabCourse[cheval.Numero, 4] = (placesIndice[0] + placesIndice[1] + placesIndice[2] + placesIndice[3] + placesIndice[4]) / 5.0f;
                                break;
                        }

                        // Calcul des points MN (Musiques Nostradamus)
                        if (!string.IsNullOrEmpty(basesHisto[cheval.Numero, 38]))
                        {
                            int nbX = basesHisto[cheval.Numero, 38].Count(c => c == '1');
                            int nbY = basesHisto[cheval.Numero, 38].Count(c => c == '2');
                            int nbZ = basesHisto[cheval.Numero, 38].Count(c => c == '3');
                            tabCourse[cheval.Numero, 14] = nbX + nbY + nbZ;
                        }
                        else
                        {
                            tabCourse[cheval.Numero, 14] = 0;
                        }

                        if (cheval.NumGeny == "PMU28022025R1" && cheval.NumCourse == 1)
                        {
                            cheval.NumGeny = "PMU28022025R1";
                        }

                        // Calcul des points CX selon les règles définies
                        //'*** 
                        //'*** si musique(c) > 0 on cumul cette valeur
                        //'*** si 0 on ajoute 11
                        //'*** Valeur obtenue / Nb de course courues
                        //'*** Précision 2 chiffres derrière la virgule
                        //'*** 
                        float calCX = 0;
                        int nbCX = 0;
                        string musique = basesHisto[cheval.Numero, 4];
                        if (!string.IsNullOrEmpty(musique) && musique != "Inédit")
                        {
                            foreach (char c in musique)
                            {
                                int value;
                                if (char.IsDigit(c))
                                {
                                    value = (c == '0') ? 11 : (c - '0');
                                }
                                else
                                {
                                    value = 11;
                                }
                                calCX += value;
                                nbCX++;
                            }
                            tabCourse[cheval.Numero, 10] = (float)Math.Round(calCX / nbCX, 2);
                        }
                        else
                        {
                            tabCourse[cheval.Numero, 10] = 0;
                        }

                        // Calcul de l'indice IDC basé sur les gains et le nombre de courses
                        //'*** 
                        //'*** (Gains / 1000) / (Age / NbCourses * 10)
                        //'*** 
                        float valGains = (cheval.Gains ?? 0) / 1000.0f;
                        int ageCheval = GetAge(cheval.SexAge);
                        int valAge = 10 * ageCheval * (cheval.NbCourses ?? 0);
                        tabCourse[cheval.Numero, 6] = valAge > 0 ? (float)Math.Round(valGains / valAge, 2) : 0;

                        // Calcul des points OR(12) à partir de la dernière performance
                        //'*** 
                        //'*** Si DerPartants = 0 on force : DerPartants = 10
                        //'*** derAllocation / (1000.0f * derPlace1) * derPartants
                        //'*** 33.000 / (1000.0f * 1) * 13 = 429
                        //'*** 
                        // Calcul des points CFP(7) à partir des points OR(12)
                        //'*** 
                        //'*** PtsIDC(6) / PtsOR(12) * 100
                        if (derPartants == 0) {  derPartants = 10; }
                        int limite = (disciplineCourse == "A" || disciplineCourse == "M") ? 8 : 6;
                        if (derPlace1 > 0 && derPlace1 < limite)
                        {
                            if (derAllocation > 0)
                            {
                                tabCourse[cheval.Numero, 12] = (float)Math.Round(derAllocation / (1000.0f * derPlace1) * derPartants, 2);
                                tabCourse[cheval.Numero, 7] = tabCourse[cheval.Numero, 12] > 0
                                    ? (float)Math.Round(tabCourse[cheval.Numero, 6] / tabCourse[cheval.Numero, 12] * 100, 2)
                                    : 0;
                            }
                            else
                            {
                                tabCourse[cheval.Numero, 12] = 0;
                                tabCourse[cheval.Numero, 7] = 0;
                            }
                        }
                        else
                        {
                            if (derPlace1 > 0)
                            {
                                if (derAllocation > 0)
                                {
                                    tabCourse[cheval.Numero, 12] = (float)Math.Round((derAllocation / (1000.0f * derPlace1) * derPartants) / 2, 2);
                                    tabCourse[cheval.Numero, 7] = tabCourse[cheval.Numero, 12] > 0
                                        ? (float)Math.Round(tabCourse[cheval.Numero, 6] / tabCourse[cheval.Numero, 12] * 100, 2)
                                        : 0;
                                }
                                else
                                {
                                    tabCourse[cheval.Numero, 12] = 0;
                                    tabCourse[cheval.Numero, 7] = 0;
                                }
                            }
                            else
                            {
                                // Si aucune place valide n'est trouvée, on force derPlace1 à 10
                                derPlace1 = 10;
                                if (derAllocation > 0)
                                {
                                    tabCourse[cheval.Numero, 12] = (float)Math.Round((derAllocation / (1000.0f * derPlace1) * derPartants) / 2, 2);
                                    tabCourse[cheval.Numero, 7] = tabCourse[cheval.Numero, 12] > 0
                                        ? (float)Math.Round(tabCourse[cheval.Numero, 6] / tabCourse[cheval.Numero, 12] * 100, 2)
                                        : 0;
                                }
                                else
                                {
                                    tabCourse[cheval.Numero, 12] = 0;
                                    tabCourse[cheval.Numero, 7] = 0;
                                }
                            }
                        }

                        // Calcul du CJE (Critère de performance)
                        float valCJE = tabCourse[cheval.Numero, 14] > 0
                            ? (1 / tabCourse[cheval.Numero, 14]) * 100
                            : 0;
                        if (cheval.PtsRD.HasValue) valCJE += cheval.PtsRD.Value / 100.0f;
                        if (cheval.PtsRE.HasValue) valCJE += cheval.PtsRE.Value / 100.0f;
                        tabCourse[cheval.Numero, 17] = valCJE > 100 ? 1 : (float)Math.Round(valCJE, 1);

                        // Calcul de Rx : moyenne des gains par course
                        // Attention pour NbCourses la valeur n'est parfois pas fiable :
                        // ex. 28022025 R1C2 working class hero M6 9 courses et coquaholy M6 9 courses
                        // Lié au chevaux étranger, attention aux courses internationnales.
                        tabCourse[cheval.Numero, 16] = (cheval.Gains.HasValue && cheval.Gains.Value > 0 && cheval.NbCourses.HasValue && cheval.NbCourses.Value > 0)
                            ? (float)Math.Round((float)cheval.Gains.Value / cheval.NbCourses.Value, 2)
                            : 0;

                        // Traitement de l'historique HMP : calcul de sommes sur différentes tranches
                        if (basesHisto[cheval.Numero, 4] != "Inédit")
                        {
                            int valHi = basesHisto[cheval.Numero, 4].Length;
                            int somHi3 = 0, somHi345 = 0, somHi369 = 0, somHi3NP = 0;
                            int somHi5 = 0, somHi545 = 0, somHi569 = 0, somHi5NP = 0;
                            int somHiF = 0, somHiF45 = 0, somHiF69 = 0, somHiFNP = 0;
                            int somHiF3 = 0, somHiF345 = 0, somHiF369 = 0, somHiF3NP = 0;

                            for (int L = 0; L < valHi; L++)
                            {
                                char c = basesHisto[cheval.Numero, 4][L];
                                if (char.IsDigit(c))
                                {
                                    int place = c - '0';
                                    if (L < 3)
                                    {
                                        if (place > 0 && place < 4) { somHi3++; somHi5++; }
                                        else if (place > 3 && place < 6) { somHi345++; somHi545++; }
                                        else if (place > 5 && place < 10) { somHi369++; somHi569++; }
                                        else if (place == 0) { somHi3NP++; somHi5NP++; }
                                    }
                                    else if (L < 5)
                                    {
                                        if (place > 0 && place < 4) { somHi5++; }
                                        else if (place > 3 && place < 6) { somHi545++; }
                                        else if (place > 5 && place < 10) { somHi569++; }
                                        else if (place == 0) { somHi5NP++; }
                                    }
                                    else
                                    {
                                        if (place > 0 && place < 4) { somHiF++; if (L > 7) somHiF3++; }
                                        else if (place > 3 && place < 6) { somHiF45++; if (L > 7) somHiF345++; }
                                        else if (place > 5 && place < 10) { somHiF69++; if (L > 7) somHiF369++; }
                                        else if (place == 0) { somHiFNP++; if (L > 7) somHiF3NP++; }
                                    }
                                }
                            }

                            basesHisto[cheval.Numero, 6] = somHi3.ToString();
                            basesHisto[cheval.Numero, 7] = somHi345.ToString();
                            basesHisto[cheval.Numero, 8] = somHi369.ToString();
                            basesHisto[cheval.Numero, 9] = somHi3NP.ToString();
                            basesHisto[cheval.Numero, 10] = somHi5.ToString();
                            basesHisto[cheval.Numero, 11] = somHi545.ToString();
                            basesHisto[cheval.Numero, 12] = somHi569.ToString();
                            basesHisto[cheval.Numero, 13] = somHi5NP.ToString();
                            basesHisto[cheval.Numero, 14] = somHiF.ToString();
                            basesHisto[cheval.Numero, 15] = somHiF45.ToString();
                            basesHisto[cheval.Numero, 16] = somHiF69.ToString();
                            basesHisto[cheval.Numero, 17] = somHiFNP.ToString();
                            basesHisto[cheval.Numero, 18] = somHiF3.ToString();
                            basesHisto[cheval.Numero, 19] = somHiF345.ToString();
                            basesHisto[cheval.Numero, 20] = somHiF369.ToString();
                            basesHisto[cheval.Numero, 21] = somHiF3NP.ToString();
                        }
                        else
                        {
                            // Initialisation de l'historique à zéro si le cheval est "Inédit"
                            for (int M = 6; M <= 17; M++)
                            {
                                basesHisto[cheval.Numero, M] = "0";
                            }
                        }

                        // Calcul du pourcentage de valeur globale de l'historique
                        basesHisto[cheval.Numero, 23] = "1";
                        if (int.TryParse(basesHisto[cheval.Numero, 6], out int val66))
                            basesHisto[cheval.Numero, 23] = (val66 * 13.34).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 7], out int val7))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val7 * 6.67)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 8], out int val8))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val8 * 1.67)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 10], out int val10))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val10 * 3)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 11], out int val11))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val11 * 2)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 12], out int val12))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + val12).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 14], out int val14))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val14 * 3)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 15], out int val15))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val15 * 2)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 16], out int val16))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + val16).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 18], out int val18))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val18 * 10)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 19], out int val19))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val19 * 5)).ToString("0.00");
                        if (int.TryParse(basesHisto[cheval.Numero, 20], out int val20))
                            basesHisto[cheval.Numero, 23] = (float.Parse(basesHisto[cheval.Numero, 23]) + (val20 * 1.66)).ToString("0.00");

                    } // Fin boucle sur chaque cheval

                    // Classement des chevaux pour chaque indice grâce à la méthode RankScores :
                    // - Coefficient de réussite (colonne 2) -> classement en colonne 3 (ordre décroissant)
                    RankScores(tabCourse, nbPartants, 2, 3, descending: true);
                    // - Indice de forme (colonne 4) -> classement en colonne 5 (ordre croissant)
                    RankScores(tabCourse, nbPartants, 4, 5, descending: false);
                    // - Indice IDC (colonne 6) -> classement en colonne 8 (ordre décroissant)
                    RankScores(tabCourse, nbPartants, 6, 8, descending: true);
                    // - Indice CFP (colonne 7) -> classement en colonne 9 (ordre décroissant)
                    RankScores(tabCourse, nbPartants, 7, 9, descending: true);
                    // - Point CX (colonne 10) -> classement en colonne 11 (ordre croissant)
                    RankScores(tabCourse, nbPartants, 10, 11, descending: false);
                    // - Point OR (colonne 12) -> classement en colonne 13 (ordre décroissant)
                    RankScores(tabCourse, nbPartants, 12, 13, descending: true);
                    // - Point MN (colonne 12) -> classement en colonne 13 (ordre décroissant)
                    RankScores(tabCourse, nbPartants, 14, 15, descending: true);

                    //
                    // ****************************** //
                    // INTERPRÉTATION DE L'HISTORIQUE //
                    // ****************************** //
                    //
                    // Déclaration d'un tableau temporaire pour les opérations de tri (indices 1 à 38)
                    string[] varTampon = new string[39]; // On ignore l'indice 0
                    double val1, val2, val3, val4, val5, val6;

                    // Calcul de l'interprétation de l'historique et du MX (indice dans la colonne 36)
                    for (int i = 1; i <= nbPartants; i++)
                    {
                        if (basesHisto[i, 4] != "Inédit")
                        {
                            string muTmp = basesHisto[i, 4];
                            double mxTmp = muTmp.Length / 2.0;
                            int mxTmp1 = 0, mxTmp2 = 0, mxTmp3 = 0;
                            // Calcul sur les 4 premiers caractères ou 4 dernières courses 
                            for (int j = 1; j <= muTmp.Length; j++)
                            {
                                string c = muTmp.Substring(j - 1, 1);
                                if (c == "1" && j >= 1 && j <= 4)
                                    mxTmp1++;
                                if (c == "2" && j >= 1 && j <= 4)
                                    mxTmp2++;
                                if (c == "3" && j >= 1 && j <= 4)
                                    mxTmp3++;
                                if (j == 4)
                                    break;
                            }
                            // Si l'historique contient plus de 4 courses
                            //'***
                            //'*** Calcul sur les 3 premières courses de l'historique(10 courses maximum) et 5 au minimum
                            //'*** On part de la droite vers la gauche sur les 10 courses
                            //'***
                            if (mxTmp > 4)
                            {
                                for (int j = muTmp.Length; j >= muTmp.Length - 2; j--)
                                {
                                    string c = muTmp.Substring(j - 1, 1);
                                    if (c == "1" && (j == muTmp.Length || j == muTmp.Length - 1 || j == muTmp.Length - 2))
                                        mxTmp1++;
                                    if (c == "2" && (j == muTmp.Length || j == muTmp.Length - 1 || j == muTmp.Length - 2))
                                        mxTmp2++;
                                    if (c == "3" && (j == muTmp.Length || j == muTmp.Length - 1 || j == muTmp.Length - 2))
                                        mxTmp3++;
                                    if (mxTmp == 10 && j == 7)
                                        break;
                                    if (mxTmp == 9 && j == 6)
                                        break;
                                    if (mxTmp == 8 && j == 5)
                                        break;
                                    if (j == 4)
                                        break;
                                }
                            }
                            string mxTmpStr = mxTmp1.ToString() + mxTmp2.ToString() + mxTmp3.ToString();
                            double mxTmpSng = 0;
                            double.TryParse(mxTmpStr, out mxTmpSng);
                            mxTmp = mxTmp1 + mxTmp2 + mxTmp3;
                            if (int.Parse(mxTmpStr) != 0 && mxTmp != 0)
                            {
                                mxTmpSng = int.Parse(mxTmpStr) / (double)mxTmp;
                                if (Math.Round(mxTmpSng, 1) > 0)
                                    basesHisto[i, 36] = Math.Round(mxTmpSng, 1).ToString();
                                else
                                    basesHisto[i, 36] = "0";
                            }
                            else
                            {
                                basesHisto[i, 36] = "0";
                            }
                        }
                        else
                        {
                            basesHisto[i, 36] = "0";
                        }
                    }

                    // -----------------------------
                    // Classement HMP (colonnes 23 et 24)
                    // -----------------------------
                    // Tri en ordre décroissant sur le pourcentage HMP (colonne 23)
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 23], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 23], out val2);
                            if (val1 < val2)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }
                    basesHisto[1, 24] = "1";
                    for (int i = 2; i <= nbPartants; i++)
                    {
                        if (basesHisto[i, 23] == basesHisto[i - 1, 23])
                            basesHisto[i, 24] = basesHisto[i - 1, 24];
                        else
                            basesHisto[i, 24] = i.ToString();
                    }

                    // -----------------------------
                    // Classement sur la place de la meilleure allocation (colonne 27)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 27], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 27], out val2);
                            if (val1 == 0)
                                val1 = 10;
                            if (val2 == 0)
                                val2 = 10;
                            if (val1 > val2)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }

                    // -----------------------------
                    // Tri sur le montant de la meilleure allocation (colonne 26)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 26], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 26], out val2);
                            if (val1 < val2)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }

                    // -----------------------------
                    // Classement de la meilleure allocation (colonne 25)
                    // -----------------------------
                    basesHisto[1, 25] = "1";
                    for (int i = 2; i <= nbPartants; i++)
                    {
                        if (basesHisto[i, 26] == basesHisto[i - 1, 26] && basesHisto[i, 27] == basesHisto[i - 1, 27])
                            basesHisto[i, 25] = basesHisto[i - 1, 25];
                        else
                            basesHisto[i, 25] = i.ToString();
                    }

                    // -----------------------------
                    // Tri sur la meilleure catégorie (colonne 35)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 35], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 35], out val2);
                            if (val1 > val2)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }

                    // -----------------------------
                    // Tri sur la place de la meilleure catégorie (colonne 34) si catégorie identique (colonne 35)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 34], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 34], out val2);
                            double.TryParse("0" + basesHisto[j, 35], out val3);
                            double.TryParse("0" + basesHisto[j + 1, 35], out val4);
                            if (val1 > val2 && val3 == val4)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }

                    // -----------------------------
                    // Tri sur le montant de la meilleure allocation si catégorie et place identiques (colonnes 33, 34 et 35)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 33], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 33], out val2);
                            double.TryParse("0" + basesHisto[j, 34], out val3);
                            double.TryParse("0" + basesHisto[j + 1, 34], out val4);
                            double.TryParse("0" + basesHisto[j, 35], out val5);
                            double.TryParse("0" + basesHisto[j + 1, 35], out val6);
                            if (val1 < val2 && val3 == val4 && val5 == val6)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }

                    // -----------------------------
                    // Classement de la meilleure place de catégorie (colonne 28)
                    // -----------------------------
                    basesHisto[1, 28] = "1";
                    for (int i = 2; i <= nbPartants; i++)
                    {
                        if (basesHisto[i, 33] == basesHisto[i - 1, 33] &&
                            basesHisto[i, 34] == basesHisto[i - 1, 34] &&
                            basesHisto[i, 35] == basesHisto[i - 1, 35])
                        {
                            basesHisto[i, 28] = basesHisto[i - 1, 28];
                        }
                        else
                        {
                            basesHisto[i, 28] = i.ToString();
                        }
                    }

                    // -----------------------------
                    // Tri final sur le numéro PMU (colonne 1)
                    // -----------------------------
                    for (int i = nbPartants - 1; i >= 1; i--)
                    {
                        for (int j = 1; j <= i; j++)
                        {
                            double.TryParse("0" + basesHisto[j, 1], out val1);
                            double.TryParse("0" + basesHisto[j + 1, 1], out val2);
                            if (val1 == 0)
                                val1 = 21;
                            if (val2 == 0)
                                val2 = 21;
                            if (val1 > val2)
                            {
                                for (int k = 1; k <= 38; k++)
                                {
                                    varTampon[k] = basesHisto[j, k];
                                    basesHisto[j, k] = basesHisto[j + 1, k];
                                    basesHisto[j + 1, k] = varTampon[k];
                                }
                            }
                        }
                    }
                    // 
                    // Fin de l'interprétation de l'historique
                    // 
                    // Mise à jour des valeurs calculées dans la base de données pour chaque cheval
                    foreach (var cheval in chevaux)
                    {
                        var dbCheval = await _context.Chevaux.FirstOrDefaultAsync(ch =>
                            ch.NumGeny == course.NumGeny &&
                            ch.NumCourse == course.NumCourse &&
                            ch.Numero == cheval.Numero);

                        if (dbCheval != null)
                        {
                            dbCheval.CoefReussite = tabCourse[cheval.Numero, 2];
                            dbCheval.ClasCoefReussite = tabCourse[cheval.Numero, 3];
                            dbCheval.IndFor = tabCourse[cheval.Numero, 4];
                            dbCheval.IndForme = (int)tabCourse[cheval.Numero, 5];
                            dbCheval.PtsIDC = tabCourse[cheval.Numero, 6];
                            dbCheval.PtsCFP = tabCourse[cheval.Numero, 7];
                            dbCheval.ClaIDC = (int)tabCourse[cheval.Numero, 8];
                            dbCheval.ClaCFP = (int)tabCourse[cheval.Numero, 9];
                            dbCheval.PtsCX = tabCourse[cheval.Numero, 10];
                            dbCheval.ClaCX = (int)tabCourse[cheval.Numero, 11];
                            dbCheval.PtsOR = tabCourse[cheval.Numero, 12];
                            dbCheval.ClaOR = (int)tabCourse[cheval.Numero, 13];
                            dbCheval.PtsMN = (int)tabCourse[cheval.Numero, 14];
                            dbCheval.ClasMN = (int)tabCourse[cheval.Numero, 15];
                            // '*** Points RX
                            dbCheval.Pourcent11h = tabCourse[cheval.Numero, 16];
                            // '*** VaCouples : Points CJE
                            dbCheval.VaCouples = tabCourse[cheval.Numero, 17];
                            // '*** PrcDr : HMP Historique Michel Poulet 
                            dbCheval.PrcDr = int.TryParse(basesHisto[cheval.Numero, 23].Split(',')[0], out int prcDr) ? prcDr : (int?)null; 
                            // '*** NbCouples : Classement Meilleur Allocation 
                            dbCheval.NbCouples = int.TryParse(basesHisto[cheval.Numero, 25].Split(',')[0], out int nbCouples) ? nbCouples : (int?)null;
                            // '*** PrcEn : Meilleur place de categorie 
                            dbCheval.PrcEn = int.TryParse(basesHisto[cheval.Numero, 28].Split(',')[0], out int prcEn) ? prcEn : (int?)null; 
                            // '*** PrcCh : MX 
                            dbCheval.PrcCh = float.TryParse(basesHisto[cheval.Numero, 36].Replace(".", ","), out float prcCh) ? prcCh : (float?)null;
                            // '*** NbBloc : Mu Musique sur 2 digits (/ 5 dernières courses)
                            // '*** NbBloc : Ecrasé dans GenTurfEvoConsole module RubMNMP
                            dbCheval.NbBloc = int.TryParse(basesHisto[cheval.Numero, 37].Split(',')[0], out int nbBloc) ? nbBloc : (int?)null;
                            // '*** Stats SRC calculées dans GenTurfEvoConsole module StatsCombi
                            dbCheval.PourcentDef = 0; // Stats SRC
                        }
                    }

                    //'**************************************
                    //'*** Calul Difficulté et Jouabilité ***
                    //'**************************************
                    //'*** 
                    //'*** Nombre de MN < 3 / NbPart * 100
                    //'***
                    float calSMN = 0;
                    float mySMN = 0;
                    for (int i = 1; i <= nbPartants; i++)
                    {
                        if (tabCourse[i, 14] < 3)
                        {
                            mySMN += 1;
                        }
                    }
                    calSMN = mySMN / nbPartants * 100;

                    // Mise à jour de la difficulté dans la base de données
                    var courseToUpdate = await _context.Courses.FirstOrDefaultAsync(c => c.NumGeny == course.NumGeny && c.NumCourse == course.NumCourse);
                    if (courseToUpdate != null)
                    {
                        courseToUpdate.Difficulte = (int)Math.Round(calSMN);
                        _context.Courses.Update(courseToUpdate);
                    }

                    await _context.SaveChangesAsync();
                } // Fin boucle sur les courses
            } // Fin boucle sur les réunions
        }
    }
}
