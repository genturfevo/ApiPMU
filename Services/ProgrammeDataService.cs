using System.Threading.Tasks;
using ApiPMU.Models;
using ApiPMU.Parsers;

namespace ApiPMU.Services
{
    public class ProgrammeDataService
    {
        private readonly ApiPMUDbContext _dbContext;
        private readonly IApiPmuService _apiPmuService;

        public ProgrammeDataService(ApiPMUDbContext dbContext, IApiPmuService apiPmuService)
        {
            _dbContext = dbContext;
            _apiPmuService = apiPmuService;
        }

        public async Task ImporterProgrammeAsync(string dateStr)
        {
            // 1. Extraction du JSON via le service d'API PMU
            var programmeJson = await _apiPmuService.ChargerProgrammeAsync<dynamic>(dateStr);

            // 2. Transformation du JSON en entités avec le parser
            ProgrammeParser parser = new ProgrammeParser();
            var parsedProgramme = parser.ParseFirstReunionAndCourses(programmeJson, dateStr);

            // 3. Enregistrement dans la base de données
            // Note : Ici, on ajoute la réunion et les courses séparément car il n'y a pas de liaison navigationnelle directe
            _dbContext.Reunions.Add(parsedProgramme.Reunion);
            _dbContext.Courses.AddRange(parsedProgramme.Courses);

            await _dbContext.SaveChangesAsync();
        }
    }
}
