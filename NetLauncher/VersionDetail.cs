using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class LibraryInfo
    {
        public string Path { get; set; }
        public string Url { get; set; }
        public string Sha1 { get; set; }
    }

    public class VersionDetail
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly Downloader _downloader = new Downloader();
        private string _versionUrl;

        public string Id { get; private set; }
        public string ClientJarPath { get; private set; }
        public string AssetIndex { get; private set; }
        public string AssetIndexUrl { get; private set; }
        public string AssetIndexSha1 { get; private set; }
        public string MainClass { get; private set; }
        public string MinecraftArguments { get; private set; } // formato viejo 1.12-
        public List<string> GameArguments { get; private set; } = new List<string>(); // formato nuevo 1.13+
        public List<string> JvmArguments { get; private set; } = new List<string>(); // formato nuevo 1.13+
        public bool IsNewFormat { get; private set; } = false;
        public List<LibraryInfo> Libraries { get; private set; } = new List<LibraryInfo>();

        public static string MinecraftPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher"
        );

        public async Task LoadAsync(string versionUrl, string versionId)
        {
            Id = versionId;
            _versionUrl = versionUrl; // guardar para ExtractNativesAsync

            string json = await _http.GetStringAsync(versionUrl);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;

                MainClass = root.GetProperty("mainClass").GetString();

                // Detectar formato de argumentos
                if (root.TryGetProperty("minecraftArguments", out JsonElement oldArgs))
                {
                    // Formato viejo — 1.12 y menor
                    IsNewFormat = false;
                    MinecraftArguments = oldArgs.GetString();
                }
                else if (root.TryGetProperty("arguments", out JsonElement newArgs))
                {
                    // Formato nuevo — 1.13+
                    IsNewFormat = true;

                    // Argumentos del juego
                    if (newArgs.TryGetProperty("game", out JsonElement gameArgsEl))
                    {
                        foreach (JsonElement arg in gameArgsEl.EnumerateArray())
                        {
                            // Algunos elementos son strings simples, otros son objetos con reglas
                            if (arg.ValueKind == JsonValueKind.String)
                                GameArguments.Add(arg.GetString());
                            // Los objetos con reglas los ignoramos por ahora (son features opcionales)
                        }
                    }

                    // Argumentos JVM
                    if (newArgs.TryGetProperty("jvm", out JsonElement jvmArgsEl))
                    {
                        foreach (JsonElement arg in jvmArgsEl.EnumerateArray())
                        {
                            if (arg.ValueKind == JsonValueKind.String)
                                JvmArguments.Add(arg.GetString());
                        }
                    }
                }

                JsonElement assetIndexEl = root.GetProperty("assetIndex");
                AssetIndex = assetIndexEl.GetProperty("id").GetString();
                AssetIndexUrl = assetIndexEl.GetProperty("url").GetString();
                AssetIndexSha1 = assetIndexEl.GetProperty("sha1").GetString();

                string clientUrl = root.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString();
                string clientSha1 = root.GetProperty("downloads").GetProperty("client").GetProperty("sha1").GetString();

                ClientJarPath = Path.Combine(MinecraftPath, "versions", versionId, $"{versionId}.jar");
                await _downloader.DownloadFileAsync(clientUrl, ClientJarPath, clientSha1);

                JsonElement libraries = root.GetProperty("libraries");
                foreach (JsonElement lib in libraries.EnumerateArray())
                {
                    if (lib.TryGetProperty("rules", out JsonElement rules))
                        if (!IsLibraryAllowed(rules)) continue;

                    if (!lib.TryGetProperty("downloads", out JsonElement downloads)) continue;
                    if (!downloads.TryGetProperty("artifact", out JsonElement artifact)) continue;

                    Libraries.Add(new LibraryInfo
                    {
                        Path = artifact.GetProperty("path").GetString(),
                        Url = artifact.GetProperty("url").GetString(),
                        Sha1 = artifact.GetProperty("sha1").GetString()
                    });
                }
            }
        }

        public async Task<string> DownloadLibrariesAsync(IProgress<string> progress = null)
        {
            var classpathParts = new List<string>();

            foreach (var lib in Libraries)
            {
                string destPath = Path.Combine(MinecraftPath, "libraries", lib.Path.Replace('/', Path.DirectorySeparatorChar));

                progress?.Report($"Descargando: {Path.GetFileName(destPath)}");
                await _downloader.DownloadFileAsync(lib.Url, destPath, lib.Sha1);

                classpathParts.Add(destPath);
            }

            classpathParts.Add(ClientJarPath);
            return string.Join(";", classpathParts);
        }

        public async Task ExtractNativesAsync(IProgress<string> progress = null)
        {
            string nativesPath = Path.Combine(MinecraftPath, "versions", Id, "natives");
            Directory.CreateDirectory(nativesPath);

            string json = await _http.GetStringAsync(_versionUrl);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement libraries = doc.RootElement.GetProperty("libraries");

                foreach (JsonElement lib in libraries.EnumerateArray())
                {
                    // ¿Tiene sección "natives" con entrada para windows?
                    if (!lib.TryGetProperty("natives", out JsonElement nativesEl)) continue;
                    if (!nativesEl.TryGetProperty("windows", out JsonElement winClassifier)) continue;

                    string classifier = winClassifier.GetString(); // ej: "natives-windows"

                    if (!lib.TryGetProperty("downloads", out JsonElement downloads)) continue;
                    if (!downloads.TryGetProperty("classifiers", out JsonElement classifiers)) continue;
                    if (!classifiers.TryGetProperty(classifier, out JsonElement nativeArtifact)) continue;

                    string url = nativeArtifact.GetProperty("url").GetString();
                    string sha1 = nativeArtifact.GetProperty("sha1").GetString();
                    string path = nativeArtifact.GetProperty("path").GetString();

                    string destJar = Path.Combine(MinecraftPath, "libraries", path.Replace('/', Path.DirectorySeparatorChar));

                    progress?.Report($"Descargando native: {System.IO.Path.GetFileName(destJar)}");
                    await _downloader.DownloadFileAsync(url, destJar, sha1);

                    progress?.Report($"Extrayendo: {System.IO.Path.GetFileName(destJar)}");
                    ExtractNativeJar(destJar, nativesPath);
                }
            }
        }

        private void ExtractNativeJar(string jarPath, string destFolder)
        {
            using (var zip = ZipFile.OpenRead(jarPath))
            {
                foreach (var entry in zip.Entries)
                {
                    // Solo DLLs, ignorar META-INF
                    if (entry.FullName.StartsWith("META-INF")) continue;
                    if (!entry.Name.EndsWith(".dll") &&
                        !entry.Name.EndsWith(".so") &&
                        !entry.Name.EndsWith(".dylib")) continue;

                    string destPath = Path.Combine(destFolder, entry.Name);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }

        private bool IsLibraryAllowed(JsonElement rules)
        {
            bool allowed = false;

            foreach (JsonElement rule in rules.EnumerateArray())
            {
                string action = rule.GetProperty("action").GetString();

                if (rule.TryGetProperty("os", out JsonElement os))
                {
                    string osName = os.GetProperty("name").GetString();
                    bool isWindows = osName == "windows";

                    if (action == "allow" && isWindows) allowed = true;
                    if (action == "disallow" && isWindows) allowed = false;
                }
                else
                {
                    allowed = action == "allow";
                }
            }

            return allowed;
        }
    }
}