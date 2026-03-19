using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class AssetManager
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly Downloader _downloader = new Downloader();

        private const string RESOURCES_URL = "https://resources.download.minecraft.net/";

        public async Task DownloadAssetsAsync(string assetIndexUrl, string assetIndexId, string assetIndexSha1, IProgress<string> progress = null)
        {
            // 1. Descargar y guardar el asset index
            string indexDir = Path.Combine(VersionDetail.MinecraftPath, "assets", "indexes");
            string indexPath = Path.Combine(indexDir, $"{assetIndexId}.json");

            progress?.Report("Descargando asset index...");
            await _downloader.DownloadFileAsync(assetIndexUrl, indexPath, assetIndexSha1);

            // 2. Parsear el index y descargar cada asset
            string json = File.ReadAllText(indexPath);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement objects = doc.RootElement.GetProperty("objects");

                int total = 0;
                int current = 0;

                // Contar total para el progreso
                // ✅ Así va bien — primero contamos
                foreach (JsonProperty _ in objects.EnumerateObject())
                    total++;

                foreach (JsonProperty asset in objects.EnumerateObject())
                {
                    current++;
                    string hash = asset.Value.GetProperty("hash").GetString();
                    string prefix = hash.Substring(0, 2);

                    string destPath = Path.Combine(
                        VersionDetail.MinecraftPath, "assets", "objects", prefix, hash
                    );

                    string url = $"{RESOURCES_URL}{prefix}/{hash}";

                    if (current % 50 == 0)
                        progress?.Report($"Assets: {current}/{total}");

                    await _downloader.DownloadFileAsync(url, destPath, hash);
                }

                progress?.Report($"Assets completos: {total}/{total}");
            }
        }
    }
}