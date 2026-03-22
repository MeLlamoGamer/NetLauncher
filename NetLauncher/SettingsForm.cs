using System;
using System.Windows.Forms;

namespace NetLauncher
{
    public partial class SettingsForm : Form
    {
        private Settings _settings;

        private TrackBar ramSlider;
        private Label ramLabel;
        private CheckBox snapshotsCheck;
        private TextBox extraJvmBox;
        private Label extraJvmLabel;
        private Button saveButton;
        private Button cancelButton;
        private CheckBox fabricCheck;

        public SettingsForm(Settings settings)
        {
            _settings = settings;
            BuildUI();
            LoadValues();
        }

        private void BuildUI()
        {
            this.Text = "Configuración";
            this.ClientSize = new System.Drawing.Size(380, 220);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            // RAM label
            var ramTitleLabel = new Label
            {
                Text = "Memoria RAM:",
                Location = new System.Drawing.Point(12, 15),
                AutoSize = true
            };

            // RAM actual
            ramLabel = new Label
            {
                Text = "2048 MB",
                Location = new System.Drawing.Point(290, 15),
                AutoSize = true
            };

            // Slider de RAM (512MB a 16384MB en pasos de 512)
            ramSlider = new TrackBar
            {
                Location = new System.Drawing.Point(12, 35),
                Size = new System.Drawing.Size(356, 45),
                Minimum = 1,
                Maximum = 32,   // 32 * 512 = 16384 MB
                TickFrequency = 2,
                SmallChange = 1,
                LargeChange = 2
            };
            ramSlider.ValueChanged += (s, e) =>
            {
                int mb = ramSlider.Value * 512;
                ramLabel.Text = mb >= 1024
                    ? $"{mb / 1024.0:0.#} GB ({mb} MB)"
                    : $"{mb} MB";
            };

            // Snapshots
            snapshotsCheck = new CheckBox
            {
                Text = "Mostrar snapshots en la lista de versiones",
                Location = new System.Drawing.Point(12, 80),
                AutoSize = true
            };

            fabricCheck = new CheckBox
            {
                Text = "Mostrar versiones de Fabric",
                Location = new System.Drawing.Point(12, 110),
                AutoSize = true
            };

            // Extra JVM args
            extraJvmLabel = new Label
            {
                Text = "Argumentos JVM extra:",
                Location = new System.Drawing.Point(12, 135),
                AutoSize = true
            };

            extraJvmBox = new TextBox
            {
                Location = new System.Drawing.Point(12, 155),
                Size = new System.Drawing.Size(356, 20),
                // PlaceholderText = "-XX:+UseG1GC -XX:+UnlockExperimentalVMOptions"
            };

            // Botones
            saveButton = new Button
            {
                Text = "Guardar",
                Location = new System.Drawing.Point(196, 180),
                Size = new System.Drawing.Size(80, 28)
            };
            saveButton.Click += SaveButton_Click;

            cancelButton = new Button
            {
                Text = "Cancelar",
                Location = new System.Drawing.Point(288, 180),
                Size = new System.Drawing.Size(80, 28)
            };
            cancelButton.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[]
            {
                ramTitleLabel, ramLabel, ramSlider,
                snapshotsCheck,
                fabricCheck,
                extraJvmLabel, extraJvmBox,
                saveButton, cancelButton
            });
        }

        private void LoadValues()
        {
            ramSlider.Value = Math.Max(1, Math.Min(32, _settings.MaxRamMb / 512));
            int mb = ramSlider.Value * 512;
            ramLabel.Text = mb >= 1024 ? $"{mb / 1024.0:0.#} GB ({mb} MB)" : $"{mb} MB";
            snapshotsCheck.Checked = _settings.ShowSnapshots;
            extraJvmBox.Text = _settings.ExtraJvmArgs;
            fabricCheck.Checked = _settings.ShowFabric;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            _settings.MaxRamMb = ramSlider.Value * 512;
            _settings.ShowSnapshots = snapshotsCheck.Checked;
            _settings.ExtraJvmArgs = extraJvmBox.Text.Trim();
            _settings.ShowFabric = fabricCheck.Checked;
            _settings.Save();

            this.DialogResult = DialogResult.OK;
        }
    }
}