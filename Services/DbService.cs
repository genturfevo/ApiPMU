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
            // On suppose que l'entité Reunion possède une propriété Date (de type string ou DateTime converti en string)
            return await _context.Reunions
                .Where(r => r.DateReunion == date)
                .ToListAsync();
        }

        public async Task<IEnumerable<Course>> GetCoursesByReunionAsync(string numGeny)
        {
            // On suppose que l'entité Course possède une propriété ReunionNumero
            return await _context.Courses
                .Where(c => c.NumGeny == numGeny)
                .ToListAsync();
        }

        /// <summary>
        /// Enregistre la liste des chevaux pour une course donnée.
        /// La clé commune est NumGeny et pour chaque cheval, la clé se compose de NumGeny, NumCourse et Numero.
        /// La liste des chevaux est obtenue via ParticipantsParser.ProcessCheval.
        /// </summary>
        /// <param name="numGeny">La clé commune pour la réunion/course</param>
        /// <param name="numCourse">Le numéro de la course</param>
        /// <param name="chevauxList">La liste des chevaux à enregistrer</param>
        public async Task SaveCourseChevauxAsync(string numGeny, short numCourse, ICollection<Cheval> chevaux)
        {
            if (chevaux == null)
                throw new ArgumentNullException(nameof(chevaux));

            // Optionnel : Supprimer les enregistrements existants pour cette course
            var existingChevaux = await _context.Chevaux
                .Where(c => c.NumGeny == numGeny && c.NumCourse == numCourse)
                .ToListAsync();

            if (existingChevaux.Any())
            {
                _context.Chevaux.RemoveRange(existingChevaux);
            }

            // Pour chaque cheval, affecter les propriétés communes (NumGeny et NumCourse)
            foreach (var cheval in chevaux)
            {
                cheval.NumGeny = numGeny;
                cheval.NumCourse = numCourse;

                // Si votre modèle Chevaux contient une propriété Date, décommentez la ligne suivante :
                // cheval.Date = date;
            }

            // Ajout en base des nouveaux enregistrements
            await _context.Chevaux.AddRangeAsync(chevaux);
            await _context.SaveChangesAsync();
        }
    }
}
