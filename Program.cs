using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using ApiPMU.Services;
using ApiPMU.Models;
using Microsoft.Extensions.Logging;

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
                    // var connectionString = "Data Source=PRESLESMU\\SQLEXPRESS;Initial Catalog=genturfevo;User ID=genturfevo;Password=Laurence#1968#;TrustServerCertificate=True";

                    // Enregistrement du DbContext configuré pour SQL Server
                    services.AddDbContext<ApiPMUDbContext>(options =>
                        options.UseSqlServer(connectionString));

                    // Enregistrement du service d'extraction des API PMU via HttpClientFactory
                    services.AddHttpClient<IApiPmuService, ApiPmuService>();
                    services.AddScoped<IDbService, DbService>();

                    // 📌 4️⃣ Injection manuelle de `Ordonanceur` avec la chaîne de connexion
                    services.AddSingleton<Ordonanceur>(sp =>
                    {
                        var apiPmuService = sp.GetRequiredService<IApiPmuService>();
                        var logger = sp.GetRequiredService<ILogger<Ordonanceur>>();
                        var serviceProvider = sp.GetRequiredService<IServiceProvider>();

                        return new Ordonanceur(apiPmuService, logger, serviceProvider, connectionString);
                    });

                    // 📌 5️⃣ Ajout de `Ordonanceur` comme `HostedService`
                    services.AddHostedService(sp => sp.GetRequiredService<Ordonanceur>());
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
