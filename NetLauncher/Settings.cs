using System;
using System.IO;
using System.Text.Json;

namespace NetLauncher
{
    public class Settings
    {
        public string LastVersion { get; set; }
        public string LastUsername { get; set; }
        public int MaxRamMb { get; set; } = 2048;  // ← nuevo
        public bool ShowSnapshots { get; set; } = false; // ← nuevo
        public string ExtraJvmArgs { get; set; } = "";

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".NetLauncher", "launcher_settings.json"
        );

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        return new Settings
                        {
                            LastVersion = root.TryGetProperty("LastVersion", out JsonElement v) ? v.GetString() : null,
                            LastUsername = root.TryGetProperty("LastUsername", out JsonElement u) ? u.GetString() : null,
                            MaxRamMb = root.TryGetProperty("MaxRamMb", out JsonElement r) ? r.GetInt32() : 2048,
                            ShowSnapshots = root.TryGetProperty("ShowSnapshots", out JsonElement s) ? s.GetBoolean() : false,
                            ExtraJvmArgs = root.TryGetProperty("ExtraJvmArgs", out JsonElement j) ? j.GetString() : ""
                        };
                    }
                }
            }
            catch { }

            return new Settings(); // si falla, devuelve vacío
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));

                string json = JsonSerializer.Serialize(new
                {
                    LastVersion = LastVersion,
                    LastUsername = LastUsername,
                    MaxRamMb = MaxRamMb,
                    ShowSnapshots = ShowSnapshots,
                    ExtraJvmArgs = ExtraJvmArgs
                });

                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}