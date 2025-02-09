using System.Threading.Tasks;
using ApiPMU.Models;
using ApiPMU.Parsers;
using Microsoft.EntityFrameworkCore;

namespace ApiPMU.Services
{
    /// <summary>
    /// Service responsable de l'importation du programme et de l'enregistrement des données en base.
    /// </summary>
    public class ProgrammeService
    {
        private readonly ApiPMUDbContext _dbContext;
        private readonly IApiPmuService _apiPmuService;

        public ProgrammeService(ApiPMUDbContext dbContext, IApiPmuService apiPmuService)
        {
            _dbContext = dbContext;
            _apiPmuService = apiPmuService;
        }

        public async Task ImporterProgrammeAsync(string dateStr)
        {
            // 1. Extraction du JSON via le service d'API PMU.
            // On récupère le résultat sous forme de chaîne.
            var programmeJson = await _apiPmuService.ChargerProgrammeAsync<string>(dateStr);

            // 2. Transformation du JSON en entités avec le parser.
            // Récupération de la chaîne de connexion à partir du contexte EF.
            var connectionString = _dbContext.Database.GetDbConnection().ConnectionString;
            ProgrammeParser parser = new ProgrammeParser(connectionString);

            // Appel de la méthode renommée "ParseProgramme" qui retourne un objet contenant la réunion et les courses.
            var parsedProgramme = parser.ParseProgramme(programmeJson, dateStr);

            // 3. Enregistrement dans la base de données.
            // Ajout de la réunion et des courses à l'EF DbContext (les entités doivent être définies dans ApiPMU.Models).
            _dbContext.Reunions.Add(parsedProgramme.Reunion);
            _dbContext.Courses.AddRange(parsedProgramme.Courses);

            await _dbContext.SaveChangesAsync();
        }
    }
}
