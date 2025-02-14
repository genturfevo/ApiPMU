using System.Collections.Generic;
using System.Threading.Tasks;
using ApiPMU.Models;

namespace ApiPMU.Services
{
    public interface IDbService
    {
        /// <summary>
        /// Récupère les réunions enregistrées pour une date donnée.
        /// </summary>
        Task<IEnumerable<Reunion>> GetReunionsByDateAsync(DateTime date);

        /// <summary>
        /// Récupère les courses associées à une réunion identifiée par son numéro.
        /// On suppose ici que l'entité Course possède une propriété ReunionNumero.
        /// </summary>
        Task<IEnumerable<Course>> GetCoursesByReunionAsync(string NumGeny);

        /// <summary>
        /// Enregistre (ou met à jour) le détail d'une course en stockant le JSON dans la propriété DetailJson.
        /// </summary>
        Task SaveCourseChevauxAsync(string numGeny, short numCourse, short numero);
    }
}
