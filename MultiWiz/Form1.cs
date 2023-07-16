using System.Collections;
using System.Diagnostics;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
using System.Net;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;

namespace MultiWiz
{
    public partial class Form1 : Form
    {

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private ArrayList accountList;
        string path = "..\\config.txt";
        public Form1()
        {
            InitializeComponent();
            accountList = new ArrayList();
            loadInformation();
        }

        public ArrayList getAccount()
        {
            return accountList;
        }

        private void loadInformation()
        {
            try
            {

                using (StreamReader sr = File.OpenText(path))
                {
                    string line = "";
                    string username = "";
                    string name = "";
                    string password = "";
                    while ((line = sr.ReadLine()) != null)
                    {
                        name = line.Trim();
                        if ((line = sr.ReadLine()) == null)
                        {
                            throw new Exception();
                        }
                        else
                        {
                            username = line.Trim();
                        }
                        if ((line = sr.ReadLine()) == null)
                        {
                            throw new Exception();
                        }
                        else
                        {
                            password = line.Trim();
                        }
                        accountList.Add(new account(name, username, password));
                    }
                }
                refresh();
            }
            catch
            {
                Console.WriteLine("Config file not found");
            }
        }

        public void addAccount(account a)
        {
            accountList.Add(a);
            refresh();
            saveInformation();
        }

        private void saveInformation()
        {
            using (StreamWriter sw = File.CreateText(path))
            {
                foreach (account a in accountList)
                {
                    sw.WriteLine(a.name);
                    sw.WriteLine(a.username);
                    sw.WriteLine(a.password);
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            refresh();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Form AddAccount = new AddAccount(this);
            AddAccount.ShowDialog();
        }

        private void button3_Click(object sender, EventArgs e)
        {

        }

        private void refresh()
        {
            listView1.Items.Clear();
            foreach (account a in accountList)
            {
                ListViewItem lvitem = new ListViewItem(a.name);
                lvitem.SubItems.Add(a.username);
                lvitem.Tag = a;
                if (a.process == null)
                {
                    lvitem.SubItems.Add("false");
                }
                else
                {
                    if (a.process.HasExited == true)
                    {
                        lvitem.SubItems.Add("false");
                        a.process = null;
                    }
                    else
                    {
                        lvitem.SubItems.Add("true");
                    }
                }
                listView1.Items.Add(lvitem);
            }
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            saveInformation();
            base.OnFormClosing(e);
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            refresh();
        }

        private void openButton_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listView1.SelectedItems)
            {
                Console.WriteLine(item.Text);
                Console.Write(item.SubItems[2].Text);
                if (item.SubItems[2].Text.Equals("false"))
                {
                    account a = (account)item.Tag;
                    a.startWizard();
                }


            }
            refresh();
        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listView1.SelectedItems)
            {
                account a = (account)item.Tag;
                if (a.process != null) { a.stopWizard(); }
            }
            refresh();
        }

        public class account
        {
            public string name;
            public string username;
            public string password;
            public Process? process;
            private static readonly object loginLock = new object();

            public account(string name, string username, string password)
            {
                this.name = name;
                this.username = username;
                this.password = password;
                this.process = null;
            }

            public void startWizard()
            {
                ProcessStartInfo info = new ProcessStartInfo();
                this.process = new Process();
                info.FileName = "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin\\WizardGraphicalClient.exe";
                info.WorkingDirectory = "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin";
                info.Arguments = "-L login.us.wizard101.com 12000";
                this.process.StartInfo = info;
                this.process.Start();
                Thread loginThread = new Thread(login);
                loginThread.Start();
            }

            public void login()
            {
                Thread.Sleep(5000);
                lock (loginLock)
                {
                    SetForegroundWindow(process.MainWindowHandle);
                    SendKeys.SendWait(username + "{TAB}");
                    SendKeys.SendWait(password + "~");
                    SetForegroundWindow(Process.GetProcessById(Environment.ProcessId).MainWindowHandle);
                }

            }



            public void stopWizard()
            {
                if (process != null)
                {
                    this.process.Kill();
                    process = null;
                }
            }

        }

        private void deleteButton_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listView1.SelectedItems)
            {
                account a = (account)item.Tag;
                accountList.Remove(a);
            }
            refresh();
            saveInformation();
        }

        private void switcherButton_Click(object sender, EventArgs e)
        {
            switcherForm s = new switcherForm(this);
            s.Show();
            this.Hide();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            foreach (account a in accountList)
            {
                if (a.process != null) { a.stopWizard(); }
            }
            refresh();
        }
    }
}