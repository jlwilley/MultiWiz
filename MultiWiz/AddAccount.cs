using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MultiWiz
{
    public partial class AddAccount : Form
    {
        private Form1 parentForm;
        public AddAccount(Form1 parnetForm)
        {
            InitializeComponent();
            this.parentForm = parnetForm;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(accountText.Text))
            {
                ErrorLabel.Text = "Invalid Account Name";
                return;
            }
            else if (string.IsNullOrWhiteSpace(usernameText.Text))
            {
                ErrorLabel.Text = "Invalid User Name";
                return;
            }
            else if (string.IsNullOrWhiteSpace(passwordText.Text))
            {
                ErrorLabel.Text = "Invalid Password";
                return;
            }
            parentForm.addAccount(new Form1.account(accountText.Text, usernameText.Text, passwordText.Text));
            Close();
        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
