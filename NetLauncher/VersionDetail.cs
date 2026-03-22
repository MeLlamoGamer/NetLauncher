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
        public int JavaMajorVersion { get; private set; } = 8; // default para versiones viejas
        public bool IsFabric { get; private set; } = false;
        public string FabricMcVersion { get; private set; }
        public List<LibraryInfo> Libraries { get; private set; } = new List<LibraryInfo>();
        public bool IsLegacyAssets { get; private set; } = false;
        public bool MapToResources { get; private set; } = false;

        public static string MinecraftPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher"
        );

        public async Task LoadAsync(string versionUrl, string versionId)
        {
            Id = versionId;
            _versionUrl = versionUrl; // guardar para ExtractNativesAsync

            string versionCachePath = Path.Combine(MinecraftPath, "versions", versionId, $"{versionId}.json");
            string json;

            if (File.Exists(versionCachePath))
            {
                // Ya está cacheado — usar local
                json = File.ReadAllText(versionCachePath);
            }
            else
            {
                // Descargar y cachear
                json = await _http.GetStringAsync(versionUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(versionCachePath));
                File.WriteAllText(versionCachePath, json);
            }

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;

                MainClass = root.GetProperty("mainClass").GetString();

                // Detectar versión de Java requerida
                if (root.TryGetProperty("javaVersion", out JsonElement javaVersionEl))
                {
                    if (javaVersionEl.TryGetProperty("majorVersion", out JsonElement majorEl))
                        JavaMajorVersion = majorEl.GetInt32();
                }

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

                // Detectar legacy por release time — versiones anteriores a 1.6 (junio 2013)
                if (root.TryGetProperty("releaseTime", out JsonElement releaseTimeEl))
                {
                    string releaseTime = releaseTimeEl.GetString(); // "2012-03-29T22:00:00+00:00"
                    if (DateTime.TryParse(releaseTime, out DateTime releaseDate))
                    {
                        // 1.6 salió en julio 2013 — todo lo anterior usa assets legacy
                        IsLegacyAssets = releaseDate < new DateTime(2013, 11, 01);
                    }
                }
                // 1.6.x — usa el nuevo sistema de assets pero además necesita copiarlos a /resources
                if (root.TryGetProperty("assetIndex", out JsonElement assetIdxCheck))
                {
                    if (assetIdxCheck.TryGetProperty("map_to_resources", out JsonElement mapEl))
                        MapToResources = mapEl.GetBoolean();
                }

                File.AppendAllText(
                    Path.Combine(MinecraftPath, "version_debug.log"),
                    $"\n[DEBUG] Id: {Id} | AssetIndex: {AssetIndex} | ReleaseTime: {(root.TryGetProperty("releaseTime", out JsonElement rt) ? rt.GetString() : "N/A")}"
                );

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

        public async Task ExtractNativesForFabricAsync(IProgress<int> progress = null)
        {
            string nativesPath = Path.Combine(MinecraftPath, "versions", Id, "natives");
            Directory.CreateDirectory(nativesPath);

            // Las natives vienen del JSON vanilla, no del JSON de Fabric
            string vanillaCachePath = Path.Combine(MinecraftPath, "versions", FabricMcVersion, $"{FabricMcVersion}.json");
            if (!File.Exists(vanillaCachePath)) return;

            string json = File.ReadAllText(vanillaCachePath);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement libraries = doc.RootElement.GetProperty("libraries");

                foreach (JsonElement lib in libraries.EnumerateArray())
                {
                    if (!lib.TryGetProperty("natives", out JsonElement nativesEl)) continue;
                    if (!nativesEl.TryGetProperty("windows", out JsonElement winClassifier)) continue;

                    string classifier = winClassifier.GetString();

                    if (!lib.TryGetProperty("downloads", out JsonElement downloads)) continue;
                    if (!downloads.TryGetProperty("classifiers", out JsonElement classifiers)) continue;
                    if (!classifiers.TryGetProperty(classifier, out JsonElement nativeArtifact)) continue;

                    string url = nativeArtifact.GetProperty("url").GetString();
                    string sha1 = nativeArtifact.GetProperty("sha1").GetString();
                    string path = nativeArtifact.GetProperty("path").GetString();

                    string destJar = Path.Combine(MinecraftPath, "libraries", path.Replace('/', Path.DirectorySeparatorChar));

                    progress?.Report(50);
                    await _downloader.DownloadFileAsync(url, destJar, sha1);

                    progress?.Report(75);
                    ExtractNativeJar(destJar, nativesPath);
                }
            }
        }

        public async Task LoadFabricAsync(string mcVersion, string loaderVersion, IProgress<int> progress = null)
        {
            IsFabric = true;
            FabricMcVersion = mcVersion;

            string profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json";

            // Cache local
            string cacheDir = Path.Combine(MinecraftPath, "versions", $"fabric-{mcVersion}");
            string cachePath = Path.Combine(cacheDir, $"fabric-{mcVersion}.json");

            string json;
            if (File.Exists(cachePath))
            {
                json = File.ReadAllText(cachePath);
            }
            else
            {
                try
                {
                    json = await _http.GetStringAsync(profileUrl);
                    Directory.CreateDirectory(cacheDir);
                    File.WriteAllText(cachePath, json);
                }
                catch
                {
                    throw new Exception($"No se pudo descargar el perfil de Fabric para {mcVersion} y no hay cache disponible.\nConectate a internet al menos una vez para cachear esta versión.");
                }
            }

            Id = $"fabric-{mcVersion}";
            _versionUrl = profileUrl;

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;

                MainClass = root.GetProperty("mainClass").GetString();

                // Fabric siempre usa formato nuevo
                IsNewFormat = true;

                if (root.TryGetProperty("arguments", out JsonElement newArgs))
                {
                    if (newArgs.TryGetProperty("game", out JsonElement gameArgsEl))
                        foreach (JsonElement arg in gameArgsEl.EnumerateArray())
                            if (arg.ValueKind == JsonValueKind.String)
                                GameArguments.Add(arg.GetString());

                    if (newArgs.TryGetProperty("jvm", out JsonElement jvmEl))
                        foreach (JsonElement arg in jvmEl.EnumerateArray())
                            if (arg.ValueKind == JsonValueKind.String)
                                JvmArguments.Add(arg.GetString());
                }

                // Asset index — Fabric hereda el de la versión vanilla base
                // Asset index — intentar obtenerlo del JSON de Fabric, si no del vanilla
                if (root.TryGetProperty("assetIndex", out JsonElement assetIndexEl))
                {
                    AssetIndex = assetIndexEl.GetProperty("id").GetString();
                    AssetIndexUrl = assetIndexEl.GetProperty("url").GetString();
                    AssetIndexSha1 = assetIndexEl.GetProperty("sha1").GetString();
                }
                // Si no tiene assetIndex, GetVanillaClientUrlAsync lo va a completar

                // Client JAR — Fabric usa el vanilla, hay que bajarlo del manifest de Mojang
                string vanillaUrl = await GetVanillaClientUrlAsync(mcVersion);
                ClientJarPath = Path.Combine(MinecraftPath, "versions", $"fabric-{mcVersion}", $"fabric-{mcVersion}.jar");
                if (!string.IsNullOrEmpty(vanillaUrl))
                    await _downloader.DownloadFileAsync(vanillaUrl, ClientJarPath, null);

                // Java version
                if (root.TryGetProperty("javaVersion", out JsonElement javaVersionEl))
                    if (javaVersionEl.TryGetProperty("majorVersion", out JsonElement majorEl))
                        JavaMajorVersion = majorEl.GetInt32();

                // Librerías
                // Las librerías de Fabric tienen formato distinto al vanilla
                JsonElement libraries = root.GetProperty("libraries");
                foreach (JsonElement lib in libraries.EnumerateArray())
                {
                    // Formato Fabric: tiene "name", "url" directamente, sin "downloads"
                    if (lib.TryGetProperty("downloads", out JsonElement downloads))
                    {
                        // Formato vanilla dentro del profile de Fabric
                        if (!downloads.TryGetProperty("artifact", out JsonElement artifact)) continue;
                        Libraries.Add(new LibraryInfo
                        {
                            Path = artifact.GetProperty("path").GetString(),
                            Url = artifact.GetProperty("url").GetString(),
                            Sha1 = artifact.GetProperty("sha1").GetString()
                        });
                    }
                    else if (lib.TryGetProperty("name", out JsonElement nameEl))
                    {
                        // Formato Fabric nativo: "name": "net.fabricmc:fabric-loader:0.16.10"
                        // Hay que construir la ruta Maven desde el nombre
                        string name = nameEl.GetString();
                        string baseUrl = lib.TryGetProperty("url", out JsonElement urlEl)
                                         ? urlEl.GetString()
                                         : "https://repo1.maven.org/maven2/";

                        string mavenPath = MavenNameToPath(name);
                        string fullUrl = baseUrl.TrimEnd('/') + "/" + mavenPath.Replace('\\', '/');

                        Libraries.Add(new LibraryInfo
                        {
                            Path = mavenPath,
                            Url = fullUrl,
                            Sha1 = null
                        });
                    }
                }
            }

            // Agregar el JAR vanilla al classpath con ruta absoluta
            string vanillaJarPath = Path.Combine(MinecraftPath, "versions", mcVersion, $"{mcVersion}.jar");
            if (!File.Exists(vanillaJarPath))
            {
                try
                {
                    string vanillaClientUrl = await GetVanillaClientUrlAsync(mcVersion);
                    if (!string.IsNullOrEmpty(vanillaClientUrl))
                        await _downloader.DownloadFileAsync(vanillaClientUrl, vanillaJarPath, null);
                }
                catch
                {
                    throw new Exception($"No se pudo descargar el JAR de Minecraft {mcVersion}.\nConectate a internet al menos una vez para cachear esta versión de Fabric.");
                }
            }

            Libraries.Add(new LibraryInfo
            {
                Path = vanillaJarPath, // ← ruta absoluta, no relativa
                Url = "",
                Sha1 = null
            });

            // Sobreescribir ClientJarPath con el JAR de Fabric loader
            ClientJarPath = Path.Combine(MinecraftPath, "versions", $"fabric-{mcVersion}", $"fabric-{mcVersion}.jar");

            // Crear carpeta mods si no existe
            // Al final de LoadFabricAsync, justo antes de crear la carpeta mods
            string vanillaCachePathFinal = Path.Combine(MinecraftPath, "versions", mcVersion, $"{mcVersion}.json");
            if (File.Exists(vanillaCachePathFinal))
            {
                string vanillaJson = File.ReadAllText(vanillaCachePathFinal);
                using (JsonDocument vanillaDoc = JsonDocument.Parse(vanillaJson))
                {
                    if (vanillaDoc.RootElement.TryGetProperty("javaVersion", out JsonElement jvEl))
                        if (jvEl.TryGetProperty("majorVersion", out JsonElement majEl))
                            JavaMajorVersion = majEl.GetInt32();
                }
            }

            // Si aún es 8 (default) y la versión de MC es 1.17+, forzar Java 21
            if (JavaMajorVersion < 21)
                JavaMajorVersion = 21;

            Directory.CreateDirectory(Path.Combine(MinecraftPath, "mods"));
        }

        private string MavenNameToPath(string mavenName)
        {
            // "net.fabricmc:fabric-loader:0.16.10"
            // → "net/fabricmc/fabric-loader/0.16.10/fabric-loader-0.16.10.jar"
            string[] parts = mavenName.Split(':');
            if (parts.Length < 3) return mavenName;

            string group = parts[0].Replace('.', Path.DirectorySeparatorChar);
            string artifact = parts[1];
            string version = parts[2];

            return Path.Combine(group, artifact, version, $"{artifact}-{version}.jar");
        }

        private async Task<string> GetVanillaClientUrlAsync(string mcVersion)
        {
            try
            {
                // Buscar en el cache local primero
                // Después de llamar GetVanillaClientUrlAsync, leer JavaMajorVersion del vanilla
                // Cargar librerías del vanilla base
                string vanillaCachePath = Path.Combine(MinecraftPath, "versions", mcVersion, $"{mcVersion}.json");
                if (File.Exists(vanillaCachePath))
                {
                    string vanillaJson = File.ReadAllText(vanillaCachePath);
                    using (JsonDocument vanillaDoc = JsonDocument.Parse(vanillaJson))
                    {
                        JsonElement vanillaLibraries = vanillaDoc.RootElement.GetProperty("libraries");
                        foreach (JsonElement lib in vanillaLibraries.EnumerateArray())
                        {
                            if (lib.TryGetProperty("rules", out JsonElement rules))
                                if (!IsLibraryAllowed(rules)) continue;

                            if (!lib.TryGetProperty("downloads", out JsonElement downloads)) continue;
                            if (!downloads.TryGetProperty("artifact", out JsonElement artifact)) continue;

                            string libPath = artifact.GetProperty("path").GetString();

                            // Evitar duplicados
                            if (Libraries.Exists(l => l.Path == libPath)) continue;

                            Libraries.Add(new LibraryInfo
                            {
                                Path = libPath,
                                Url = artifact.GetProperty("url").GetString(),
                                Sha1 = artifact.GetProperty("sha1").GetString()
                            });
                        }
                    }
                }

                string json;
                if (File.Exists(vanillaCachePath))
                {
                    json = File.ReadAllText(vanillaCachePath);
                }
                else
                {
                    // Bajar el manifest para encontrar la URL de esta versión
                    string manifestJson = await _http.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest.json");
                    string versionUrl = null;

                    using (JsonDocument manifest = JsonDocument.Parse(manifestJson))
                    {
                        foreach (JsonElement v in manifest.RootElement.GetProperty("versions").EnumerateArray())
                        {
                            if (v.GetProperty("id").GetString() == mcVersion)
                            {
                                versionUrl = v.GetProperty("url").GetString();
                                break;
                            }
                        }
                    }

                    if (versionUrl == null) return null;

                    json = await _http.GetStringAsync(versionUrl);
                    Directory.CreateDirectory(Path.GetDirectoryName(vanillaCachePath));
                    File.WriteAllText(vanillaCachePath, json);
                }

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    // También aprovechar el assetIndex del vanilla si Fabric no lo tiene
                    if (!doc.RootElement.TryGetProperty("assetIndex", out _))
                        return null;

                    JsonElement assetEl = doc.RootElement.GetProperty("assetIndex");
                    if (string.IsNullOrEmpty(AssetIndex))
                    {
                        AssetIndex = assetEl.GetProperty("id").GetString();
                        AssetIndexUrl = assetEl.GetProperty("url").GetString();
                        AssetIndexSha1 = assetEl.GetProperty("sha1").GetString();
                    }

                    return doc.RootElement
                               .GetProperty("downloads")
                               .GetProperty("client")
                               .GetProperty("url")
                               .GetString();
                }
            }
            catch { return null; }
        }

        public async Task<string> DownloadLibrariesAsync(IProgress<int> progress = null)
        {
            var classpathParts = new List<string>();
            int total = Libraries.Count;
            int current = 0;

            foreach (var lib in Libraries)
            {
                current++;

                // Si la ruta es absoluta, usarla directamente sin descargar
                if (Path.IsPathRooted(lib.Path))
                {
                    classpathParts.Add(lib.Path);
                    progress?.Report((int)((float)current / total * 100));
                    continue;
                }

                string destPath = Path.Combine(MinecraftPath, "libraries", lib.Path.Replace('/', Path.DirectorySeparatorChar));

                if (!string.IsNullOrEmpty(lib.Url))
                    await _downloader.DownloadFileAsync(lib.Url, destPath, lib.Sha1);

                classpathParts.Add(destPath);
                progress?.Report((int)((float)current / total * 100));
            }

            classpathParts.Add(ClientJarPath);
            return string.Join(";", classpathParts);
        }

        public async Task ExtractNativesAsync(IProgress<int> progress = null)
        {
            string nativesPath = Path.Combine(MinecraftPath, "versions", Id, "natives");
            Directory.CreateDirectory(nativesPath);

            string versionCachePath = Path.Combine(MinecraftPath, "versions", Id, $"{Id}.json");
            string json = File.Exists(versionCachePath)
                ? File.ReadAllText(versionCachePath)
                : await _http.GetStringAsync(_versionUrl);

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

                    progress?.Report(50);
                    await _downloader.DownloadFileAsync(url, destJar, sha1);

                    progress?.Report(60);
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