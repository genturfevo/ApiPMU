using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ApiPMU.Services;
using ApiPMU.Models;

namespace ApiPMU
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Création de l'hôte générique pour gérer DI, configuration, logging, etc.
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Chaîne de connexion intégrée directement dans le code
                    var connectionString = "Data Source=.\\SQLEXPRESS;Initial Catalog=genturfevo;User ID=genturfevo;Password=Laurence#1968#;TrustServerCertificate=True";

                    // Enregistrement du DbContext configuré pour SQL Server
                    services.AddDbContext<ApiPMUDbContext>(options =>
                        options.UseSqlServer(connectionString));

                    // Enregistrement du service d'extraction des API PMU via HttpClientFactory
                    services.AddHttpClient<IApiPmuService, ApiPmuService>();

                    // Enregistrement du service hébergé qui s'exécutera quotidiennement
                    services.AddHostedService<Ordonanceur>();
                })
                .Build();

            // Création d'une portée pour appliquer les migrations et d'autres initialisations
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var dbContext = services.GetRequiredService<ApiPMUDbContext>();

                // Application des migrations pour mettre à jour la base de données
                await dbContext.Database.MigrateAsync();

                Console.WriteLine("Initialisation de la base et des services terminée.");
            }

            // Exécution de l'hôte (pour lancer le BackgroundService et garder la console active)
            await host.RunAsync();
        }
    }
}
