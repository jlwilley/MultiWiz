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
using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Velopack;
using Velopack.Sources;
using NAudio.CoreAudioApi;
using System.Security.Cryptography;


namespace MultiWiz
{
    /// <summary>
    /// Game server options for KingsIsle games
    /// </summary>
    public enum GameServer
    {
        Wizard101_US,
        Wizard101_EU,
        Pirate101_US
    }

    /// <summary>
    /// Helper class for GameServer enum
    /// </summary>
    public static class GameServerExtensions
    {
        public static string GetLaunchArguments(this GameServer server)
        {
            return server switch
            {
                GameServer.Wizard101_US => "-L login.us.wizard101.com 12000",
                GameServer.Wizard101_EU => "-L login.eu.wizard101.com 12000",
                GameServer.Pirate101_US => "-L login.us.pirate101.com 12000",
                _ => "-L login.us.wizard101.com 12000"
            };
        }

        public static string GetDisplayName(this GameServer server)
        {
            return server switch
            {
                GameServer.Wizard101_US => "Wizard101 US",
                GameServer.Wizard101_EU => "Wizard101 EU",
                GameServer.Pirate101_US => "Pirate101 US",
                _ => "Wizard101 US"
            };
        }

        public static string GetExecutablePath(this GameServer server)
        {
            return server switch
            {
                GameServer.Pirate101_US => "C:\\ProgramData\\KingsIsle Entertainment\\Pirate101\\Bin\\PirateGraphicalClient.exe",
                _ => "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin\\WizardGraphicalClient.exe"
            };
        }

        public static string GetWorkingDirectory(this GameServer server)
        {
            return server switch
            {
                GameServer.Pirate101_US => "C:\\ProgramData\\KingsIsle Entertainment\\Pirate101\\Bin",
                _ => "C:\\ProgramData\\KingsIsle Entertainment\\Wizard101\\Bin"
            };
        }
    }

