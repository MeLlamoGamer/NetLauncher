using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace NetLauncher
{
    public class GameLauncher
    {
        public Process Launch(VersionDetail detail, string classpath, OfflineSession session, string assetIndex)
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
                jvmArgs = BuildNewJvmArgs(detail.JvmArguments, nativesPath, classpath, mcPath, detail.Id);
            }
            else
            {
                gameArgs = detail.MinecraftArguments
                .Replace("${auth_player_name}", session.Username)
                .Replace("${auth_username}", session.Username)   // ← formato muy viejo
                .Replace("${auth_uuid}", session.UUID)
                .Replace("${auth_access_token}", session.AccessToken)
                .Replace("${auth_session}", session.AccessToken)
                .Replace("${game_directory}", $"\"{mcPath}\"")
                .Replace("${assets_root}", $"\"{assetsPath}\"")
                .Replace("${game_assets}", $"\"{assetsPath}\"") // ← formato muy viejo
                .Replace("${assets_index_name}", assetIndex)
                .Replace("${version_name}", detail.Id)
                .Replace("${user_type}", "offline")
                .Replace("${user_properties}", "{}");

                jvmArgs = $"-Xmx2G -Djava.library.path=\"{nativesPath}\" -cp \"{classpath}\"";
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

            process.OutputDataReceived += (s, e) => { if (e.Data != null) File.AppendAllText(logPath, "\n[OUT] " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) File.AppendAllText(logPath, "\n[ERR] " + e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        private string FindJava(int requiredMajor)
        {
            string[] searchRoots = new[]
            {
                @"C:\Program Files\Java",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\BellSoft",
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

        private int GetJavaVersion(string javaExe)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = javaExe,
                        Arguments = "-XshowSettings:property -version",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                string output = p.StandardError.ReadToEnd();
                p.WaitForExit();

                // Buscar "java.version = 21.0.x" o similar
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains("java.version"))
                    {
                        string val = line.Split('=')[1].Trim(); // "21.0.3"
                        string major = val.Split('.')[0];        // "21"

                        // Java 8 se reporta como "1.8.x"
                        if (major == "1")
                            major = val.Split('.')[1];

                        if (int.TryParse(major, out int v))
                            return v;
                    }
                }
            }
            catch { }

            return 0;
        }

        private string BuildNewGameArgs(List<string> args, OfflineSession session, string mcPath, string assetsPath, string assetIndex, string versionId)
        {
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

        private string BuildNewJvmArgs(List<string> args, string nativesPath, string classpath, string mcPath, string versionId)
        {
            if (args == null || args.Count == 0)
                return $"-Xmx2G -Djava.library.path=\"{nativesPath}\" -cp \"{classpath}\"";

            var result = new List<string>();

            foreach (string arg in args)
            {
                string resolved = arg
                    .Replace("${natives_directory}", $"\"{nativesPath}\"")
                    .Replace("${launcher_name}", "NetLauncher")
                    .Replace("${launcher_version}", "1.0")
                    .Replace("${classpath}", $"\"{classpath}\"");

                result.Add(resolved);
            }

            string joined = string.Join(" ", result);
            if (!joined.Contains("-Xmx"))
                joined = "-Xmx2G " + joined;

            return joined;
        }
    }
}