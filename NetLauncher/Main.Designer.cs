namespace NetLauncher
{
    partial class Main
    {
        /// <summary>
        /// Variable del diseñador necesaria.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Limpiar los recursos que se estén usando.
        /// </summary>
        /// <param name="disposing">true si los recursos administrados se deben desechar; false en caso contrario.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Código generado por el Diseñador de Windows Forms

        /// <summary>
        /// Método necesario para admitir el Diseñador. No se puede modificar
        /// el contenido de este método con el editor de código.
        /// </summary>
        private void InitializeComponent()
        {
            this.mcVersion = new System.Windows.Forms.ComboBox();
            this.playButton = new System.Windows.Forms.Button();
            this.title = new System.Windows.Forms.Label();
            this.playerName = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // mcVersion
            // 
            this.mcVersion.FormattingEnabled = true;
            this.mcVersion.Location = new System.Drawing.Point(12, 332);
            this.mcVersion.Name = "mcVersion";
            this.mcVersion.Size = new System.Drawing.Size(74, 21);
            this.mcVersion.TabIndex = 0;
            // 
            // playButton
            // 
            this.playButton.Font = new System.Drawing.Font("Segoe Print", 20F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.playButton.Location = new System.Drawing.Point(101, 305);
            this.playButton.Name = "playButton";
            this.playButton.Size = new System.Drawing.Size(151, 62);
            this.playButton.TabIndex = 1;
            this.playButton.Text = "Play";
            this.playButton.UseVisualStyleBackColor = true;
            // 
            // title
            // 
            this.title.AutoSize = true;
            this.title.Font = new System.Drawing.Font("Segoe Print", 20F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.title.Location = new System.Drawing.Point(71, 9);
            this.title.Name = "title";
            this.title.Size = new System.Drawing.Size(215, 47);
            this.title.TabIndex = 2;
            this.title.Text = "NET Launcher";
            // 
            // playerName
            // 
            this.playerName.Location = new System.Drawing.Point(267, 333);
            this.playerName.Name = "playerName";
            this.playerName.Size = new System.Drawing.Size(84, 20);
            this.playerName.TabIndex = 3;
            this.playerName.Text = "Player";
            // 
            // Main
            // 
            this.ClientSize = new System.Drawing.Size(363, 378);
            this.Controls.Add(this.playerName);
            this.Controls.Add(this.title);
            this.Controls.Add(this.playButton);
            this.Controls.Add(this.mcVersion);
            this.Name = "Main";
            this.Text = "Net Launcher";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox mcVersion;
        private System.Windows.Forms.Button playButton;
        private System.Windows.Forms.Label title;
        private System.Windows.Forms.TextBox playerName;
    }
}

