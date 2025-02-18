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
        /// Enregistre (ou met à jour) les reunions de la journée
        /// </summary>
        Task SaveOrUpdateReunionAsync(Reunion newReunion, bool updateColumns = true, bool deleteAndRecreate = false);

        /// <summary>
        /// Enregistre (ou met à jour) les courses d'une reunions
        /// </summary>
        Task SaveOrUpdateCourseAsync(Course newCourse, bool updateColumns = true, bool deleteAndRecreate = false);

        /// <summary>
        /// Calcule l’âge moyen des chevaux d’une course et met à jour la colonne Age dans la table Course.
        /// </summary>
        Task UpdateCourseAgeMoyenAsync(string numGeny, short numCourse);

        /// <summary>
        /// Enregistre (ou met à jour) les chevaux d'une course
        /// </summary>
        Task SaveOrUpdateChevauxAsync(IEnumerable<Cheval> chevauxList, bool updateColumns = true, bool deleteAndRecreate = false);

        /// <summary>
        /// Enregistre (ou met à jour) les chevaux d'une course
        /// </summary>
        Task SaveOrUpdatePerformanceAsync(IEnumerable<Performance> newPerformances, bool updateColumns = true, bool deleteAndRecreate = false);
    }
}
