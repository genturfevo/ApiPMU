namespace ApiPMU.Services
{
    /// <summary>
    /// Interface pour le service d'extraction des données des API PMU.
    /// </summary>
    public interface IApiPmuService
    {
        /// <summary>
        /// Charge le programme de la journée pour une date donnée (format "jjmmaaaa").
        /// </summary>
        Task<T> ChargerProgrammeAsync<T>(string dateStr);

        /// <summary>
        /// Charge les données d'une course spécifique (participants ou performances détaillées).
        /// </summary>
        Task<T> ChargerCourseAsync<T>(string dateStr, int reunion, int course, string detailType);
    }
}
