namespace MultiWiz
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            button1 = new Button();
            button2 = new Button();
            panel1 = new Panel();
            panel3 = new Panel();
            panel4 = new Panel();
            deleteButton = new Button();
            button3 = new Button();
            openButton = new Button();
            listView1 = new ListView();
            nameColumn = new ColumnHeader();
            usernameColumn = new ColumnHeader();
            runningColumn = new ColumnHeader();
            panel2 = new Panel();
            switcherButton = new Button();
            refreshButton = new Button();
            button4 = new Button();
            panel1.SuspendLayout();
            panel3.SuspendLayout();
            panel4.SuspendLayout();
            panel2.SuspendLayout();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Dock = DockStyle.Left;
            button1.Location = new Point(0, 0);
            button1.Name = "button1";
            button1.Size = new Size(139, 57);
            button1.TabIndex = 0;
            button1.Text = "Update Processes";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // button2
            // 
            button2.Dock = DockStyle.Left;
            button2.Location = new Point(139, 0);
            button2.Name = "button2";
            button2.Size = new Size(136, 57);
            button2.TabIndex = 1;
            button2.Text = "Add Account";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // panel1
            // 
            panel1.Controls.Add(panel3);
            panel1.Controls.Add(panel2);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(800, 450);
            panel1.TabIndex = 4;
            panel1.Paint += panel1_Paint;
            // 
            // panel3
            // 
            panel3.Controls.Add(panel4);
            panel3.Controls.Add(listView1);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(0, 57);
            panel3.Name = "panel3";
            panel3.Size = new Size(800, 393);
            panel3.TabIndex = 4;
            // 
            // panel4
            // 
            panel4.Controls.Add(deleteButton);
            panel4.Controls.Add(button3);
            panel4.Controls.Add(openButton);
            panel4.Dock = DockStyle.Bottom;
            panel4.Location = new Point(0, 293);
            panel4.Name = "panel4";
            panel4.Size = new Size(800, 100);
            panel4.TabIndex = 1;
            // 
            // deleteButton
            // 
            deleteButton.Location = new Point(257, 26);
            deleteButton.Name = "deleteButton";
            deleteButton.Size = new Size(96, 44);
            deleteButton.TabIndex = 2;
            deleteButton.Text = "Delete";
            deleteButton.UseVisualStyleBackColor = true;
            deleteButton.Click += deleteButton_Click;
            // 
            // button3
            // 
            button3.Location = new Point(145, 26);
            button3.Name = "button3";
            button3.Size = new Size(106, 44);
            button3.TabIndex = 1;
            button3.Text = "Close";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click_1;
            // 
            // openButton
            // 
            openButton.Location = new Point(33, 26);
            openButton.Name = "openButton";
            openButton.Size = new Size(106, 44);
            openButton.TabIndex = 0;
            openButton.Text = "Open";
            openButton.UseVisualStyleBackColor = true;
            openButton.Click += openButton_Click;
            // 
            // listView1
            // 
            listView1.Columns.AddRange(new ColumnHeader[] { nameColumn, usernameColumn, runningColumn });
            listView1.Dock = DockStyle.Fill;
            listView1.FullRowSelect = true;
            listView1.GridLines = true;
            listView1.Location = new Point(0, 0);
            listView1.Name = "listView1";
            listView1.Size = new Size(800, 393);
            listView1.TabIndex = 0;
            listView1.UseCompatibleStateImageBehavior = false;
            listView1.View = View.Details;
            // 
            // nameColumn
            // 
            nameColumn.Text = "Account Name";
            nameColumn.Width = 150;
            // 
            // usernameColumn
            // 
            usernameColumn.Text = "Username";
            usernameColumn.Width = 150;
            // 
            // runningColumn
            // 
            runningColumn.Text = "Running";
            runningColumn.Width = 120;
            // 
            // panel2
            // 
            panel2.Controls.Add(button4);
            panel2.Controls.Add(switcherButton);
            panel2.Controls.Add(refreshButton);
            panel2.Controls.Add(button2);
            panel2.Controls.Add(button1);
            panel2.Dock = DockStyle.Top;
            panel2.Location = new Point(0, 0);
            panel2.Name = "panel2";
            panel2.Size = new Size(800, 57);
            panel2.TabIndex = 0;
            // 
            // switcherButton
            // 
            switcherButton.Dock = DockStyle.Left;
            switcherButton.Location = new Point(397, 0);
            switcherButton.Name = "switcherButton";
            switcherButton.Size = new Size(113, 57);
            switcherButton.TabIndex = 3;
            switcherButton.Text = "Switcher";
            switcherButton.UseVisualStyleBackColor = true;
            switcherButton.Click += switcherButton_Click;
            // 
            // refreshButton
            // 
            refreshButton.Dock = DockStyle.Left;
            refreshButton.Location = new Point(275, 0);
            refreshButton.Name = "refreshButton";
            refreshButton.Size = new Size(122, 57);
            refreshButton.TabIndex = 2;
            refreshButton.Text = "Refresh";
            refreshButton.UseVisualStyleBackColor = true;
            refreshButton.Click += refreshButton_Click;
            // 
            // button4
            // 
            button4.Dock = DockStyle.Left;
            button4.Location = new Point(510, 0);
            button4.Name = "button4";
            button4.Size = new Size(100, 57);
            button4.TabIndex = 4;
            button4.Text = "Close All";
            button4.UseVisualStyleBackColor = true;
            button4.Click += button4_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(panel1);
            Name = "Form1";
            Text = "Form1";
            panel1.ResumeLayout(false);
            panel3.ResumeLayout(false);
            panel4.ResumeLayout(false);
            panel2.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private Button button2;
        private Panel panel1;
        private Panel panel2;
        private Panel panel3;
        private ListView listView1;
        private Panel panel4;
        private Button openButton;
        private ColumnHeader nameColumn;
        private ColumnHeader usernameColumn;
        private Button button3;
        private ColumnHeader runningColumn;
        private Button refreshButton;
        private Button deleteButton;
        private Button switcherButton;
        private Button button4;
    }
}