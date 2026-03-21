using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.LinkLabel;

namespace NetLauncher
{
    public partial class Main : Form
    {
        private readonly VersionManager _versionManager = new VersionManager();
        private readonly AssetManager _assetManager = new AssetManager();
        private readonly GameLauncher _gameLauncher = new GameLauncher();
        private Settings _settings;

        public Main()
        {
            InitializeComponent();
            this.Load += Main_Load;
            playButton.Click += PlayButton_Click;
        }

        private async void SettingsButton_Click(object sender, EventArgs e)
        {
            bool previousSnapshots = _settings.ShowSnapshots;

            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    // Si cambió la opción de snapshots, recargar la lista
                    if (_settings.ShowSnapshots != previousSnapshots)
                    {
                        playButton.Enabled = false;
                        await _versionManager.LoadVersionsAsync(_settings.ShowSnapshots);
                        mcVersion.Items.Clear();

                        foreach (var v in _versionManager.Versions)
                            mcVersion.Items.Add(v.Id);

                        if (mcVersion.Items.Count > 0)
                            mcVersion.SelectedIndex = 0;

                        playButton.Enabled = true;
                    }
                }
            }
        }
        private void McVersion_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            string versionId = mcVersion.Items[e.Index].ToString();

            string versionJson = Path.Combine(VersionDetail.MinecraftPath, "versions", versionId, $"{versionId}.json");
            bool isCached = File.Exists(versionJson);

            // Pintar fondo manualmente
            Color backColor = (e.State & DrawItemState.Selected) != 0
                ? SystemColors.Highlight
                : SystemColors.Window;

            e.Graphics.FillRectangle(new SolidBrush(backColor), e.Bounds);

            // Color del texto
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
                await _versionManager.LoadVersionsAsync(_settings.ShowSnapshots);
                mcVersion.Items.Clear();

                foreach (var v in _versionManager.Versions)
                    mcVersion.Items.Add(v.Id);

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
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando versiones:\n{ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

            playButton.Enabled = false;
            playButton.Text = "Cargando...";
            progressBar.Visible = true;
            progressBar.Value = 0;

            try
            {
                var mcVer = _versionManager.Versions.Find(v => v.Id == selectedId);
                var detail = new VersionDetail();

                // 5% — cargando JSON de versión
                progressBar.Value = 5;
                await detail.LoadAsync(mcVer.Url, mcVer.Id);

                // Verificar Java
                string javaError = _gameLauncher.CheckJava(detail.JavaMajorVersion);
                if (javaError != null)
                {
                    MessageBox.Show(javaError, "Java requerido",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetUI();
                    return;
                }

                // 10% — descargando librerías
                progressBar.Value = 10;
                var libProgress = new Progress<int>(pct =>
                {
                    // Librerías van del 10% al 40%
                    progressBar.Value = 10 + (int)(pct * 0.30);
                });
                string classpath = await detail.DownloadLibrariesAsync(libProgress);

                // 40% — extrayendo natives
                progressBar.Value = 40;
                var nativeProgress = new Progress<int>(pct =>
                {
                    // Natives van del 40% al 55%
                    progressBar.Value = 40 + (int)(pct * 0.15);
                });
                await detail.ExtractNativesAsync(nativeProgress);

                // 55% — descargando assets
                progressBar.Value = 55;
                var assetProgress = new Progress<int>(pct =>
                {
                    // Assets van del 55% al 95%
                    progressBar.Value = 55 + (int)(pct * 0.40);
                });

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

                // 95% — lanzando
                Application.DoEvents();
                progressBar.Value = 95;
                playButton.Text = "Lanzando...";

                var session = AuthManager.CreateOfflineSession(username);
                Process gameProcess = _gameLauncher.Launch(detail, classpath, session, detail.AssetIndex, _settings.MaxRamMb, _settings.ExtraJvmArgs);

                // 100% — jugando
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
                MessageBox.Show($"Error al lanzar:\n{ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ResetUI();
            }
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