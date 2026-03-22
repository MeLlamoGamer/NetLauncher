using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class FabricVersion
    {
        public string McVersion { get; set; }
        public string LoaderVersion { get; set; }
        public string DisplayName => $"FB {McVersion}";
    }

    public class FabricManager
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string GAME_VERSIONS_URL = "https://meta.fabricmc.net/v2/versions/game";
        private const string LOADER_VERSION_URL = "https://meta.fabricmc.net/v2/versions/loader";

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher", "fabric_versions_cache.json"
        );

        public List<FabricVersion> Versions { get; private set; } = new List<FabricVersion>();

        public async Task LoadVersionsAsync(bool includeSnapshots = false)
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".NetLauncher", "launcher_debug.log"
            );

            string loaderVersion = await GetLatestLoaderVersionAsync();
            File.AppendAllText(logPath, $"\n[DEBUG] Fabric loader version: {loaderVersion}");

            string gameJson = null;
            try
            {
                //_http.Timeout = TimeSpan.FromSeconds(10); // aumentar timeout
                gameJson = await _http.GetStringAsync(GAME_VERSIONS_URL);
                File.AppendAllText(logPath, $"\n[DEBUG] Fabric game JSON descargado OK, longitud: {gameJson.Length}");
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, gameJson);
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, $"\n[DEBUG] Error descargando Fabric versions: {ex.Message}");
                if (File.Exists(CachePath))
                {
                    gameJson = File.ReadAllText(CachePath);
                    File.AppendAllText(logPath, $"\n[DEBUG] Usando cache de Fabric");
                }
                else
                {
                    File.AppendAllText(logPath, $"\n[DEBUG] Sin cache de Fabric disponible");
                    return;
                }
            }

            Versions.Clear();

            using (JsonDocument doc = JsonDocument.Parse(gameJson))
            {
                foreach (JsonElement v in doc.RootElement.EnumerateArray())
                {
                    bool isStable = v.GetProperty("stable").GetBoolean();
                    if (!isStable && !includeSnapshots) continue;
                    Versions.Add(new FabricVersion
                    {
                        McVersion = v.GetProperty("version").GetString(),
                        LoaderVersion = loaderVersion
                    });
                }
            }

            File.AppendAllText(logPath, $"\n[DEBUG] Fabric versions cargadas: {Versions.Count}");
        }

        private static string LoaderCachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher", "fabric_loader_cache.json"
        );

        private async Task<string> GetLatestLoaderVersionAsync()
        {
            try
            {
                string json = await _http.GetStringAsync(LOADER_VERSION_URL);

                // Cachear para uso offline
                Directory.CreateDirectory(Path.GetDirectoryName(LoaderCachePath));
                File.WriteAllText(LoaderCachePath, json);

                using (JsonDocument doc = JsonDocument.Parse(json))
                    return doc.RootElement[0].GetProperty("version").GetString();
            }
            catch
            {
                // Intentar usar cache
                if (File.Exists(LoaderCachePath))
                {
                    try
                    {
                        string json = File.ReadAllText(LoaderCachePath);
                        using (JsonDocument doc = JsonDocument.Parse(json))
                            return doc.RootElement[0].GetProperty("version").GetString();
                    }
                    catch { }
                }

                return "0.16.10"; // fallback
            }
        }

        public async Task<string> GetLaunchProfileUrlAsync(string mcVersion, string loaderVersion)
        {
            return $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
        }
    }
}