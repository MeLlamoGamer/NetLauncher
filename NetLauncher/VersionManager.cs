using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class McVersion
    {
        public string Id { get; set; }       // "1.8.9", "1.20.1", etc.
        public string Type { get; set; }     // "release", "snapshot", "old_beta"
        public string Url { get; set; }      // URL al JSON de detalle de esa versión
    }

    public class VersionManager
    {
        private const string MANIFEST_URL = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        private static readonly HttpClient _http = new HttpClient();

        public List<McVersion> Versions { get; private set; } = new List<McVersion>();

        public async Task LoadVersionsAsync()
        {
            string json = await _http.GetStringAsync(MANIFEST_URL);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement versions = doc.RootElement.GetProperty("versions");

                Versions.Clear();

                foreach (JsonElement v in versions.EnumerateArray())
                {
                    string type = v.GetProperty("type").GetString();

                    if (type != "release") continue;

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