    /// <summary>
    /// Value converter for GameServer enum to display name
    /// </summary>
    public class GameServerToDisplayNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is GameServer server)
            {
                return server.GetDisplayName();
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for Boolean to Visibility
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

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
            try
            {
                // Public repository - no authentication token needed
                // Users can download updates directly from GitHub releases
                var mgr = new UpdateManager(new GithubSource("https://github.com/jlwilley/MultiWiz", null, false));

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion != null)
                {
                    await mgr.DownloadUpdatesAsync(newVersion);

                    var result = await UpdateDialogHost.ShowDialog(UpdateDialogHost.Content);
                    if ((bool)result)
                    {
                        mgr.ApplyUpdatesAndRestart(newVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle update errors silently or log them
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }


        string path = ".\\config.txt";
        private string settingsPath = ".\\settings.txt";

        public ObservableCollection<account> Accounts;
        private account? _editingAccount = null;

        // Windows API constants for messaging
        private const uint WM_CHAR = 0x0102;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const int VK_TAB = 0x09;
        private const int VK_RETURN = 0x0D;

        // Import the necessary functions from user32.dll
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        /// <summary>
        /// Encrypt a string using Windows DPAPI (Data Protection API)
        /// </summary>
        private static string EncryptString(string plainText)
        {
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Encryption failed: {ex.Message}");
                return plainText; // Fallback to plain text if encryption fails
            }
        }

        /// <summary>
        /// Decrypt a string using Windows DPAPI (Data Protection API)
        /// </summary>
        private static string DecryptString(string encryptedText)
        {
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // If decryption fails, assume it's plain text (for backward compatibility)
                return encryptedText;
            }
        }

        /// <summary>
        /// Check if a string is encrypted (Base64 format check)
        /// </summary>
        private static bool IsEncrypted(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            try
            {
                // Try to decode as Base64
                byte[] data = Convert.FromBase64String(text);
                // Additional check: encrypted data should be longer than original for short strings
                return data.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Send text to a window without bringing it to foreground
        /// </summary>
        private static void SendTextToWindow(IntPtr hWnd, string text)
        {
            foreach (char c in text)
            {
                PostMessage(hWnd, WM_CHAR, (IntPtr)c, IntPtr.Zero);
                Thread.Sleep(5); // Small delay between characters
            }
        }

        /// <summary>
        /// Send a control key press (Tab, Enter, etc.) to a window.
        /// Builds correct lParam (scan code and state bits) for WM_KEYDOWN/WM_KEYUP.
        /// </summary>
        private static void SendKeyToWindow(IntPtr hWnd, int virtualKeyCode)
        {
            uint scanCode = MapVirtualKey((uint)virtualKeyCode, 0);
            int lParamDown = 1 | ((int)scanCode << 16); // repeat count = 1, scan code
            int lParamUp = lParamDown | (1 << 30) | (1 << 31); // previous state + transition

            PostMessage(hWnd, WM_KEYDOWN, (IntPtr)virtualKeyCode, new IntPtr(lParamDown));
            Thread.Sleep(50);

            PostMessage(hWnd, WM_KEYUP, (IntPtr)virtualKeyCode, new IntPtr(lParamUp));
            Thread.Sleep(50);
        }

        private void RefocusMainWindow()
        {
            if (!IsVisible)
            {
                return;
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
            }
        }

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

            VelopackApp.Build()
                .WithFirstRun(v => MessageBox.Show("MultiWiz Successfully Installed"))
                .Run();

            // Check for updates on startup
            _ = UpdateMyApp();

            Accounts = new ObservableCollection<account>();
            loadInformation();
            loadSettings();
            AccountView.ItemsSource = Accounts;

        }

        protected override void OnClosing( CancelEventArgs e)
        {
            saveInformation();
            closeAllAccounts();
            RestoreAllMutedVolumesAsync(runInBackground: false).GetAwaiter().GetResult();

            // Clean up all cached audio sessions
            lock (_audioSessionLock)
            {
                foreach (var session in _cachedAudioSessions.Values)
                {
                    try
                    {
                        session?.Dispose();
                    }
                    catch { }
                }
                _cachedAudioSessions.Clear();
            }

            // Dispose debounce timer
            _focusDebounceTimer?.Dispose();

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
                        else if (key == "_muteWhenNotInFocus")
                        {
                            _muteWhenNotInFocus = bool.Parse(value);
                        }
                        else if (key == "_unmuteVolume")
                        {
                            if (uint.TryParse(value, out uint volume))
                            {
                                UnmuteVolume = volume;
                            }
                        }
                        else if (key == "_switcherOpacity")
                        {
                            if (double.TryParse(value, out double opacity))
                            {
                                SwitcherOpacity = opacity;
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
                    writer.WriteLine($"Wait={Wait}");
                    writer.WriteLine($"_muteWhenNotInFocus={_muteWhenNotInFocus}");
                    writer.WriteLine($"_unmuteVolume={_unmuteVolume}");
                    writer.WriteLine($"_switcherOpacity={_switcherOpacity}");
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
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDarkModeEnabled ? BaseTheme.Dark : BaseTheme.Light);
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

        private bool _muteWhenNotInFocus = true;
        public bool MuteWhenNotInFocus
        {
            get => _muteWhenNotInFocus;
            set
            {
                if (_muteWhenNotInFocus != value)
                {
                    _muteWhenNotInFocus = value;
                    OnPropertyChanged(nameof(MuteWhenNotInFocus));

                    if (!_muteWhenNotInFocus)
                    {
                        _ = RestoreAllMutedVolumesAsync();
                    }
                }
            }
        }

        private uint _unmuteVolume = 100; // Maximum volume
        public uint UnmuteVolume
        {
            get => _unmuteVolume;
            set
            {
                var clampedValue = Math.Min(value, 100u);
                if (_unmuteVolume != clampedValue)
                {
                    _unmuteVolume = clampedValue;
                    OnPropertyChanged(nameof(UnmuteVolume));
                }
            }
        }

        private double _switcherOpacity = 0.98; // Default 98% opacity
        public double SwitcherOpacity
        {
            get => _switcherOpacity;
            set
            {
                // Ensure opacity is between 0.1 (10%) and 1.0 (100%)
                var clampedValue = Math.Max(0.1, Math.Min(1.0, value));
                if (_switcherOpacity != clampedValue)
                {
                    _switcherOpacity = clampedValue;
                    OnPropertyChanged(nameof(SwitcherOpacity));
                }
            }
        }

        private readonly object _audioSessionLock = new();
        private readonly Dictionary<int, float> _mutedProcessVolumes = new();
        private readonly Dictionary<int, AudioSessionControl> _cachedAudioSessions = new();

        // Debouncing for rapid window switches
        private System.Threading.Timer _focusDebounceTimer;
        private account _pendingFocusAccount;
        private readonly object _focusLock = new();
        private const int FOCUS_DEBOUNCE_MS = 150;

        /// <summary>
        /// Cache the audio session for a process. Call this once when a process starts.
        /// </summary>
        public void CacheAudioSessionForProcess(int processId)
        {
            lock (_audioSessionLock)
            {
                try
                {
                    // If already cached, don't cache again
                    if (_cachedAudioSessions.ContainsKey(processId))
                    {
                        return;
                    }

                    using var enumerator = new MMDeviceEnumerator();
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    {
                        using (device)
                        {
                            var sessionCollection = device.AudioSessionManager.Sessions;
                            for (int i = 0; i < sessionCollection.Count; i++)
                            {
                                var session = sessionCollection[i];
                                if (session?.GetProcessID == processId)
                                {
                                    // Cache the session (do NOT dispose it - we're keeping it)
                                    _cachedAudioSessions[processId] = session;
                                    Debug.WriteLine($"Cached audio session for process {processId}");
                                    return;
                                }
                            }
                        }
                    }

                    Debug.WriteLine($"No audio session found for process {processId}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to cache audio session for process {processId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Remove and dispose the cached audio session for a process
        /// </summary>
        public void RemoveCachedAudioSession(int processId)
        {
            lock (_audioSessionLock)
            {
                if (_cachedAudioSessions.TryGetValue(processId, out var session))
                {
                    try
                    {
                        session?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing audio session for process {processId}: {ex.Message}");
                    }
                    _cachedAudioSessions.Remove(processId);
                    Debug.WriteLine($"Removed cached audio session for process {processId}");
                }
            }
        }

        /// <summary>
        /// Get cached audio session, or null if not available
        /// </summary>
        private AudioSessionControl GetCachedAudioSession(int processId)
        {
            if (_cachedAudioSessions.TryGetValue(processId, out var session))
            {
                try
                {
                    // Verify session is still valid by checking if we can access its properties
                    _ = session.GetProcessID;
                    return session;
                }
                catch
                {
                    // Session is no longer valid, remove it
                    _cachedAudioSessions.Remove(processId);
                    Debug.WriteLine($"Cached audio session for process {processId} is no longer valid");
                }
            }
            return null;
        }

        private void MuteApplication(Process process)
        {
            if (process == null || !MuteWhenNotInFocus)
            {
                return;
            }

            Debug.WriteLine($"Muting {process.Id}");
            _ = QueueVolumeAdjustmentAsync(() => MuteProcessInternal(process.Id));
        }

        // Method to unmute the application
        private void UnmuteApplication(Process process)
        {
            if (process == null)
            {
                return;
            }

            Debug.WriteLine($"Unmuting {process.Id}");
            _ = RestoreVolumeForProcessAsync(process.Id);
        }

        /// <summary>
        /// Batched audio operation: unmute focused account and mute all others in a single pass
        /// This is MUCH more efficient than individual operations
        /// </summary>
        private void MuteOtherAccounts(account focusedAccount)
        {
            if (!MuteWhenNotInFocus)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                lock (_audioSessionLock)
                {
                    try
                    {
                        foreach (var account in Accounts)
                        {
                            if (account.Process == null || account.Process.HasExited)
                            {
                                continue;
                            }

                            int processId = account.Process.Id;

                            if (account == focusedAccount)
                            {
                                // Restore volume for focused account
                                RestoreVolumeForProcessInternal(processId, UnmuteVolume);
                            }
                            else
                            {
                                // Mute other accounts
                                MuteProcessInternal(processId);
                            }
                        }
                        Debug.WriteLine($"Batched audio adjustment completed for {Accounts.Count} accounts");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Batched audio adjustment failed: {ex.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// Debounced focus method to prevent rapid switches from stacking audio operations
        /// </summary>
        public void DebouncedFocusAccount(account acc)
        {
            lock (_focusLock)
            {
                _pendingFocusAccount = acc;

                // Cancel existing timer if any
                _focusDebounceTimer?.Dispose();

                // Create new timer that will execute after debounce delay
                _focusDebounceTimer = new System.Threading.Timer(
                    callback: (state) =>
                    {
                        lock (_focusLock)
                        {
                            if (_pendingFocusAccount != null)
                            {
                                var accountToFocus = _pendingFocusAccount;
                                _pendingFocusAccount = null;

                                // Execute the actual focus operation
                                ExecuteFocusInternal(accountToFocus);
                            }
                        }
                    },
                    state: null,
                    dueTime: FOCUS_DEBOUNCE_MS,
                    period: Timeout.Infinite
                );
            }
        }

        /// <summary>
        /// Internal focus execution (called after debounce delay)
        /// </summary>
        private void ExecuteFocusInternal(account acc)
        {
            if (acc.Process != null && !acc.Process.HasExited)
            {
                try
                {
                    SetForegroundWindow(acc.Process.MainWindowHandle);
                    MuteOtherAccounts(acc); // This now handles both unmute and mute in one batched operation
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Focus operation failed for {acc.Name}: {ex.Message}");
                }
            }
        }

        private Task RestoreVolumeForProcessAsync(int processId, bool runInBackground = true)
        {
            return QueueVolumeAdjustmentAsync(() => RestoreVolumeForProcessInternal(processId, UnmuteVolume), runInBackground);
        }

        private Task RestoreAllMutedVolumesAsync(bool runInBackground = true)
        {
            return QueueVolumeAdjustmentAsync(RestoreAllMutedVolumesInternal, runInBackground);
        }

        private Task QueueVolumeAdjustmentAsync(Action adjustment, bool runInBackground = true)
        {
            if (runInBackground)
            {
                return Task.Run(() => ExecuteAudioAdjustment(adjustment));
            }

            ExecuteAudioAdjustment(adjustment);
            return Task.CompletedTask;
        }

        private void ExecuteAudioAdjustment(Action adjustment)
        {
            lock (_audioSessionLock)
            {
                try
                {
                    adjustment();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Audio adjustment failed: {ex.Message}");
                }
            }
        }

        private void MuteProcessInternal(int processId)
        {
            if (!MuteWhenNotInFocus)
            {
                return;
            }

            AdjustProcessVolume(processId, 0f, rememberCurrentVolume: true);
        }

        private void RestoreVolumeForProcessInternal(int processId, float? fallbackPercent = null)
        {
            if (_mutedProcessVolumes.TryGetValue(processId, out var cachedVolume))
            {
                AdjustProcessVolume(processId, cachedVolume * 100f, rememberCurrentVolume: false);
                _mutedProcessVolumes.Remove(processId);
            }
            else if (fallbackPercent.HasValue)
            {
                AdjustProcessVolume(processId, fallbackPercent.Value, rememberCurrentVolume: false);
            }
        }

        private void RestoreAllMutedVolumesInternal()
        {
            var processIds = _mutedProcessVolumes.Keys.ToList();
            foreach (var processId in processIds)
            {
                RestoreVolumeForProcessInternal(processId);
            }
        }

        private bool AdjustProcessVolume(int processId, float targetVolumePercent, bool rememberCurrentVolume)
        {
            // Try to use cached session first (much faster!)
            var cachedSession = GetCachedAudioSession(processId);
            if (cachedSession != null)
            {
                try
                {
                    using var simpleVolume = cachedSession.SimpleAudioVolume;
                    if (rememberCurrentVolume && !_mutedProcessVolumes.ContainsKey(processId))
                    {
                        _mutedProcessVolumes[processId] = simpleVolume.Volume;
                    }

                    var normalizedVolume = Math.Clamp(targetVolumePercent, 0f, 100f) / 100f;
                    simpleVolume.Volume = normalizedVolume;
                    Debug.WriteLine($"Adjusted volume for process {processId} using cached session");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to adjust volume using cached session for process {processId}: {ex.Message}");
                    // Remove invalid cached session
                    _cachedAudioSessions.Remove(processId);
                }
            }

            // Fallback: enumerate if no cached session (slower, but works for edge cases)
            Debug.WriteLine($"No cached session for process {processId}, falling back to enumeration");
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var sessionCollection = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessionCollection.Count; i++)
                    {
                        var session = sessionCollection[i];
                        if (session == null)
                        {
                            continue;
                        }

                        // Don't dispose the session in the fallback path either
                        if (session.GetProcessID != processId)
                        {
                            continue;
                        }

                        using var simpleVolume = session.SimpleAudioVolume;
                        if (rememberCurrentVolume && !_mutedProcessVolumes.ContainsKey(processId))
                        {
                            _mutedProcessVolumes[processId] = simpleVolume.Volume;
                        }

                        var normalizedVolume = Math.Clamp(targetVolumePercent, 0f, 100f) / 100f;
                        simpleVolume.Volume = normalizedVolume;

                        // Cache this session for future use
                        if (!_cachedAudioSessions.ContainsKey(processId))
                        {
                            _cachedAudioSessions[processId] = session;
                            Debug.WriteLine($"Cached audio session for process {processId} during fallback");
                        }

                        return true;
                    }
                }
            }

            return false;
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

            private MainWindow Parent { get; set;}

            private GameServer server;
            public GameServer Server
            {
                get { return server; }
                set
                {
                    if (server != value)
                    {
                        server = value;
                        NotifyPropertyChanged();
                        NotifyPropertyChanged(nameof(ServerDisplayName));
                    }
                }
            }

            public string ServerDisplayName => Server.GetDisplayName();

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
                        NotifyPropertyChanged(nameof(LaunchButtonIcon));
                        NotifyPropertyChanged(nameof(LaunchButtonTooltip));
                    }
                }
            }

            public string LaunchButtonIcon => IsRunning ? "Stop" : "Play";
            public string LaunchButtonTooltip => IsRunning ? "Stop" : "Launch";



            public account(string name, string username, string password, MainWindow parent, GameServer server = GameServer.Wizard101_US)
            {
                Name = name;
                Username = username;
                Password = password;
                Process = null;
                IsRunning = false;
                Parent = parent;
                Server = server;
            }

            //starts the game for associated account
            public void StartWizard(int Wait)
            {
                ProcessStartInfo info = new ProcessStartInfo();
                Process = new Process();
                info.FileName = Server.GetExecutablePath();
                info.WorkingDirectory = Server.GetWorkingDirectory();
                info.Arguments = Server.GetLaunchArguments();
                Process.StartInfo = info;
                Process.Start();
                IsRunning = true;
                //uses a new thread
                Thread loginThread = new Thread(() => login(Wait));
                loginThread.IsBackground = true;
                loginThread.Start();

            }

            public void login(int Wait)
            {
                //waits for game to load
                Thread.Sleep(Wait * 1000);

                // Background login - simple keyboard simulation
                if (Process != null && !Process.HasExited)
                {
                    try
                    {
                        // Wait for window to be ready
                        Thread.Sleep(1000);

                        IntPtr mainWindowHandle = Process.MainWindowHandle;
                        if (mainWindowHandle != IntPtr.Zero)
                        {
                            Debug.WriteLine($"Starting background login for {Name}");

                            // Send username characters
                            MainWindow.SendTextToWindow(mainWindowHandle, Username);
                            Thread.Sleep(100);

                            // Send TAB key
                            Debug.WriteLine($"Sending TAB for {Name}");
                            MainWindow.SendKeyToWindow(mainWindowHandle, VK_TAB);
                            Thread.Sleep(100);

                            // Send password characters
                            MainWindow.SendTextToWindow(mainWindowHandle, Password);
                            Thread.Sleep(100);

                            // Send ENTER key
                            Debug.WriteLine($"Sending ENTER for {Name}");
                            MainWindow.SendKeyToWindow(mainWindowHandle, VK_RETURN);

                            Parent.Dispatcher.BeginInvoke(new Action(Parent.RefocusMainWindow));

                            Debug.WriteLine($"Login sequence complete for {Name}");

                            // Cache audio session after login completes and window is ready
                            // Wait a bit for audio to initialize
                            Thread.Sleep(500);
                            Parent.CacheAudioSessionForProcess(Process.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background login failed for {Name}: {ex.Message}");
                    }
                }

                if(Process != null)
                {
                    var exitingProcessId = Process.Id;
                    Process.WaitForExit();

                    // Clean up audio session and restore volume
                    _ = Parent.RestoreVolumeForProcessAsync(exitingProcessId);
                    Parent.RemoveCachedAudioSession(exitingProcessId);
                }
                IsRunning = false;
                Process = null;
            }


            //stops the process with the current account
            public void StopWizard()
            {
                if (Process != null)
                {
                    var processId = Process.Id;
                    _ = Parent.RestoreVolumeForProcessAsync(processId);
                    Parent.RemoveCachedAudioSession(processId);
                    this.Process.Kill();
                    Process = null;
                    IsRunning = false;
                }
            }

            public void Focus() {
                if (Process != null && !Process.HasExited)
                {
                    // Use debounced focus to prevent audio operations from stacking
                    Parent.DebouncedFocusAccount(this);
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

                            // Decrypt username and password if encrypted
                            string username = info.Length > 1 ? DecryptString(info[1]) : "";
                            string password = info.Length > 2 ? DecryptString(info[2]) : "";

                            // Support both old format (3 fields) and new format (4 fields with server)
                            GameServer server = GameServer.Wizard101_US; // Default
                            if (info.Length >= 4 && Enum.TryParse<GameServer>(info[3], out GameServer parsedServer))
                            {
                                server = parsedServer;
                            }

                            Accounts.Add(new account(info[0], username, password, this, server));
                        }
                    } catch (Exception ex)
                    {
                        Console.WriteLine($"Config file is malformed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config file not found: {ex.Message}");
            }
        }

        private void saveInformation()
        {
            using (StreamWriter sw = File.CreateText(path))
            {
                foreach (account a in Accounts)
                {
                    // Encrypt username and password before saving
                    string encryptedUsername = EncryptString(a.Username);
                    string encryptedPassword = EncryptString(a.Password);
                    sw.WriteLine($"{a.Name},{encryptedUsername},{encryptedPassword},{a.Server}");
                }
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddDialog.DialogContent != null)
            {
                // Set default server selection
                ServerComboBox.SelectedIndex = 0; // Wizard101_US
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
            // Get selected server or default to Wizard101_US
            GameServer selectedServer = GameServer.Wizard101_US;
            if (ServerComboBox.SelectedItem != null && ServerComboBox.SelectedItem is GameServer server)
            {
                selectedServer = server;
            }

            // Get password from PasswordBox
            string password = PasswordTextBox.Password;

            account a = new account(AccountNameTextBox.Text, UsernameTextBox.Text, password, this, selectedServer);
            addAccount(a);
            CloseAddAccountDialog();
        }

        private void FocusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is account acc)
            {
                acc.Focus();
            }
        }

        private void LaunchStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is account acc)
            {
                if (acc.IsRunning)
                {
                    // Stop the account
                    acc.StopWizard();
                }
                else
                {
                    // Launch the account
                    acc.StartWizard(waitInSeconds);
                }
            }
        }

        private void EditAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is account acc)
            {
                _editingAccount = acc;

                // Populate edit dialog with current account data
                EditAccountNameTextBox.Text = acc.Name;
                EditUsernameTextBox.Text = acc.Username;
                EditPasswordTextBox.Password = acc.Password;
                EditServerComboBox.SelectedItem = acc.Server;

                // Show edit dialog
                if (EditDialog.DialogContent != null)
                {
                    EditDialog.ShowDialog(EditDialog.DialogContent);
                }
            }
        }

        private void DialogEditSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingAccount != null)
            {
                // Get selected server
                GameServer selectedServer = GameServer.Wizard101_US;
                if (EditServerComboBox.SelectedItem != null && EditServerComboBox.SelectedItem is GameServer server)
                {
                    selectedServer = server;
                }

                // Update account properties
                _editingAccount.Name = EditAccountNameTextBox.Text;
                _editingAccount.Username = EditUsernameTextBox.Text;
                _editingAccount.Password = EditPasswordTextBox.Password;
                _editingAccount.Server = selectedServer;

                // Save changes
                saveInformation();

                // Close dialog
                CloseEditDialog();
            }
        }

        private void DialogEditCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CloseEditDialog();
        }

        private void CloseEditDialog()
        {
            EditDialog.IsOpen = false;
            _editingAccount = null;
            EditAccountNameTextBox.Text = "";
            EditUsernameTextBox.Text = "";
            EditPasswordTextBox.Password = "";
            EditServerComboBox.SelectedIndex = 0;
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
            PasswordTextBox.Password = "";
            ServerComboBox.SelectedIndex = 0; // Default to first item (Wizard101_US)
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
