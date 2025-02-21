using ApiPMU.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiPMU.Services
{
    public class DbService : IDbService
    {
        private readonly ApiPMUDbContext _context;

        public DbService(ApiPMUDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Reunion>> GetReunionsByDateAsync(DateTime date)
        {
            return await _context.Reunions
                .Where(r => r.DateReunion == date)
                .ToListAsync();
        }

        public async Task<IEnumerable<Course>> GetCoursesByReunionAsync(string numGeny)
        {
            return await _context.Courses
                .Where(c => c.NumGeny == numGeny)
                .ToListAsync();
        }

        /// <summary>
        /// créer ou met à jour une reunion. 
        /// La clé est NumGeny.
        /// </summary>
        /// <param name="numGeny">La clé commune pour la réunion/course</param>
        public async Task SaveOrUpdateReunionAsync(Reunion newReunion, bool updateColumns = true, bool deleteAndRecreate = false)
        {
            if (newReunion == null)
                throw new ArgumentNullException(nameof(newReunion));

            // Recherche de la réunion existante basée sur la clé composite (NumGeny)
            var existingReunion = await _context.Reunions
                .FirstOrDefaultAsync(r => r.NumGeny == newReunion.NumGeny);

            if (existingReunion != null)
            {
                if (deleteAndRecreate)
                {
                    _context.Reunions.Remove(existingReunion);
                    await _context.SaveChangesAsync();
                    await _context.Reunions.AddAsync(newReunion);
                }
                else if (updateColumns)
                {
                    // Mettre à jour les colonnes que vous souhaitez modifier
                    existingReunion.NumReunion = newReunion.NumReunion;
                    existingReunion.LieuCourse = newReunion.LieuCourse;
                    existingReunion.DateReunion = newReunion.DateReunion;
                    existingReunion.DateModif = newReunion.DateModif;
                    // Ajoutez ici d'autres colonnes à mettre à jour...

                    _context.Reunions.Update(existingReunion);
                }
            }
            else
            {
                await _context.Reunions.AddAsync(newReunion);
            }

            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Crée ou met à jour une course. 
        /// La clé commune est NumGeny, NumCourse.
        /// </summary>
        /// <param name="numGeny">La clé commune pour la réunion/course</param>
        /// <param name="numCourse">Le numéro de la course</param>
        public async Task SaveOrUpdateCourseAsync(Course newCourse, bool updateColumns = true, bool deleteAndRecreate = false)
        {
            if (newCourse == null)
                throw new ArgumentNullException(nameof(newCourse));

            var existingCourse = await _context.Courses
                .FirstOrDefaultAsync(c => c.NumGeny == newCourse.NumGeny && c.NumCourse == newCourse.NumCourse);

            if (existingCourse != null)
            {
                if (deleteAndRecreate)
                {
                    _context.Courses.Remove(existingCourse);
                    await _context.SaveChangesAsync();
                    await _context.Courses.AddAsync(newCourse);
                }
                else if (updateColumns)
                {
                    // Mettre à jour les colonnes souhaitées
                    existingCourse.Discipline = newCourse.Discipline;
                    existingCourse.Difficulte = newCourse.Difficulte;
                    existingCourse.Libelle = newCourse.Libelle;
                    existingCourse.Distance = newCourse.Distance;
                    existingCourse.Partants = newCourse.Partants;
                    existingCourse.Age = newCourse.Age;
                    // etc.

                    _context.Courses.Update(existingCourse);
                }
            }
            else
            {
                await _context.Courses.AddAsync(newCourse);
            }

            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Met a jour l'age moyen des chevaux pour une course donnée.
        /// La clé commune est NumGeny, NumCourse.
        /// </summary>
        /// <param name="numGeny">La clé commune pour la réunion/course</param>
        /// <param name="numCourse">Le numéro de la course</param>
        public async Task UpdateCourseAgeMoyenAsync(string numGeny, short numCourse)
        {
            // Récupérer la liste des chevaux enregistrés pour la course (identifiée par la clé commune numGeny et le numéro de course)
            var chevaux = await _context.Chevaux
                .Where(c => c.NumGeny == numGeny && c.NumCourse == numCourse)
                .ToListAsync();

            if (chevaux == null || !chevaux.Any())
            {
                // Rien à faire si aucun cheval n'est trouvé
                return;
            }

            // Calcul de l'âge moyen
            short AgeMoyen = (short)chevaux.Average(c => int.Parse(c.SexAge.Substring(1)));

            // Mise à jour de la course correspondante
            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.NumGeny == numGeny && c.NumCourse == numCourse);

            if (course != null)
            {
                course.Age = AgeMoyen;
                await _context.SaveChangesAsync();
            }
        }
        /// <summary>
        /// Enregistre la liste des chevaux pour une course donnée.
        /// La clé commune est NumGeny et pour chaque cheval, la clé se compose de NumGeny, NumCourse et Numero.
        /// La liste des chevaux est obtenue via ParticipantsParser.ProcessCheval.
        /// </summary>
        /// <param name="numGeny">La clé commune pour la réunion/course</param>
        /// <param name="numCourse">Le numéro de la course</param>
        /// <param name="chevaux">La liste des chevaux à enregistrer</param>
        public async Task SaveOrUpdateChevauxAsync(IEnumerable<Cheval> chevauxList, bool updateColumns = true, bool deleteAndRecreate = false)
        {
            if (chevauxList == null)
                throw new ArgumentNullException(nameof(chevauxList));

            foreach (var newCheval in chevauxList)
            {
                // Recherche d'un cheval existant basé sur la clé composite : NumGeny, NumCourse et Numero
                var existingCheval = await _context.Chevaux
                    .FirstOrDefaultAsync(c => c.NumGeny == newCheval.NumGeny
                                           && c.NumCourse == newCheval.NumCourse
                                           && c.Numero == newCheval.Numero);

                if (existingCheval != null)
                {
                    if (deleteAndRecreate)
                    {
                        _context.Chevaux.Remove(existingCheval);
                        // Vous pouvez appeler SaveChanges ici si vous souhaitez que la suppression soit immédiate, 
                        // sinon, elle sera appliquée à la fin avec l'appel global.
                        await _context.Chevaux.AddAsync(newCheval);
                    }
                    else if (updateColumns)
                    {
                        // Mise à jour sélective des colonnes
                        existingCheval.Nom = newCheval.Nom;
                        existingCheval.SexAge = newCheval.SexAge;
                        // Mettez à jour d'autres colonnes si nécessaire
                        _context.Chevaux.Update(existingCheval);
                    }
                }
                else
                {
                    await _context.Chevaux.AddAsync(newCheval);
                }
            }

            await _context.SaveChangesAsync();
        }
        /// <summary>
        /// Enregistre la liste des chevaux pour une course donnée.
        /// La clé commune est NumGeny et pour chaque cheval, la clé se compose de NumGeny, NumCourse et Numero.
        /// La liste des chevaux est obtenue via ParticipantsParser.ProcessCheval.
        /// </summary>
        /// <param name="nom">La clé commune pour la réunion/course</param>
        /// <param name="datePerf">Le numéro de la course</param>
        /// <param name="discipline">La liste des chevaux à enregistrer</param>
        public async Task SaveOrUpdatePerformanceAsync(IEnumerable<Performance> newPerformances, bool updateColumns = true, bool deleteAndRecreate = false)
        {
            if (newPerformances == null)
                throw new ArgumentNullException(nameof(newPerformances));

            foreach (var newPerf in newPerformances)
            {
                // Recherche d'une cheval existant basé sur la clé composite : DatePerf, Discipline et Nom
                var existingPerf = await _context.Performances
                    .FirstOrDefaultAsync(c => c.Nom == newPerf.Nom
                                           && c.DatePerf == newPerf.DatePerf
                                           && c.Discipline == newPerf.Discipline);

                if (existingPerf != null)
                {
                    if (deleteAndRecreate)
                    {
                        _context.Performances.Remove(existingPerf);
                        // Vous pouvez appeler SaveChanges ici si vous souhaitez que la suppression soit immédiate, 
                        // sinon, elle sera appliquée à la fin avec l'appel global.
                        await _context.Performances.AddAsync(newPerf);
                    }
                    else if (updateColumns)
                    {
                        // Mise à jour sélective des colonnes
                        existingPerf.Nom = newPerf.Nom;
                        existingPerf.DatePerf = newPerf.DatePerf;
                        // Mettez à jour d'autres colonnes si nécessaire
                        _context.Performances.Update(existingPerf);
                    }
                }
                else
                {
                    await _context.Performances.AddAsync(newPerf);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
