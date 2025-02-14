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

        public async Task SaveCourseChevauxAsync(string numGeny, short numCourse, short numero)
        {
            // Recherche de la course à mettre à jour
            var chevaux = await _context.Chevaux
                .FirstOrDefaultAsync(c => c.NumGeny == numGeny && c.NumCourse == numCourse && c.Numero == numero);

            if (chevaux != null)
            {
                chevaux.NumCourse = numCourse;  // On suppose que la propriété NumCourse existe dans Chevaux
            }
            else
            {
                // Si la course n'existe pas (cas rare selon votre logique d'extraction), on peut la créer
                chevaux = new Cheval
                {
                    NumGeny = numGeny,
                    NumCourse = numCourse,
                    Numero = numero,
                };
                _context.Chevaux.Add(chevaux);
            }
            await _context.SaveChangesAsync();
        }
    }
}
