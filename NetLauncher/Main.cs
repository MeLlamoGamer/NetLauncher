using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetLauncher
{
    public partial class Main : Form
    {
        private readonly VersionManager _versionManager = new VersionManager();
        private readonly AssetManager _assetManager = new AssetManager();
        private readonly GameLauncher _gameLauncher = new GameLauncher();
        private readonly FabricManager _fabricManager = new FabricManager();
        private Settings _settings;

        public Main()
        {
            InitializeComponent();
            this.Load += Main_Load;
            playButton.Click += PlayButton_Click;
        }

        private async void Main_Load(object sender, EventArgs e)
        {
            progressBar.Visible = false;
            playButton.Enabled = false;
            mcVersion.Text = "Cargando...";

            _settings = Settings.Load();

            if (!string.IsNullOrEmpty(_settings.LastUsername))
                playerName.Text = _settings.LastUsername;

            try
            {
                await ReloadVersionsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando versiones:\n{ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void SettingsButton_Click(object sender, EventArgs e)
        {
            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                    await ReloadVersionsAsync();
            }
        }

        private async Task ReloadVersionsAsync()
        {
            File.AppendAllText(
                Path.Combine(VersionDetail.MinecraftPath, "launcher_debug.log"),
                $"\n[DEBUG] ReloadVersionsAsync llamado - {DateTime.Now}"
            );

            playButton.Enabled = false;
            mcVersion.Items.Clear();

            await _versionManager.LoadVersionsAsync(_settings.ShowSnapshots);
            foreach (var v in _versionManager.Versions)
                mcVersion.Items.Add(v.Id);

            string logPath = Path.Combine(VersionDetail.MinecraftPath, "launcher_debug.log");
            File.AppendAllText(logPath, $"\n[DEBUG] ShowFabric: {_settings.ShowFabric}");

            if (_settings.ShowFabric)
            {
                await _fabricManager.LoadVersionsAsync(_settings.ShowSnapshots);

                File.AppendAllText(logPath, $"\n[DEBUG] Fabric versions count: {_fabricManager.Versions.Count}");

                foreach (var v in _fabricManager.Versions)
                {
                    File.AppendAllText(logPath, $"\n[DEBUG] Agregando: {v.DisplayName}");
                    mcVersion.Items.Add(v.DisplayName);
                }
            }

            if (!string.IsNullOrEmpty(_settings.LastVersion))
            {
                int idx = mcVersion.Items.IndexOf(_settings.LastVersion);
                mcVersion.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else if (mcVersion.Items.Count > 0)
            {
                mcVersion.SelectedIndex = 0;
            }

            playButton.Enabled = true;
        }

        private async void PlayButton_Click(object sender, EventArgs e)
        {
            if (mcVersion.SelectedItem == null) return;

            string selectedId = mcVersion.SelectedItem.ToString();
            string username = playerName.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Ingresá un nombre de jugador.", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Guardar settings
            _settings.LastVersion = selectedId;
            _settings.LastUsername = username;
            _settings.Save();

            playButton.Enabled = false;
            playButton.Text = "Cargando...";
            progressBar.Visible = true;
            progressBar.Value = 0;

            try
            {
                var detail = new VersionDetail();

                if (selectedId.StartsWith("FB "))
                {
                    string mcVerId = selectedId.Replace("FB ", "");
                    var fabricVer = _fabricManager.Versions.Find(v => v.McVersion == mcVerId);
                    await detail.LoadFabricAsync(fabricVer.McVersion, fabricVer.LoaderVersion);
                }
                else
                {
                    var mcVer = _versionManager.Versions.Find(v => v.Id == selectedId);
                    await detail.LoadAsync(mcVer.Url, mcVer.Id);
                }

                progressBar.Value = 5;

                // Verificar Java
                string javaError = _gameLauncher.CheckJava(detail.JavaMajorVersion);
                if (javaError != null)
                {
                    MessageBox.Show(javaError, "Java requerido",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetUI();
                    return;
                }

                // Librerías (10% → 40%)
                progressBar.Value = 10;
                var libProgress = new Progress<int>(pct =>
                    progressBar.Value = 10 + (int)(pct * 0.30));
                string classpath = await detail.DownloadLibrariesAsync(libProgress);

                // Natives (40% → 55%)
                progressBar.Value = 40;
                var nativeProgress = new Progress<int>(pct =>
                    progressBar.Value = 40 + (int)(pct * 0.15));

                if (detail.IsFabric)
                    await detail.ExtractNativesForFabricAsync(nativeProgress);
                else
                    await detail.ExtractNativesAsync(nativeProgress);

                // Assets (55% → 95%)
                progressBar.Value = 55;
                var assetProgress = new Progress<int>(pct =>
                    progressBar.Value = 55 + (int)(pct * 0.40));

                if (detail.IsLegacyAssets)
                {
                    await _assetManager.DownloadLegacyAssetsAsync(
                        detail.AssetIndexUrl, detail.AssetIndex, detail.AssetIndexSha1, assetProgress);
                    _assetManager.ExtractSoundsFromJar(detail.Id);
                }
                else
                {
                    await _assetManager.DownloadAssetsAsync(
                        detail.AssetIndexUrl, detail.AssetIndex, detail.AssetIndexSha1, assetProgress);
                }

                if (detail.MapToResources)
                    _assetManager.MapAssetsToResources(detail.AssetIndex);

                // Lanzar
                progressBar.Value = 95;
                playButton.Text = "Lanzando...";
                Application.DoEvents();

                var session = AuthManager.CreateOfflineSession(username);
                Process gameProcess = _gameLauncher.Launch(detail, classpath, session, detail.AssetIndex, _settings.MaxRamMb, _settings.ExtraJvmArgs);

                progressBar.Value = 100;
                playButton.Text = "¡Jugando!";
                Application.DoEvents();

                await Task.Run(() => gameProcess.WaitForExit());

                int exitCode = gameProcess.ExitCode;
                if (exitCode != 0)
                {
                    string logPath = Path.Combine(VersionDetail.MinecraftPath, "launcher_debug.log");
                    string logContent = File.Exists(logPath) ? File.ReadAllText(logPath) : "Sin log disponible";
                    MessageBox.Show($"El juego cerró con error (código {exitCode}).\n\nLog:\n{logContent}",
                                    "Error al lanzar", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al lanzar:\n{ex.Message}\n\nStackTrace:\n{ex.StackTrace}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ResetUI();
            }
        }

        private void McVersion_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            string versionId = mcVersion.Items[e.Index].ToString();
            string versionJson;

            if (versionId.StartsWith("FB "))
            {
                string mcVerId = versionId.Replace("FB ", "");
                versionJson = Path.Combine(VersionDetail.MinecraftPath, "versions", $"fabric-{mcVerId}", $"fabric-{mcVerId}.json");
            }
            else
            {
                versionJson = Path.Combine(VersionDetail.MinecraftPath, "versions", versionId, $"{versionId}.json");
            }

            bool isCached = File.Exists(versionJson);

            Color backColor = (e.State & DrawItemState.Selected) != 0
                ? SystemColors.Highlight
                : SystemColors.Window;

            e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);

            Color foreColor = (e.State & DrawItemState.Selected) != 0
                ? SystemColors.HighlightText
                : SystemColors.WindowText;

            Font font = isCached
                ? new Font(e.Font, FontStyle.Bold)
                : e.Font;

            e.Graphics.DrawString(versionId, font, new SolidBrush(foreColor), e.Bounds);

            if (isCached) font.Dispose();
             
            e.DrawFocusRectangle();
        }

        private void ResetUI()
        {
            playButton.Text = "Play";
            playButton.Enabled = true;
            progressBar.Visible = false;
            progressBar.Value = 0;
        }
    }
}