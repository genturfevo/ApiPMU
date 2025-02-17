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
        /// Enregistre (ou met à jour) les participants d'une course en passant par la table intermédiaire CourseChevaux.
        /// </summary>
        Task SaveCourseChevauxAsync(string numGeny, short numCourse, ICollection<Cheval> chevaux);

        /// <summary>
        /// Calcule l’âge moyen des chevaux d’une course et met à jour la colonne Age dans la table Course.
        /// </summary>
        Task UpdateCourseAgeMoyenAsync(string numGeny, short numCourse);
    }
}
