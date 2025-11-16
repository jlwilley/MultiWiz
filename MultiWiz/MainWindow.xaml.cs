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
        public event PropertyChangedEventHandler? PropertyChanged;

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

        public ObservableCollection<Account> Accounts;
        private Account? _editingAccount = null;

        // New services for multi-instance broadcasting and tiling
        private MultiWiz.Services.InputBroadcaster? _inputBroadcaster;
        private TilingWindow? _tilingWindow;
        private bool _isBroadcastEnabled = false;
        public bool IsBroadcastEnabled
        {
            get => _isBroadcastEnabled;
            private set
            {
                _isBroadcastEnabled = value;
                OnPropertyChanged(nameof(IsBroadcastEnabled));
            }
        }

        // Settings restoration fields
        private bool _shouldRestoreBroadcastState = false;
        private bool _shouldShowTilingWindow = false;
        private Services.TilingLayout _savedTilingLayout = Services.TilingLayout.Grid2x2;
        private double _savedTilingLeft = 100;
        private double _savedTilingTop = 100;
        private double _savedTilingWidth = 1200;
        private double _savedTilingHeight = 800;

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

            Accounts = new ObservableCollection<Account>();
            loadInformation();
            loadSettings();
            AccountView.ItemsSource = Accounts;

            // Initialize InputBroadcaster
            _inputBroadcaster = new MultiWiz.Services.InputBroadcaster(GetRunningGameWindowHandles);
            _inputBroadcaster.BroadcastStateChanged += OnBroadcastStateChanged;
            try
            {
                _inputBroadcaster.Initialize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize input broadcaster: {ex.Message}");
                MessageBox.Show($"Input broadcasting feature unavailable: {ex.Message}", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Initialize TilingWindow (but don't show it yet)
            _tilingWindow = new TilingWindow();
            _tilingWindow.SetWindowHandleProvider(
                () => GetRunningGameWindowHandles().ToList(),
                (hwnd) => FocusGameWindow(hwnd)
            );

            // Register hotkeys
            RegisterHotkeys();

            // Restore saved state
            RestoreSavedState();
        }

        /// <summary>
        /// Restores saved broadcast and tiling state from settings
        /// </summary>
        private void RestoreSavedState()
        {
            // Restore broadcast state
            if (_shouldRestoreBroadcastState && _inputBroadcaster != null)
            {
                _inputBroadcaster.EnableBroadcast();
            }

            // Restore tiling window position and layout
            if (_tilingWindow != null)
            {
                _tilingWindow.Left = _savedTilingLeft;
                _tilingWindow.Top = _savedTilingTop;
                _tilingWindow.Width = _savedTilingWidth;
                _tilingWindow.Height = _savedTilingHeight;
                _tilingWindow.SetLayout(_savedTilingLayout);

                if (_shouldShowTilingWindow)
                {
                    _tilingWindow.Show();
                }
            }
        }

        /// <summary>
        /// Registers global hotkeys for broadcast and tiling features
        /// </summary>
        private void RegisterHotkeys()
        {
            try
            {
                // Ctrl+B: Toggle broadcast mode
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("ToggleBroadcast", Key.B, ModifierKeys.Control, OnToggleBroadcastHotkey);

                // Ctrl+T: Toggle tiling window
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("ToggleTiling", Key.T, ModifierKeys.Control, OnToggleTilingHotkey);

                // Ctrl+1 through Ctrl+6: Switch tiling layouts
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout1", Key.D1, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.Single));
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout2", Key.D2, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.SideBySide));
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout3", Key.D3, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.Grid2x2));
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout4", Key.D4, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.ThreeColumn));
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout5", Key.D5, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.PrimarySecondary));
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("Layout6", Key.D6, ModifierKeys.Control, (s, e) => SetTilingLayout(Services.TilingLayout.Sidebar));

                // Ctrl+Tab: Cycle to next window in tiling view
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("CycleNext", Key.Tab, ModifierKeys.Control, OnCycleNextHotkey);

                // Ctrl+Shift+Tab: Cycle to previous window
                NHotkey.Wpf.HotkeyManager.Current.AddOrReplace("CyclePrev", Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, OnCyclePrevHotkey);

                // Ctrl+1 through Ctrl+9: Direct window selection in tiling view
                // These are now registered locally in TilingWindow, not globally
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register hotkeys: {ex.Message}");
            }
        }

        private void OnToggleBroadcastHotkey(object? sender, NHotkey.HotkeyEventArgs e)
        {
            ToggleBroadcast();
            e.Handled = true;
        }

        private void OnToggleTilingHotkey(object? sender, NHotkey.HotkeyEventArgs e)
        {
            ToggleTilingWindow();
            e.Handled = true;
        }

        private void OnCycleNextHotkey(object? sender, NHotkey.HotkeyEventArgs e)
        {
            _tilingWindow?.CycleNextWindow();
            e.Handled = true;
        }

        private void OnCyclePrevHotkey(object? sender, NHotkey.HotkeyEventArgs e)
        {
            _tilingWindow?.CyclePreviousWindow();
            e.Handled = true;
        }

        private void SetTilingLayout(Services.TilingLayout layout)
        {
            if (_tilingWindow != null)
            {
                _tilingWindow.SetLayout(layout);
            }
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

            // Clean up input broadcaster and tiling window
            _inputBroadcaster?.Dispose();
            _tilingWindow?.Close();

            saveSettings();
            base.OnClosing(e);
        }

        public void closeAllAccounts()
        {
              foreach(Account a in Accounts)
            {
                a.StopWizard();
            }
        }

        public void addAccount(Account a)
        {
            Accounts.Add(a);
            saveInformation();
        }

        public void deleteAccount(Account a)
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
                        else if (key == "BroadcastEnabled")
                        {
                            if (bool.TryParse(value, out bool enabled) && enabled)
                            {
                                // Will be applied after InputBroadcaster is initialized
                                _shouldRestoreBroadcastState = true;
                            }
                        }
                        else if (key == "TilingWindowVisible")
                        {
                            _shouldShowTilingWindow = bool.TryParse(value, out bool visible) && visible;
                        }
                        else if (key == "TilingLayout")
                        {
                            if (Enum.TryParse<Services.TilingLayout>(value, out var layout))
                            {
                                _savedTilingLayout = layout;
                            }
                        }
                        else if (key == "TilingWindowLeft")
                        {
                            if (double.TryParse(value, out double left))
                            {
                                _savedTilingLeft = left;
                            }
                        }
                        else if (key == "TilingWindowTop")
                        {
                            if (double.TryParse(value, out double top))
                            {
                                _savedTilingTop = top;
                            }
                        }
                        else if (key == "TilingWindowWidth")
                        {
                            if (double.TryParse(value, out double width))
                            {
                                _savedTilingWidth = width;
                            }
                        }
                        else if (key == "TilingWindowHeight")
                        {
                            if (double.TryParse(value, out double height))
                            {
                                _savedTilingHeight = height;
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

                    // New broadcast and tiling settings
                    writer.WriteLine($"BroadcastEnabled={IsBroadcastEnabled}");
                    if (_tilingWindow != null)
                    {
                        writer.WriteLine($"TilingWindowVisible={_tilingWindow.IsVisible}");
                        writer.WriteLine($"TilingLayout={_tilingWindow.CurrentLayout}");
                        writer.WriteLine($"TilingWindowLeft={_tilingWindow.Left}");
                        writer.WriteLine($"TilingWindowTop={_tilingWindow.Top}");
                        writer.WriteLine($"TilingWindowWidth={_tilingWindow.Width}");
                        writer.WriteLine($"TilingWindowHeight={_tilingWindow.Height}");
                    }
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
        private readonly Dictionary<int, DateTime> _lastAudioSessionCacheAttempt = new();
        private static readonly TimeSpan AudioSessionCacheRetryInterval = TimeSpan.FromMilliseconds(750);

        // Debouncing for rapid window switches
        private System.Threading.Timer _focusDebounceTimer;
        private Account _pendingFocusAccount;
        private readonly object _focusLock = new();
        private const int FOCUS_DEBOUNCE_MS = 150;

        private void EnsureAudioSessionsCached(IEnumerable<int> processIds, bool forceRetry = false)
        {
            if (processIds == null)
            {
                return;
            }

            var targets = processIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            lock (_audioSessionLock)
            {
                var pending = new HashSet<int>();

                foreach (var processId in targets)
                {
                    if (_cachedAudioSessions.ContainsKey(processId))
                    {
                        continue;
                    }

                    if (!forceRetry &&
                        _lastAudioSessionCacheAttempt.TryGetValue(processId, out var lastAttempt) &&
                        (DateTime.UtcNow - lastAttempt) < AudioSessionCacheRetryInterval)
                    {
                        continue;
                    }

                    pending.Add(processId);
                    _lastAudioSessionCacheAttempt[processId] = DateTime.UtcNow;
                }

                if (pending.Count == 0)
                {
                    return;
                }

                try
                {
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

                                int processId;
                                try
                                {
                                    uint rawProcessId = session.GetProcessID;
                                    if (rawProcessId == 0 || rawProcessId > int.MaxValue)
                                    {
                                        continue;
                                    }
                                    processId = unchecked((int)rawProcessId);
                                }
                                catch
                                {
                                    continue;
                                }

                                if (!pending.Contains(processId))
                                {
                                    continue;
                                }

                                _cachedAudioSessions[processId] = session;
                                _lastAudioSessionCacheAttempt.Remove(processId);
                                Debug.WriteLine($"Cached audio session for process {processId}");

                                pending.Remove(processId);
                                if (pending.Count == 0)
                                {
                                    return;
                                }
                            }
                        }
                    }

                    foreach (var processId in pending)
                    {
                        Debug.WriteLine($"No audio session found for process {processId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to cache audio sessions: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Cache the audio session for a process. Call this once when a process starts.
        /// </summary>
        public void CacheAudioSessionForProcess(int processId)
        {
            EnsureAudioSessionsCached(new[] { processId }, forceRetry: true);
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
                    _lastAudioSessionCacheAttempt.Remove(processId);
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
            EnsureAudioSessionsCached(new[] { process.Id });
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
        /// Batched audio operation: unmute focused Account and mute all others in a single pass
        /// This is MUCH more efficient than individual operations
        /// </summary>
        private void MuteOtherAccounts(Account focusedAccount)
        {
            if (!MuteWhenNotInFocus)
            {
                return;
            }

            var activeAccounts = Accounts
                .Where(acc => acc.Process != null && !acc.Process.HasExited)
                .Select(acc => (Account: acc, ProcessId: acc.Process!.Id))
                .ToList();

            if (activeAccounts.Count == 0)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                EnsureAudioSessionsCached(activeAccounts.Select(info => info.ProcessId));

                lock (_audioSessionLock)
                {
                    try
                    {
                        foreach (var entry in activeAccounts)
                        {
                            int processId = entry.ProcessId;

                            if (entry.Account == focusedAccount)
                            {
                                // Restore volume for focused Account
                                RestoreVolumeForProcessInternal(processId, UnmuteVolume);
                            }
                            else
                            {
                                // Mute other accounts
                                MuteProcessInternal(processId);
                            }
                        }
                        Debug.WriteLine($"Batched audio adjustment completed for {activeAccounts.Count} accounts");
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
        public void DebouncedFocusAccount(Account acc)
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
        private void ExecuteFocusInternal(Account acc)
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
            EnsureAudioSessionsCached(new[] { processId });

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
            EnsureAudioSessionsCached(processIds);
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


        //Account class
        public class Account : INotifyPropertyChanged
        {

            public event PropertyChangedEventHandler? PropertyChanged;

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



            public Account(string name, string username, string password, MainWindow parent, GameServer server = GameServer.Wizard101_US)
            {
                Name = name;
                Username = username;
                Password = password;
                Process = null;
                IsRunning = false;
                Parent = parent;
                Server = server;
            }

            //starts the game for associated Account
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

                // Refresh tiling window to show new game instance
                Parent.RefreshTilingWindow();
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


            //stops the process with the current Account
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

                    // Refresh tiling window to remove closed game instance
                    Parent.RefreshTilingWindow();
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

                            Accounts.Add(new Account(info[0], username, password, this, server));
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
                foreach (Account a in Accounts)
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

            Account a = new Account(AccountNameTextBox.Text, UsernameTextBox.Text, password, this, selectedServer);
            addAccount(a);
            CloseAddAccountDialog();
        }

        private void FocusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Account acc)
            {
                acc.Focus();
            }
        }

        private void LaunchStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Account acc)
            {
                if (acc.IsRunning)
                {
                    // Stop the Account
                    acc.StopWizard();
                }
                else
                {
                    // Launch the Account
                    acc.StartWizard(waitInSeconds);
                }
            }
        }

        private void EditAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Account acc)
            {
                _editingAccount = acc;

                // Populate edit dialog with current Account data
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

                // Update Account properties
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
            foreach(Account a in AccountView.SelectedItems)
            {
                accounts.Add(a);
            }
            foreach (Account a in accounts)
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
            foreach(Account a in AccountView.SelectedItems)
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

        #region Multi-Instance Broadcasting and Tiling

        /// <summary>
        /// Gets window handles of all running game instances
        /// </summary>
        private IEnumerable<IntPtr> GetRunningGameWindowHandles()
        {
            return Accounts
                .Where(acc => acc.Process != null && !acc.Process.HasExited)
                .Select(acc => acc.Process!.MainWindowHandle)
                .Where(hwnd => hwnd != IntPtr.Zero);
        }

        /// <summary>
        /// Toggles broadcast mode on/off
        /// </summary>
        public void ToggleBroadcast()
        {
            _inputBroadcaster?.ToggleBroadcast();
        }

        /// <summary>
        /// Toggles the tiling window visibility
        /// </summary>
        public void ToggleTilingWindow()
        {
            if (_tilingWindow == null) return;

            if (_tilingWindow.IsVisible)
            {
                _tilingWindow.Hide();
            }
            else
            {
                _tilingWindow.Show();
                _tilingWindow.RefreshLayout();
            }
        }

        /// <summary>
        /// Event handler for broadcast state changes
        /// </summary>
        private void OnBroadcastStateChanged(object? sender, bool isEnabled)
        {
            IsBroadcastEnabled = isEnabled;
            Debug.WriteLine($"Broadcast mode is now {(isEnabled ? "ENABLED" : "DISABLED")}");

            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                if (BroadcastIcon != null && BroadcastText != null)
                {
                    BroadcastIcon.Kind = isEnabled ? PackIconKind.Broadcast : PackIconKind.BroadcastOff;
                    BroadcastText.Text = isEnabled ? "Broadcast: ON" : "Broadcast: OFF";

                    // Change button color to indicate state
                    if (BroadcastToggleButton != null)
                    {
                        BroadcastToggleButton.Background = isEnabled
                            ? new SolidColorBrush(Color.FromRgb(0xa6, 0xe3, 0xa1)) // Green
                            : null; // Default
                    }
                }
            });
        }

        /// <summary>
        /// Broadcast toggle button click handler
        /// </summary>
        private void BroadcastToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleBroadcast();
        }

        /// <summary>
        /// Tiling view button click handler
        /// </summary>
        private void TilingViewButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleTilingWindow();
        }

        /// <summary>
        /// Called when a window is "focused" in the tiling view - handles audio but does NOT focus the actual window
        /// </summary>
        private void FocusGameWindow(IntPtr windowHandle)
        {
            // Don't focus the actual game window - we're using input forwarding in the tiling view
            // Just handle audio muting for the focused account
            if (windowHandle != IntPtr.Zero && Win32.DwmInterop.IsWindow(windowHandle))
            {
                var account = Accounts.FirstOrDefault(acc =>
                    acc.Process != null &&
                    !acc.Process.HasExited &&
                    acc.Process.MainWindowHandle == windowHandle);

                if (account != null)
                {
                    MuteOtherAccounts(account);
                }
            }
        }

        /// <summary>
        /// Refreshes the tiling window layout (call when games are launched/closed)
        /// </summary>
        public void RefreshTilingWindow()
        {
            if (_tilingWindow != null && _tilingWindow.IsVisible)
            {
                _tilingWindow.RefreshLayout();
            }
        }

        #endregion
    }
}
