using System;
using System.IO;
using System.Text.Json;

namespace NetLauncher
{
    public class Settings
    {
        public string LastVersion { get; set; }
        public string LastUsername { get; set; }

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
                            LastUsername = root.TryGetProperty("LastUsername", out JsonElement u) ? u.GetString() : null
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
                    LastUsername = LastUsername
                });

                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}