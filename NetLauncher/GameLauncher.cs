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

            string jvmArgs;
            string gameArgs;

            if (detail.IsNewFormat)
            {
                // ── Formato nuevo 1.13+ ──────────────────────────────────────
                gameArgs = BuildNewGameArgs(detail.GameArguments, session, mcPath, assetsPath, assetIndex, detail.Id);
                jvmArgs = BuildNewJvmArgs(detail.JvmArguments, nativesPath, classpath, mcPath, detail.Id);
            }
            else
            {
                // ── Formato viejo 1.12- ──────────────────────────────────────
                gameArgs = detail.MinecraftArguments
                    .Replace("${auth_player_name}", session.Username)
                    .Replace("${auth_uuid}", session.UUID)
                    .Replace("${auth_access_token}", session.AccessToken)
                    .Replace("${auth_session}", session.AccessToken)
                    .Replace("${game_directory}", $"\"{mcPath}\"")
                    .Replace("${assets_root}", $"\"{assetsPath}\"")
                    .Replace("${assets_index_name}", assetIndex)
                    .Replace("${version_name}", detail.Id)
                    .Replace("${user_type}", "offline")
                    .Replace("${user_properties}", "{}");

                jvmArgs = $"-Xmx2G -Djava.library.path=\"{nativesPath}\" -cp \"{classpath}\"";
            }

            string fullArgs = $"{jvmArgs} {detail.MainClass} {gameArgs}";

            string logPath = Path.Combine(mcPath, "launcher_debug.log");
            File.WriteAllText(logPath, $"java {fullArgs}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "java",
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
            // Si Mojang no proveyó JVM args, usamos los mínimos necesarios
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

            // Agregar memoria si Mojang no la incluyó
            string joined = string.Join(" ", result);
            if (!joined.Contains("-Xmx"))
                joined = "-Xmx2G " + joined;

            return joined;
        }
    }
}