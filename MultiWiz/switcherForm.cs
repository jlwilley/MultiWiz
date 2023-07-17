using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static MultiWiz.Form1;

namespace MultiWiz
{
    public partial class switcherForm : Form
    {
        //Imports required functionality for changing foreground window
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        //the form that created this one
        Form1 ParentForm;
        ArrayList buttonList = new ArrayList();
        //the switcher form object
        public switcherForm(Form1 parentForm)
        {
            //ensures form is always on top
            this.TopMost = true;
            //positions switcher at rightmost point on the screen
            Point p = new Point(Screen.PrimaryScreen.Bounds.Width - (this.Width * 3 / 4), (Screen.PrimaryScreen.Bounds.Height / 2) - this.Height);
            this.Location = p;
            this.Opacity = .5;
            InitializeComponent();
            this.ParentForm = parentForm;
            

            //adds a button to the form for each account currently logged in
            foreach (account a in parentForm.getAccount())
            {
                if (a.process != null)
                {
                    Button button = new Button();
                    button.Name = a.name;
                    button.Text = a.name;
                    button.Tag = a;
                    button.Dock = DockStyle.Top;
                    button.Height = 50;
                    Font buttonFont = new Font(DefaultFont.Name, 20, FontStyle.Bold);
                    button.Font = buttonFont;
                    button.ForeColor = Color.White;
                    button.Click += OnGenericButtonClick;
                    buttonList.Add(button);
                    buttonPanel.Controls.Add(button);                
                }
            }
        }

        //generic method for handling switcher buttons
        void OnGenericButtonClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                foreach (Button b in buttonList)
                {
                    b.BackColor = Color.Black;
                }
                btn.BackColor = Color.SlateGray;
                account a = (account)btn.Tag;
                SetForegroundWindow(a.process.MainWindowHandle);
                
            }
        }

        private void switcherForm_Load(object sender, EventArgs e)
        {

        }

        //ensures main form is shown when this one closes
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ParentForm.Show();
            base.OnFormClosing(e);
        }

        //close button
        private void button1_Click(object sender, EventArgs e)
        {
            ParentForm.Show();
            this.Close();
        }

        private void navBar_Paint(object sender, PaintEventArgs e)
        {

        }

        //controls for dragging the form around
        private bool mouseDown;
        private Point lastLocation;
        private void navBar_MouseDown(object sender, MouseEventArgs e)
        {
            mouseDown = true;
            lastLocation = e.Location;
        }

        private void navBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseDown)
            {
                this.Location = new Point(
                    (this.Location.X - lastLocation.X) + e.X, (this.Location.Y - lastLocation.Y) + e.Y);

                this.Update();
            }
        }

        private void navBar_MouseUp(object sender, MouseEventArgs e)
        {
            mouseDown = false;
        }
    }
}
