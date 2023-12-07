using MaterialDesignThemes.Wpf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static MultiWiz.MainWindow;
using InputSimulatorEx;
using System.Runtime.CompilerServices;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Xml.Linq;



namespace MultiWiz
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public async Task<bool> IsUpdateAvailable()
        {
            string currentVersion = "1.0.0"; // Your current app version
            string repo = "jlwilley/MultiWiz"; // Your GitHub repo
            String Token = "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";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppName", "1.0"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                var response = await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");
                JObject latestRelease = JObject.Parse(response);
                string latestVersion = latestRelease["tag_name"].ToString(); // Assuming you use tags for versioning

                return Version.Parse(latestVersion) > Version.Parse(currentVersion);
            }
        }

        string path = ".\\config.txt";

        public ObservableCollection<account> Accounts;

        private static readonly object loginLock = new object();



        // Import the necessary functions from user32.dll
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public MainWindow()
        {
            InitializeComponent();
            Accounts = new ObservableCollection<account>();
            loadInformation();
            AccountView.ItemsSource = Accounts;
        }

        protected override void OnClosing( CancelEventArgs e)
        {
            saveInformation();
            base.OnClosing(e);
        }

        public void addAccount(account a)
        {
            Accounts.Add(a);
            saveInformation();
        }

        public void deleteAccount(account a)
        {
            Accounts.Remove(a);
            saveInformation();
        }

        //method for loading settings from file, such as dark mode, etc.
        private void loadSettings()
        {
           
        }

        //method for saving settings to file, such as dark mode, etc.
        private void saveSettings()
        {

        }

        //method for changing to dark mode
        private void darkMode()
        {

        }
       

         
        //account class
        public class account : INotifyPropertyChanged
        {

            public event PropertyChangedEventHandler PropertyChanged;

            private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public string Name { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public Process? Process { get; set; }

            private bool isRunning;
            public bool IsRunning
            {
                get { return isRunning; }
                set
                {
                    if (isRunning != value)
                    {
                        isRunning = value;
                        NotifyPropertyChanged();
                    }
                }
            }


            public account(string name, string username, string password)
            {
                Name = name;
                Username = username;
                Password = password;
                Process = null;
                IsRunning = false;
            }

            //starts wizard 101 for assocaited account
            public void StartWizard()
            {
                ProcessStartInfo info = new ProcessStartInfo();
                Process = new Process();
                info.FileName = "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin\\WizardGraphicalClient.exe";
                info.WorkingDirectory = "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin";
                info.Arguments = "-L login.us.wizard101.com 12000";
                Process.StartInfo = info;
                Process.Start();
                IsRunning = true;
                //uses a new thread
                Thread loginThread = new Thread(login);
                loginThread.Start();
                
            }

            public void login()
            {
                //waits 5 seconds to ensure game loads
                Thread.Sleep(5000);
                //locks to ensure logins are correctly entererd individually
                lock (loginLock)
                {
                    if (Process != null)
                    {
                        SetForegroundWindow(Process.MainWindowHandle);
                        var simulator = new InputSimulator();
                        simulator.Keyboard.TextEntry(Username);
                        simulator.Keyboard.KeyPress(InputSimulatorEx.Native.VirtualKeyCode.TAB);
                        simulator.Keyboard.TextEntry(Password);
                        simulator.Keyboard.KeyPress(InputSimulatorEx.Native.VirtualKeyCode.RETURN);
                        SetForegroundWindow(Process.GetProcessById(Environment.ProcessId).MainWindowHandle);
                    }
                }
                Process.WaitForExit();
                IsRunning = false;
                Process = null;
            }


            //stops the process with the current account
            public void StopWizard()
            {
                if (Process != null)
                {
                    this.Process.Kill();
                    Process = null;
                    IsRunning = false;
                }
            }

            public void Focus() {                 
                if (Process != null)
                {
                    SetForegroundWindow(Process.MainWindowHandle);
                }
            }
        }

        private void loadInformation()
        {
            try
            {
                using (StreamReader sr = File.OpenText(path))
                {
                    string line = "";
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] info = line.Split(',');
                        Accounts.Add(new account(info[0], info[1], info[2]));
                    }
                }               
            }
            catch
            {
                Console.WriteLine("Config file not found");
            }
        }

        private void saveInformation()
        {
            using (StreamWriter sw = File.CreateText(path))
            {
                foreach (account a in Accounts)
                {
                    sw.WriteLine(a.Name + "," + a.Username + "," + a.Password);
                }
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddDialog.DialogContent != null)
            {            
                AddDialog.ShowDialog(AddDialog.DialogContent);
            }
        }

        private void DialogAddButton_Click(object sender, RoutedEventArgs e)
        {
            account a = new account(AccountNameTextBox.Text, UsernameTextBox.Text, PasswordTextBox.Text);
            addAccount(a);
            CloseAddAccountDialog();
        }

        private void DialogCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseAddAccountDialog();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            ArrayList accounts = new ArrayList();
            foreach(account a in AccountView.SelectedItems)
            {
                accounts.Add(a);
            }
            foreach (account a in accounts)
            {
                Accounts.Remove(a);
            }
        }

        private void CloseAddAccountDialog()
        {
            AddDialog.IsOpen = false;
            AccountNameTextBox.Text = "";
            UsernameTextBox.Text = "";
            PasswordTextBox.Text = "";
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {           
            foreach(account a in AccountView.SelectedItems)
            {
                a.StartWizard();
            }
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
           Switcher switcher = new Switcher(this);
            switcher.Show();
            this.Hide();
        }
    }
}
