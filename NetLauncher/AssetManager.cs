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

        public void ExtractSoundsFromJar(string versionId)
        {
            string jarPath = Path.Combine(VersionDetail.MinecraftPath, "versions", versionId, $"{versionId}.jar");
            string resourcesPath = Path.Combine(VersionDetail.MinecraftPath, "resources");

            if (!File.Exists(jarPath)) return;

            using (var fs = File.OpenRead(jarPath))
            using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("sound/") &&
                        !entry.FullName.StartsWith("sounds/") &&
                        !entry.FullName.StartsWith("music/") &&
                        !entry.FullName.StartsWith("records/"))
                        continue;

                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    string destPath = Path.Combine(resourcesPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                    if (!File.Exists(destPath))
                    {
                        using (var entryStream = entry.Open())
                        using (var destStream = File.Create(destPath))
                        {
                            entryStream.CopyTo(destStream);
                        }
                    }
                }
            }
        }

        public void MapAssetsToResources(string assetIndexId)
        {
            string indexPath = Path.Combine(VersionDetail.MinecraftPath, "assets", "indexes", $"{assetIndexId}.json");
            string resourcesPath = Path.Combine(VersionDetail.MinecraftPath, "resources");

            if (!File.Exists(indexPath)) return;

            string json = File.ReadAllText(indexPath);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("objects", out JsonElement objects))
                    return;

                foreach (JsonProperty asset in objects.EnumerateObject())
                {
                    string virtualPath = asset.Name;
                    string hash = asset.Value.GetProperty("hash").GetString();
                    string prefix = hash.Substring(0, 2);

                    string objectPath = Path.Combine(VersionDetail.MinecraftPath, "assets", "objects", prefix, hash);
                    string resourceDest = Path.Combine(resourcesPath, virtualPath.Replace('/', Path.DirectorySeparatorChar));

                    if (!File.Exists(objectPath)) continue;
                    if (File.Exists(resourceDest)) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(resourceDest));
                    File.Copy(objectPath, resourceDest);
                }
            }
        }

        // Versiones viejas (< 1.7) esperan los assets en /resources en lugar de /assets/objects
        public async Task DownloadLegacyAssetsAsync(string assetIndexUrl, string assetIndexId, string assetIndexSha1, IProgress<int> progress = null)
        {
            string logPath = Path.Combine(VersionDetail.MinecraftPath, "launcher_debug.log");
            File.AppendAllText(logPath, $"\n[DEBUG] DownloadLegacyAssetsAsync iniciado para index: {assetIndexId}");

            string indexDir = Path.Combine(VersionDetail.MinecraftPath, "assets", "indexes");
            string indexPath = Path.Combine(indexDir, $"{assetIndexId}.json");

            File.AppendAllText(logPath, $"\n[DEBUG] Index path: {indexPath} | Existe: {File.Exists(indexPath)}");

            progress?.Report(0);
            await _downloader.DownloadFileAsync(assetIndexUrl, indexPath, assetIndexSha1);

            string resourcesPath = Path.Combine(VersionDetail.MinecraftPath, "resources");
            Directory.CreateDirectory(resourcesPath);

            string json = File.ReadAllText(indexPath);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                // El index legacy puede tener formato distinto — verificar si tiene "objects" o no
                JsonElement root = doc.RootElement;

                // Algunos index legacy tienen directamente los archivos con su ruta virtual como clave
                if (!root.TryGetProperty("objects", out JsonElement objects))
                    return;

                int total = 0;
                int current = 0;

                foreach (JsonProperty _ in objects.EnumerateObject()) total++;

                foreach (JsonProperty asset in objects.EnumerateObject())
                {
                    current++;
                    string virtualPath = asset.Name; // ej: "sounds/ambient/cave/cave1.ogg"
                    string hash = asset.Value.GetProperty("hash").GetString();
                    string prefix = hash.Substring(0, 2);

                    // Descargar a objects como siempre
                    string objectDest = Path.Combine(VersionDetail.MinecraftPath, "assets", "objects", prefix, hash);
                    string url = $"https://resources.download.minecraft.net/{prefix}/{hash}";

                    if (current % 10 == 0)
                        progress?.Report((int)((float)current / total * 100));

                    await _downloader.DownloadFileAsync(url, objectDest, hash);

                    // Además copiar a /resources con la ruta virtual
                    string resourceDest = Path.Combine(resourcesPath, virtualPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(resourceDest));

                    if (!File.Exists(resourceDest))
                        File.Copy(objectDest, resourceDest);
                }

                progress?.Report(100);
            }
        }

        public async Task DownloadAssetsAsync(string assetIndexUrl, string assetIndexId, string assetIndexSha1, IProgress<int> progress = null)
        {

            // 1. Descargar y guardar el asset index
            string indexDir = Path.Combine(VersionDetail.MinecraftPath, "assets", "indexes");
            string indexPath = Path.Combine(indexDir, $"{assetIndexId}.json");

            progress?.Report(0);
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

                    if (current % 10 == 0)
                        progress?.Report((int)((float)current / total * 100));

                    await _downloader.DownloadFileAsync(url, destPath, hash);
                }

                progress?.Report(100);
            }
        }
    }

}