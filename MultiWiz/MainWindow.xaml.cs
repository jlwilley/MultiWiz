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
using Squirrel;
using Squirrel.Sources;


namespace MultiWiz
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // Method to raise the PropertyChanged event
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task UpdateMyApp()
        {
            
            String Token = "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";
            using var mgr = new UpdateManager(new GithubSource("https://github.com/jlwilley/MultiWiz", Token, false));
            if (mgr.IsInstalledApp)
            {
                var newVersion = await mgr.UpdateApp();
                
                // optionally restart the app automatically, or ask the user if/when they want to restart
                if (newVersion != null)
                {
                    var result = await UpdateDialogHost.ShowDialog(UpdateDialogHost.Content);
                    if ((bool)result)
                    {
                        UpdateManager.RestartApp();
                    }


                }
            }
        }

        private static void OnAppInstall(SemanticVersion version, IAppTools tools)
        {
            tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
        }

        private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
        {
            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
        }

        private async void OnAppRun(SemanticVersion version, IAppTools tools, bool firstRun)
        {
            tools.SetProcessAppUserModelId();
            // show a welcome message when the app is first installed
            if (firstRun) MessageBox.Show("MultiWiz Successfully Installed");
            await UpdateMyApp();
        }

        string path = ".\\config.txt";
        private string settingsPath = ".\\settings.txt";

        public ObservableCollection<account> Accounts;

        private static readonly object loginLock = new object();


        // Import the necessary functions from user32.dll
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public MainWindow()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string multiWizPath = System.IO.Path.Combine(appDataPath, "MultiWiz");
            this.DataContext = this;

            // Ensure the MultiWiz directory exists
            Directory.CreateDirectory(multiWizPath);

        // Set the path for config.txt within the MultiWiz directory
        string configPath = System.IO.Path.Combine(multiWizPath, "config.txt");
            this.path = configPath;
            this.settingsPath = System.IO.Path.Combine(multiWizPath, "settings.txt");
            InitializeComponent();
            SquirrelAwareApp.HandleEvents(
    onInitialInstall: OnAppInstall,
    onAppUninstall: OnAppUninstall,
    onEveryRun: OnAppRun);
            Accounts = new ObservableCollection<account>();
            loadInformation();
            loadSettings();
            AccountView.ItemsSource = Accounts;

        }

        protected override void OnClosing( CancelEventArgs e)
        {
            saveInformation();
            closeAllAccounts();
            saveSettings();
            base.OnClosing(e);
        }

        public void closeAllAccounts()
        {
              foreach(account a in Accounts)
            {
                a.StopWizard();
            }
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
            try
            {
                if (File.Exists(settingsPath))
                {
                    var settingsLines = File.ReadAllLines(settingsPath);
                    foreach (var line in settingsLines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length != 2) continue;

                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        if (key == "IsDarkModeEnabled")
                        {
                            isDarkModeEnabled = bool.Parse(value);
                            ApplyTheme();
                        }
                        else if (key == "Wait")
                        {
                            if (int.TryParse(value, out int waitSeconds))
                            {
                                Wait = waitSeconds;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        //method for saving settings to file, such as dark mode, etc.
        private void saveSettings()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(settingsPath, append: false))
                {
                    writer.WriteLine($"IsDarkModeEnabled={IsDarkModeEnabled}");
                    writer.WriteLine($"Wait={Wait}"); // Save Wait in seconds
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }



        private void ApplyTheme()
        {
            var paletteHelper = new PaletteHelper();
            ITheme theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDarkModeEnabled ? Theme.Dark : Theme.Light);
            paletteHelper.SetTheme(theme);
        }

        private bool isDarkModeEnabled;
        public bool IsDarkModeEnabled
        {
            get => isDarkModeEnabled;
            set
            {
                if (isDarkModeEnabled != value)
                {
                    isDarkModeEnabled = value;
                    OnPropertyChanged(nameof(IsDarkModeEnabled)); // Notify the UI of the change
                }
            }
        }

        private int waitInSeconds = 6; // Default value is 6 seconds
        public int Wait
        {
            get => waitInSeconds;
            set
            {
                if (waitInSeconds != value)
                {
                    waitInSeconds = value;
                    OnPropertyChanged(nameof(Wait)); // Notify UI of change
                }
            }
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
            public void StartWizard(int Wait)
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
                Thread loginThread = new Thread(() => login(Wait));
                loginThread.Start();
                
            }

            public void login(int Wait)
            {
                //waits 5 seconds to ensure game loads
                Thread.Sleep(Wait * 1000);
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
                if(Process != null)
                {
                    Process.WaitForExit();
                }
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
                    try
                    {
                        string line = "";
                        while ((line = sr.ReadLine()) != null)
                        {
                            string[] info = line.Split(',');
                            Accounts.Add(new account(info[0], info[1], info[2]));
                        }
                    } catch
                    {
                        Console.WriteLine("Config file is malformed");
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsDialogHost.DialogContent != null)
            {
                SettingsDialogHost.ShowDialog(SettingsDialogHost.DialogContent);
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Apply the theme immediately to reflect any dark mode changes
            ApplyTheme();

            // Save the updated settings to the settings file
            saveSettings();
        }

        private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Reload the settings from the file to revert any changes
            loadSettings();
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
                a.StartWizard(waitInSeconds);
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
