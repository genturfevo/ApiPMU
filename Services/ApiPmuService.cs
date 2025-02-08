using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace ApiPMU.Services
{
    public class ApiPmuService : IApiPmuService
    {
        // Utilisation d'un dictionnaire concurrent pour la mise en cache
        private static readonly ConcurrentDictionary<string, string> CacheRequetes = new ConcurrentDictionary<string, string>();

        private readonly HttpClient _httpClient;

        public ApiPmuService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Récupère un objet depuis le cache ou exécute la fonction asynchrone pour le charger.
        /// Vérifie que la désérialisation ne renvoie pas null.
        /// </summary>
        private async Task<T> ObtenirDepuisCacheOuAppelerAsync<T>(string cacheKey, Func<Task<T>> fonctionAsync)
        {
            if (CacheRequetes.TryGetValue(cacheKey, out string? jsonData) && jsonData != null)
            {
                T? result = JsonConvert.DeserializeObject<T>(jsonData);
                if (result is null)
                {
                    throw new InvalidOperationException($"La désérialisation a retourné null pour la clé de cache '{cacheKey}'.");
                }
                return result;
            }

            T newResult = await fonctionAsync();
            string jsonResult = JsonConvert.SerializeObject(newResult);
            CacheRequetes[cacheKey] = jsonResult;
            return newResult;
        }

        /// <summary>
        /// Effectue un appel HTTP et désérialise la réponse JSON en objet de type T.
        /// Vérifie que la désérialisation ne renvoie pas null.
        /// </summary>
        private async Task<T> GetJsonFromUrlAsync<T>(string url)
        {
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string jsonString = await response.Content.ReadAsStringAsync();
            T? result = JsonConvert.DeserializeObject<T>(jsonString);
            if (result is null)
            {
                throw new InvalidOperationException($"La désérialisation a retourné null pour l'URL '{url}'.");
            }
            return result;
        }

        public async Task<T> ChargerProgrammeAsync<T>(string dateStr)
        {
            string urlOnline = $"https://online.turfinfo.api.pmu.fr/rest/client/61/programme/{dateStr}";
            string urlOffline = $"https://offline.turfinfo.api.pmu.fr/rest/client/61/programme/{dateStr}";

            async Task<T> FonctionAsync()
            {
                try
                {
                    return await GetJsonFromUrlAsync<T>(urlOnline);
                }
                catch (Exception)
                {
                    return await GetJsonFromUrlAsync<T>(urlOffline);
                }
            }

            return await ObtenirDepuisCacheOuAppelerAsync<T>(dateStr, FonctionAsync);
        }

        public async Task<T> ChargerCourseAsync<T>(string dateStr, int reunion, int course, string detailType)
        {
            string urlOnline = $"https://online.turfinfo.api.pmu.fr/rest/client/61/programme/{dateStr}/R{reunion}/C{course}/{detailType}";
            string urlOffline = $"https://offline.turfinfo.api.pmu.fr/rest/client/61/programme/{dateStr}/R{reunion}/C{course}/{detailType}";
            string cacheKey = $"{dateStr}_R{reunion}_C{course}_{detailType}";

            async Task<T> FonctionAsync()
            {
                try
                {
                    return await GetJsonFromUrlAsync<T>(urlOnline);
                }
                catch (Exception)
                {
                    return await GetJsonFromUrlAsync<T>(urlOffline);
                }
            }

            return await ObtenirDepuisCacheOuAppelerAsync<T>(cacheKey, FonctionAsync);
        }
    }
}
