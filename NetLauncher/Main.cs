using System;
using System.Diagnostics;
using System.Drawing;
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

        private async void Main_Load(object sender, EventArgs e)
        {
            playButton.Enabled = false;
            mcVersion.Text = "Cargando...";

            _settings = Settings.Load();

            if (!string.IsNullOrEmpty(_settings.LastUsername))
                playerName.Text = _settings.LastUsername;

            try
            {
                await _versionManager.LoadVersionsAsync();
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

            _settings.LastVersion = selectedId;
            _settings.LastUsername = username;
            _settings.Save();

            playButton.Enabled = false;
            playButton.Text = "Descargando...";

            var progress = new Progress<string>(msg => playButton.Text = msg);

            try
            {
                // 1. Obtener URL de la versión seleccionada
                var mcVer = _versionManager.Versions.Find(v => v.Id == selectedId);

                // 2. Cargar detalle y descargar client.jar + librerías
                var detail = new VersionDetail();
                await detail.LoadAsync(mcVer.Url, mcVer.Id);
                string classpath = await detail.DownloadLibrariesAsync(progress);

                await detail.ExtractNativesAsync(progress);

                // 3. Descargar assets
                await _assetManager.DownloadAssetsAsync(
                    detail.AssetIndexUrl,
                    detail.AssetIndex,
                    detail.AssetIndexSha1,
                    progress
                );

                // 4. Crear sesión offline
                var session = AuthManager.CreateOfflineSession(username);

                // 5. Lanzar el juego
                playButton.Text = "Lanzando...";
                Process gameProcess = _gameLauncher.Launch(detail, classpath, session, detail.AssetIndex);
                playButton.Text = "¡Jugando!";

                await Task.Run(() => gameProcess.WaitForExit());

                // El juego se cerró — resetear botón
                playButton.Text = "Play";
                playButton.Enabled = true;

                
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al lanzar:\n{ex.Message}", "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                playButton.Text = "Play";
                playButton.Enabled = true;
            }
        }
    }
}