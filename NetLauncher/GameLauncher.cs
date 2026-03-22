using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NetLauncher
{
    public class GameLauncher
    {
        public Process Launch(VersionDetail detail, string classpath, OfflineSession session, string assetIndex, int maxRamMb, string extraJvmArgs)
        {
            string mcPath = VersionDetail.MinecraftPath;
            string assetsPath = Path.Combine(mcPath, "assets");
            string nativesPath = Path.Combine(mcPath, "versions", detail.Id, "natives");
            Directory.CreateDirectory(nativesPath);

            // Buscar el ejecutable de Java correcto
            string javaExe = FindJava(detail.JavaMajorVersion);

            string jvmArgs;
            string gameArgs;

            if (detail.IsNewFormat)
            {
                gameArgs = BuildNewGameArgs(detail.GameArguments, session, mcPath, assetsPath, assetIndex, detail.Id);

                if (detail.IsFabric)
                {
                    // Fabric no provee JVM args completos — construirlos manualmente
                    jvmArgs = $"-Xmx{maxRamMb}M " +
                              $"-Djava.library.path=\"{nativesPath}\" " +
                              $"-Dfabric.gameVersion={detail.FabricMcVersion} " +
                              $"-Dminecraft.api.auth.host=https://invalid.invalid " +
                              $"-Dminecraft.api.account.host=https://invalid.invalid " +
                              $"-Dminecraft.api.session.host=https://invalid.invalid " +
                              $"-Dminecraft.api.services.host=https://invalid.invalid " +
                              $"-cp \"{classpath}\"";

                    if (!string.IsNullOrEmpty(extraJvmArgs))
                        jvmArgs += " " + extraJvmArgs;
                }
                else
                {
                    jvmArgs = BuildNewJvmArgs(detail.JvmArguments, nativesPath, classpath, mcPath, detail.Id, maxRamMb, extraJvmArgs);
                }
            }
            else
            {
                string effectiveAssetsPath = detail.IsLegacyAssets
                    ? Path.Combine(mcPath, "resources")
                    : assetsPath;

                gameArgs = detail.MinecraftArguments
                .Replace("${auth_player_name}", session.Username)
                .Replace("${auth_username}", session.Username)   // ← formato muy viejo
                .Replace("${auth_uuid}", session.UUID)
                .Replace("${auth_access_token}", session.AccessToken)
                .Replace("${auth_session}", session.AccessToken)
                .Replace("${game_directory}", $"\"{mcPath}\"")
                .Replace("${assets_root}", $"\"{effectiveAssetsPath}\"")
                .Replace("${game_assets}", $"\"{effectiveAssetsPath}\"")
                .Replace("${assets_index_name}", assetIndex)
                .Replace("${asset_index}", assetIndex)
                .Replace("${version_name}", detail.Id)
                .Replace("${user_type}", "offline")
                .Replace("${user_properties}", "{}");

                jvmArgs = $"-Xmx{maxRamMb}M {extraJvmArgs} -Djava.library.path=\"{nativesPath}\" -cp \"{classpath}\"".Trim();

            }

            string fullArgs = $"{jvmArgs} {detail.MainClass} {gameArgs}";

            string logPath = Path.Combine(mcPath, "launcher_debug.log");
            File.WriteAllText(logPath, $"{javaExe} {fullArgs}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaExe,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendLog(logPath, "\n[OUT] " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendLog(logPath, "\n[ERR] " + e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private static readonly object _logLock = new object();

        private void AppendLog(string logPath, string text)
        {
            lock (_logLock)
            {
                try { File.AppendAllText(logPath, text); }
                catch { }
            }
        }

        private string FindJava(int requiredMajor)
        {
            string[] searchRoots = new[]
            {
                @"C:\Program Files\Java",                    // ← Oracle se instala acá
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\BellSoft",
                @"C:\Program Files\Oracle",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs\Eclipse Adoptium",
            };

            string bestExe = null;
            int bestVersion = int.MaxValue;

            foreach (string root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (string javaDir in Directory.GetDirectories(root))
                {
                    string javaBin = Path.Combine(javaDir, "bin", "java.exe");
                    if (!File.Exists(javaBin)) continue;

                    int version = GetJavaVersion(javaBin);

                    // Tiene que ser >= al requerido, y preferimos el más cercano
                    if (version >= requiredMajor && version < bestVersion)
                    {
                        bestVersion = version;
                        bestExe = javaBin;
                    }
                }
            }

            return bestExe ?? "java";
        }

        public string CheckJava(int requiredMajor)
        {
            string javaExe = FindJava(requiredMajor);

            if (javaExe == "java")
            {
                // No encontró ningún Java instalado
                return $"No se encontró Java instalado.\nDescargalo desde https://adoptium.net";
            }

            int version = GetJavaVersion(javaExe);
            if (version < requiredMajor)
            {
                return $"Esta versión de Minecraft requiere Java {requiredMajor} o superior.\n" +
                       $"Java instalado más reciente: Java {version}\n" +
                       $"Descargá Java {requiredMajor} desde https://adoptium.net";
            }

            return null; // null = todo bien
        }

        private int GetJavaVersion(string javaExe)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaExe,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                // Java imprime la versión en stderr
                string output = p.StandardError.ReadToEnd();
                p.WaitForExit();

                // Formato moderno: openjdk version "25" o java version "25.0.1"
                // Formato viejo: java version "1.8.0_472"
                foreach (string line in output.Split('\n'))
                {
                    if (!line.Contains("version")) continue;

                    // Buscar el número entre comillas
                    int start = line.IndexOf('"');
                    int end = line.LastIndexOf('"');
                    if (start < 0 || end <= start) continue;

                    string versionStr = line.Substring(start + 1, end - start - 1); // ej: "25", "21.0.3", "1.8.0_472"
                    string[] parts = versionStr.Split('.');

                    // Java 8 y anterior: "1.8.x" → major es parts[1]
                    if (parts[0] == "1" && parts.Length > 1)
                        return int.Parse(parts[1]);

                    // Java 9+: "25", "21.0.3" → major es parts[0]
                    if (int.TryParse(parts[0], out int major))
                        return major;
                }
            }
            catch { }

            return 0;
        }

        private string BuildNewGameArgs(List<string> args, OfflineSession session, string mcPath, string assetsPath, string assetIndex, string versionId)
        {
            // Si no hay args definidos, usar los estándar
            if (args == null || args.Count == 0)
            {
                return $"--username {session.Username} " +
                       $"--version {versionId} " +
                       $"--gameDir \"{mcPath}\" " +
                       $"--assetsDir \"{assetsPath}\" " +
                       $"--assetIndex {assetIndex} " +
                       $"--uuid {session.UUID} " +
                       $"--accessToken {session.AccessToken} " +
                       $"--userType offline";
            }

            var result = new List<string>();
            foreach (string arg in args)
            {
                string resolved = arg
                    .Replace("${auth_player_name}", session.Username)
                    .Replace("${auth_uuid}", session.UUID)
                    .Replace("${auth_access_token}", session.AccessToken)
                    .Replace("${auth_session}", session.AccessToken)
                    .Replace("${game_directory}", $"\"{mcPath}\"")
                    .Replace("${assets_root}", $"\"{assetsPath}\"")
                    .Replace("${assets_index_name}", assetIndex)
                    .Replace("${version_name}", versionId)
                    .Replace("${user_type}", "offline")
                    .Replace("${user_properties}", "{}")
                    .Replace("${clientid}", "0")
                    .Replace("${auth_xuid}", "0")
                    .Replace("${version_type}", "release");

                result.Add(resolved);
            }

            return string.Join(" ", result);
        }

        private string BuildNewJvmArgs(List<string> args, string nativesPath, string classpath, string mcPath, string versionId, int maxRamMb, string extraJvmArgs)
        {
            const string offlineArgs = " -Dminecraft.api.auth.host=https://invalid.invalid" +
                                       " -Dminecraft.api.account.host=https://invalid.invalid" +
                                       " -Dminecraft.api.session.host=https://invalid.invalid" +
                                       " -Dminecraft.api.services.host=https://invalid.invalid";

            if (args == null || args.Count == 0)
                return $"-Xmx{maxRamMb}M {extraJvmArgs} -Djava.library.path=\"{nativesPath}\" -cp \"{classpath}\"{offlineArgs}".Trim();

            var blacklist = new[]
            {
                "--sun-misc-unsafe-memory-access=allow",
                "--enable-native-access=ALL-UNNAMED"
            };

            var result = new List<string>();
            foreach (string arg in args)
            {
                bool isBlacklisted = false;
                foreach (string b in blacklist)
                    if (arg.Contains(b)) { isBlacklisted = true; break; }
                if (isBlacklisted) continue;

                string resolved = arg
                    .Replace("${natives_directory}", $"\"{nativesPath}\"")
                    .Replace("${launcher_name}", "NetLauncher")
                    .Replace("${launcher_version}", "1.3")
                    .Replace("${classpath}", $"\"{classpath}\"")
                    .Replace("${version_name}", versionId)
                    .Replace("${library_directory}", $"\"{Path.Combine(VersionDetail.MinecraftPath, "libraries")}\"")
                    .Replace("${classpath_separator}", ";");

                result.Add(resolved);
            }

            string joined = string.Join(" ", result);

            if (!joined.Contains("-Xmx"))
                joined = $"-Xmx{maxRamMb}M " + joined;

            joined += offlineArgs; // ← agregar acá

            if (!string.IsNullOrEmpty(extraJvmArgs))
                joined = joined + " " + extraJvmArgs;

            return joined;
        }
    }
}