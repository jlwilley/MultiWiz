namespace MultiWiz
{
    partial class switcherForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(switcherForm));
            navBar = new Panel();
            button1 = new Button();
            buttonPanel = new Panel();
            navBar.SuspendLayout();
            SuspendLayout();
            // 
            // navBar
            // 
            navBar.BackColor = Color.Black;
            navBar.BorderStyle = BorderStyle.Fixed3D;
            navBar.Controls.Add(button1);
            navBar.Dock = DockStyle.Top;
            navBar.Location = new Point(0, 0);
            navBar.Name = "navBar";
            navBar.Size = new Size(175, 35);
            navBar.TabIndex = 0;
            navBar.Paint += navBar_Paint;
            navBar.MouseDown += navBar_MouseDown;
            navBar.MouseMove += navBar_MouseMove;
            navBar.MouseUp += navBar_MouseUp;
            // 
            // button1
            // 
            button1.BackColor = Color.Transparent;
            button1.BackgroundImage = (Image)resources.GetObject("button1.BackgroundImage");
            button1.BackgroundImageLayout = ImageLayout.Zoom;
            button1.Dock = DockStyle.Right;
            button1.Location = new Point(135, 0);
            button1.Name = "button1";
            button1.Size = new Size(36, 31);
            button1.TabIndex = 0;
            button1.UseVisualStyleBackColor = false;
            button1.Click += button1_Click;
            // 
            // buttonPanel
            // 
            buttonPanel.BackColor = Color.Black;
            buttonPanel.BorderStyle = BorderStyle.Fixed3D;
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Location = new Point(0, 35);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new Size(175, 415);
            buttonPanel.TabIndex = 1;
            // 
            // switcherForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            ClientSize = new Size(175, 450);
            ControlBox = false;
            Controls.Add(buttonPanel);
            Controls.Add(navBar);
            FormBorderStyle = FormBorderStyle.None;
            Name = "switcherForm";
            StartPosition = FormStartPosition.Manual;
            Text = "Switcher";
            TransparencyKey = Color.Lime;
            Load += switcherForm_Load;
            navBar.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel navBar;
        private Panel buttonPanel;
        private Button button1;
    }
}