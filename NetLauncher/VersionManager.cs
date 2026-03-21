using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class McVersion
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
    }

    public class VersionManager
    {
        private const string MANIFEST_URL = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        private static readonly HttpClient _http = new HttpClient();

        private static string CachePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher", "version_manifest_cache.json"
        );

        public List<McVersion> Versions { get; private set; } = new List<McVersion>();

        public async Task LoadVersionsAsync(bool includeSnapshots = false)
        {
            string json = null;

            // 1. Intentar bajar el manifest de internet
            try
            {
                _http.Timeout = TimeSpan.FromSeconds(5); // no esperar demasiado
                json = await _http.GetStringAsync(MANIFEST_URL);

                // Guardarlo en cache para uso offline
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, json);

                ParseVersions(json, includeSnapshots);
            }
            catch
            {
                // Sin internet — intentar usar el cache
                if (File.Exists(CachePath))
                    json = File.ReadAllText(CachePath);
                else
                    throw new Exception("Sin conexión a internet y no hay versiones en caché.\nConectate al menos una vez para descargar la lista de versiones.");
            }

            ParseVersions(json, includeSnapshots);
        }

        private void ParseVersions(string json, bool includeSnapshots)
        {
            Versions.Clear();

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement versions = doc.RootElement.GetProperty("versions");

                foreach (JsonElement v in versions.EnumerateArray())
                {
                    string type = v.GetProperty("type").GetString();

                    // Filtrar según configuración
                    if (type == "release") { /* siempre incluir */ }
                    else if (type == "snapshot" && includeSnapshots) { /* incluir si está activado */ }
                    else continue;

                    Versions.Add(new McVersion
                    {
                        Id = v.GetProperty("id").GetString(),
                        Type = type,
                        Url = v.GetProperty("url").GetString()
                    });
                }
            }
        }
    }
}