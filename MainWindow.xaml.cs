using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Windows.Documents;
using Velopack;
using Velopack.Sources;
using Forms = System.Windows.Forms; // Alias pour éviter les conflits avec WPF

namespace ARC_Sight
{
    public partial class MainWindow : Window
    {
        public static string AppVersion { get; } = "1.3.3";

        // Configuration des URLs
        private const string NOTE_URL = "https://raw.githubusercontent.com/rodafux/ARC-Sight/refs/heads/Default/msg.ini";
        private const string API_URL = "https://metaforge.app/api/arc-raiders/events-schedule";
        private const string HEARTBEAT_URL = "https://arbitrary-gertrudis-rodafux-fe0cc8aa.koyeb.app/ping";
        private const string GITHUB_REPO_URL = "https://github.com/rodafux/ARC-Sight";
        private const string GITHUB_RELEASE_API = "https://api.github.com/repos/rodafux/ARC-Sight/releases/tags/";
        private const string LANG_BASE_URL = "https://raw.githubusercontent.com/rodafux/ARC-Sight/Default/languages/";

        public static string AppDataPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARC-Sight");
        public static string ConfigFile { get; } = Path.Combine(AppDataPath, "config.ini");
        public static string LanguagesDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "languages");

        private readonly HttpClient _client = new HttpClient();
        private DispatcherTimer? _uiTimer;
        private DispatcherTimer? _apiTimer;
        private IntPtr _windowHandle;

        private static MediaPlayer _mediaPlayer = new MediaPlayer();

#if DEBUG
#else
        private Velopack.UpdateInfo? _updateInfo;
#endif

        private bool _isWindowLocked = true;
        private bool _isDragging = false;
        private System.Windows.Point _dragOffset;
        private Forms.NotifyIcon? _notifyIcon;

        public ObservableCollection<TabViewModel> Tabs { get; set; } = new ObservableCollection<TabViewModel>();
        public ImageSource? AppLogo { get; set; }

        public static string Hotkey { get; set; } = "F9";
        public static int NotifySeconds { get; set; } = 300;
        public static bool SoundEnabled { get; set; } = true;
        public static bool ShowLocalTime { get; set; } = false;
        public static string CurrentLanguage { get; set; } = "en";
        public static string LastSeenVersion { get; set; } = "v0.0.0";
        public static string CurrentLanguageAuthor { get; set; } = "Unknown";

        public static readonly Dictionary<string, string> Translations = new Dictionary<string, string>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            LoadLanguage(); // Charge d'abord le fichier local pour l'instantanéité
            LoadSoundFile();
            LoadLogoSafe();

            this.DataContext = this;
            MainTabControl.ItemsSource = Tabs;
            this.Loaded += MainWindow_Loaded;
            this.MouseMove += MainWindow_MouseMove;
            this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
        }

        // --- GESTION SERVEUR DES LANGUES ---
        private async Task UpdateLanguageFromServerAsync()
        {
            try
            {
                string fileName = $"lang_{CurrentLanguage}.ini";
                string url = $"{LANG_BASE_URL}{fileName}?t={DateTime.UtcNow.Ticks}";

                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        ParseLanguageContent(content);
                        UpdateLocalizedUI();
                    }
                }
            }
            catch { /* Fallback sur la version locale déjà chargée */ }
        }

        private static void ParseLanguageContent(string content)
        {
            Translations.Clear();
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("="))
                {
                    var p = line.Split(new[] { '=' }, 2);
                    if (p.Length > 1)
                    {
                        string key = p[0].Trim().ToLower();
                        string value = p[1].Trim();
                        if (key == "author") CurrentLanguageAuthor = value;
                        Translations[key] = value;
                    }
                }
            }
        }

        public static void LoadLanguage()
        {
            try
            {
                string path = Path.Combine(LanguagesDir, $"lang_{CurrentLanguage}.ini");
                if (!File.Exists(path)) path = Path.Combine(LanguagesDir, "lang_en.ini");

                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path, Encoding.UTF8);
                    ParseLanguageContent(content);
                }
            }
            catch { }
        }

        // --- SYSTEM TRAY ---
        private void SetupSystemTray()
        {
            _notifyIcon = new Forms.NotifyIcon();
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.ico");
                if (File.Exists(iconPath)) _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                else
                {
                    var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/logo.ico")).Stream;
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                }
            }
            catch { }

            _notifyIcon.Visible = true;
            _notifyIcon.Text = "ARC-Sight";
            _notifyIcon.Click += (s, e) => {
                if (this.Visibility == Visibility.Visible) this.Hide();
                else { this.Show(); this.Activate(); this.WindowState = WindowState.Normal; }
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (s, e) => { this.Show(); this.Activate(); });
            contextMenu.Items.Add("Exit", null, (s, e) => System.Windows.Application.Current.Shutdown());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        private void LoadLogoSafe()
        {
            try { AppLogo = new BitmapImage(new Uri("pack://application:,,,/assets/logo.png")); }
            catch
            {
                try
                {
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
                    if (File.Exists(localPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                        AppLogo = bitmap;
                    }
                }
                catch { }
            }
        }

        private void LoadSoundFile()
        {
            try
            {
                string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
                string mp3Path = Path.Combine(assetsPath, "Notif.mp3");
                string wavPath = Path.Combine(assetsPath, "Notif.wav");
                string finalPath = File.Exists(mp3Path) ? mp3Path : (File.Exists(wavPath) ? wavPath : "");
                if (!string.IsNullOrEmpty(finalPath)) _mediaPlayer.Open(new Uri(finalPath, UriKind.Absolute));
            }
            catch { }
        }

        private async Task StartHeartbeat()
        {
            while (true)
            {
                try
                {
                    var payload = new { version = AppVersion };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var request = new HttpRequestMessage(HttpMethod.Post, HEARTBEAT_URL) { Content = content };
                    request.Headers.Add("User-Agent", "ARC-Sight-Desktop-Client/1.0");
                    await _client.SendAsync(request);
                }
                catch { }
                await Task.Delay(60000);
            }
        }

        private async Task FetchNote()
        {
            try
            {
                string url = $"{NOTE_URL}?t={DateTime.UtcNow.Ticks}";
                var content = await _client.GetStringAsync(url);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    string targetKey = CurrentLanguage.ToUpper() + "=";
                    string message = lines.FirstOrDefault(l => l.StartsWith(targetKey))?.Substring(targetKey.Length).Trim() ?? "";

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string header = GetTrans("note_header", "UI");
                        if (string.IsNullOrEmpty(header)) header = "NOTE IMPORTANTE :";
                        NoteText.Inlines.Clear();
                        NoteText.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(header + " ")));
                        string pattern = @"(https?://[^\s]+)";
                        string[] parts = Regex.Split(message, pattern);
                        foreach (var part in parts)
                        {
                            if (Regex.IsMatch(part, pattern))
                            {
                                var link = new Hyperlink(new Run(part)) { NavigateUri = new Uri(part), Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 240, 255)) };
                                link.RequestNavigate += (s, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; };
                                NoteText.Inlines.Add(link);
                            }
                            else NoteText.Inlines.Add(new Run(part));
                        }
                        NoteText.Visibility = Visibility.Visible;
                    }
                    else NoteText.Visibility = Visibility.Collapsed;
                }
            }
            catch { NoteText.Visibility = Visibility.Collapsed; }
        }

        private async Task CheckForUpdates()
        {
            try
            {
#if DEBUG
#else
                var mgr = new UpdateManager(new GithubSource(GITHUB_REPO_URL, null, false));
                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion != null) { _updateInfo = newVersion; this.Dispatcher.Invoke(() => { UpdateBtn.Content = GetTrans("update_available_button", "UI"); UpdateBtn.Visibility = Visibility.Visible; }); }
#endif
            }
            catch { }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            System.Windows.MessageBox.Show("Update simulation in DEBUG mode.");
#else
            if (_updateInfo == null) return;
            try
            {
                UpdateBtn.Visibility = Visibility.Collapsed; UpdateProgressPanel.Visibility = Visibility.Visible; UpdateBtn.IsEnabled = false;
                var mgr = new UpdateManager(new GithubSource(GITHUB_REPO_URL, null, false));
                Action<int> progressAction = percent => this.Dispatcher.Invoke(() => UpdateProgressBar.Value = percent);
                await mgr.DownloadUpdatesAsync(_updateInfo, progressAction);
                mgr.ApplyUpdatesAndRestart(_updateInfo);
            }
            catch { UpdateBtn.Content = GetTrans("update_error", "UI"); UpdateBtn.IsEnabled = true; UpdateBtn.Visibility = Visibility.Visible; UpdateProgressPanel.Visibility = Visibility.Collapsed; }
#endif
        }

        public static void TriggerNotification(string title, string message)
        {
            if (SoundEnabled) { try { _mediaPlayer.Stop(); _mediaPlayer.Play(); } catch { } }
            System.Windows.Application.Current.Dispatcher.Invoke(() => { try { new ToastWindow(title, message).Show(); } catch { } });
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Left = 0; this.Top = 0; this.Width = SystemParameters.PrimaryScreenWidth;
            UpdateLocalizedUI();
            _windowHandle = new WindowInteropHelper(this).Handle;
            SetWindowLong(_windowHandle, -16, GetWindowLong(_windowHandle, -16) & ~0x10000);
            HwndSource.FromHwnd(_windowHandle)?.AddHook(HwndHook);
            RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, ev) => UpdateAllTimers(); _uiTimer.Start();

            _apiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _apiTimer.Tick += async (s, ev) => { await FetchData(); await FetchNote(); }; _apiTimer.Start();

            await InitialLoad();
            _ = StartHeartbeat(); _ = CheckForUpdates(); SetupSystemTray();
        }

        private async Task InitialLoad()
        {
            StatusText.Text = "Initializing...";
            await Task.Delay(1000);
            await UpdateLanguageFromServerAsync(); // Mise à jour à distance des traductions
            await FetchData();
            await FetchNote();
            await CheckAndShowChangelog();
        }

        private async Task CheckAndShowChangelog()
        {
            if (AppVersion != LastSeenVersion) { await FetchAndShowChangelogData(AppVersion); LastSeenVersion = AppVersion; SaveConfig(); }
        }

        public async Task FetchAndShowChangelogData(string versionTag)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{GITHUB_RELEASE_API}{versionTag}");
                request.Headers.Add("User-Agent", "ARC-Sight-App");
                var response = await _client.SendAsync(request);
                string notes = "No details available.";
                if (response.IsSuccessStatusCode)
                {
                    using (JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                        if (doc.RootElement.TryGetProperty("body", out var body)) notes = body.GetString() ?? "No content.";
                }
                new ChangelogWindow(notes) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Error fetching notes: {ex.Message}"); }
        }

        private void UpdateLocalizedUI()
        {
            string tooltip = GetTrans("lock_tooltip", "UI");
            if (string.IsNullOrEmpty(tooltip)) tooltip = "Lock / Unlock window position";
            if (LockBtn != null) LockBtn.ToolTip = tooltip;
        }

        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox lb && e.Delta != 0)
            {
                var sv = FindVisualChild<ScrollViewer>(lb);
                if (sv != null) { for (int i = 0; i < 40; i++) { if (e.Delta > 0) sv.LineLeft(); else sv.LineRight(); } e.Handled = true; }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject? p) where T : DependencyObject
        {
            if (p == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++)
            {
                var c = VisualTreeHelper.GetChild(p, i);
                if (c is T t) return t;
                var res = FindVisualChild<T>(c); if (res != null) return res;
            }
            return null;
        }

        private async Task FetchData()
        {
            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    StatusText.Text = i == 0 ? "Updating..." : $"Retrying ({i}/{maxRetries})...";
                    if (!_client.DefaultRequestHeaders.UserAgent.Any()) _client.DefaultRequestHeaders.UserAgent.ParseAdd("ARC-Sight/1.0");
                    var response = await _client.GetAsync(API_URL);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var doc = JsonDocument.Parse(json);
                        List<ScheduleEvent>? raw = null;
                        if (doc.RootElement.ValueKind == JsonValueKind.Array) raw = JsonSerializer.Deserialize<List<ScheduleEvent>>(json);
                        else if (doc.RootElement.TryGetProperty("events", out var ev)) raw = JsonSerializer.Deserialize<List<ScheduleEvent>>(ev.GetRawText());
                        else if (doc.RootElement.TryGetProperty("data", out var da)) raw = JsonSerializer.Deserialize<List<ScheduleEvent>>(da.GetRawText());

                        if (raw != null && raw.Count > 0) { ProcessScheduleData(raw); StatusText.Text = ""; return; }
                    }
                }
                catch { if (i < maxRetries - 1) await Task.Delay(2000); }
            }
            StatusText.Text = "API Error (Check Connection)";
        }

        private void ProcessScheduleData(List<ScheduleEvent> schedule)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var processed = schedule.GroupBy(e => new { e.name, e.map }).Select(g => new EventDisplayData(g.FirstOrDefault(e => e.startTime <= now && e.endTime > now) ?? g.Where(e => e.startTime > now).OrderBy(e => e.startTime).FirstOrDefault()!)).Where(d => d.Raw != null).ToList();
            UpdateUiWithProcessedData(processed);
        }

        private void UpdateUiWithProcessedData(List<EventDisplayData> data)
        {
            var allTab = Tabs.FirstOrDefault(t => t.Header == "ALL");
            if (allTab == null) { allTab = new TabViewModel("ALL"); Tabs.Insert(0, allTab); }
            MergeCards(allTab.Cards, data);
            foreach (var group in data.GroupBy(e => e.Raw.name).OrderBy(g => g.Key))
            {
                string name = GetTrans(group.Key ?? "Unknown", "TABS");
                var tab = Tabs.FirstOrDefault(t => t.Header == name) ?? new TabViewModel(name);
                if (!Tabs.Contains(tab)) Tabs.Add(tab);
                MergeCards(tab.Cards, group.ToList());
            }
        }

        private void MergeCards(ObservableCollection<CardViewModel> coll, List<EventDisplayData> evts)
        {
            for (int i = coll.Count - 1; i >= 0; i--) if (!evts.Any(e => e.Raw.name == coll[i].RawData.name && e.Raw.map == coll[i].RawData.map)) coll.RemoveAt(i);
            foreach (var e in evts)
            {
                var ex = coll.FirstOrDefault(c => c.RawData.name == e.Raw.name && c.RawData.map == e.Raw.map);
                if (ex != null) ex.UpdateData(e.Raw);
                else { var n = new CardViewModel(e.Raw); n.RequestNotification += TriggerNotification; coll.Add(n); }
            }
        }

        private void UpdateAllTimers() { foreach (var t in Tabs) foreach (var c in t.Cards) c.UpdateTimer(); }
        public static string GetTrans(string key, string section) { if (string.IsNullOrEmpty(key)) return ""; string k = key.Replace(" ", "_").ToLower().Trim(); return Translations.ContainsKey(k) ? Translations[k] : key.ToUpper(); }

        private void LoadConfig()
        {
            if (File.Exists(ConfigFile))
            {
                foreach (var line in File.ReadAllLines(ConfigFile))
                {
                    var p = line.Split('='); if (p.Length < 2) continue;
                    if (line.StartsWith("hotkey=")) Hotkey = p[1];
                    if (line.StartsWith("language=")) CurrentLanguage = p[1];
                    if (line.StartsWith("notify_minutes=") && int.TryParse(p[1], out int m)) NotifySeconds = m * 60;
                    if (line.StartsWith("sound_enabled=") && bool.TryParse(p[1], out bool s)) SoundEnabled = s;
                    if (line.StartsWith("show_local_time=") && bool.TryParse(p[1], out bool sl)) ShowLocalTime = sl;
                    if (line.StartsWith("last_seen_version=")) LastSeenVersion = p[1];
                }
            }
        }

        public static void SaveConfig() { try { Directory.CreateDirectory(AppDataPath); File.WriteAllLines(ConfigFile, new[] { $"hotkey={Hotkey}", $"notify_minutes={NotifySeconds / 60}", $"language={CurrentLanguage}", $"sound_enabled={SoundEnabled}", $"show_local_time={ShowLocalTime}", $"last_seen_version={LastSeenVersion}" }); } catch { } }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int i, uint f, uint v);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int n);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int n, int l);

        private IntPtr HwndHook(IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) { if (m == 0x0312 && w.ToInt32() == 1) { if (Visibility == Visibility.Visible) Hide(); else { Show(); Activate(); } handled = true; } return IntPtr.Zero; }
        public static uint GetVkCode(string k) { if (string.IsNullOrEmpty(k)) return 0x78; if (k.StartsWith("F") && int.TryParse(k.Substring(1), out int n)) return (uint)(0x70 + n - 1); return (uint)k.ToUpper()[0]; }
        private void OpenSettings_Click(object sender, RoutedEventArgs e) { if (new SettingsWindow { Owner = this }.ShowDialog() == true) { UnregisterHotKey(_windowHandle, 1); RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey)); Tabs.Clear(); _ = InitialLoad(); UpdateLocalizedUI(); } }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { if (new ConfirmationWindow(GetTrans("exit_confirm_title", "UI"), GetTrans("exit_confirm_msg", "UI"), GetTrans("yes_btn", "UI"), GetTrans("no_btn", "UI")) { Owner = this }.ShowDialog() == true) System.Windows.Application.Current.Shutdown(); }
        private void ToggleLock_Click(object sender, RoutedEventArgs e) { _isWindowLocked = !_isWindowLocked; LockBtn.Content = _isWindowLocked ? "🔒" : "🔓"; LockBtn.Foreground = _isWindowLocked ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 0)) : System.Windows.Media.Brushes.White; this.ResizeMode = _isWindowLocked ? ResizeMode.NoResize : ResizeMode.CanResize; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!_isWindowLocked) { _isDragging = true; _dragOffset = e.GetPosition(this); this.CaptureMouse(); } }
        private void MainWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (_isDragging) { var d = e.GetPosition(this) - _dragOffset; this.Left += d.X; this.Top += d.Y; } }
        private void MainWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_isDragging) { _isDragging = false; this.ReleaseMouseCapture(); } }
    }

    // --- CLASSES AUXILIAIRES ---
    public class ScheduleEvent { public string? name { get; set; } public string? map { get; set; } public string? icon { get; set; } public long startTime { get; set; } public long endTime { get; set; } }
    public class EventDisplayData { public ScheduleEvent Raw { get; set; } public EventDisplayData(ScheduleEvent r) { Raw = r; } }
    public class TabViewModel { public string Header { get; set; } public ObservableCollection<CardViewModel> Cards { get; set; } public ICollectionView SortedCards { get; set; } public TabViewModel(string h) { Header = h; Cards = new ObservableCollection<CardViewModel>(); SortedCards = CollectionViewSource.GetDefaultView(Cards); SortedCards.SortDescriptions.Add(new SortDescription(nameof(CardViewModel.IsActive), ListSortDirection.Descending)); SortedCards.SortDescriptions.Add(new SortDescription(nameof(CardViewModel.TargetTime), ListSortDirection.Ascending)); var lv = (ICollectionViewLiveShaping)SortedCards; if (lv.CanChangeLiveSorting) { lv.IsLiveSorting = true; lv.LiveSortingProperties.Add(nameof(CardViewModel.IsActive)); lv.LiveSortingProperties.Add(nameof(CardViewModel.TargetTime)); } } }

    public class CardViewModel : INotifyPropertyChanged
    {
        public ScheduleEvent RawData;
        public event Action<string, string>? RequestNotification;
        public string Title => MainWindow.GetTrans(RawData.name ?? "", "TABS");
        public string Map => MainWindow.GetTrans(RawData.map ?? "", "MAPS");
        public string AlertLabel => MainWindow.GetTrans("alert_button_label", "UI");
        public ImageSource? BackgroundImage { get; private set; }

        private bool _isActive; public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } } }
        private DateTime _targetTime = DateTime.MaxValue; public DateTime TargetTime { get => _targetTime; set { if (_targetTime != value) { _targetTime = value; OnPropertyChanged(nameof(TargetTime)); } } }
        private string _timerText = "--:--"; public string TimerText { get => _timerText; set { if (_timerText != value) { _timerText = value; OnPropertyChanged(nameof(TimerText)); } } }
        private string _timerPrefix = ""; public string TimerPrefix { get => _timerPrefix; set { if (_timerPrefix != value) { _timerPrefix = value; OnPropertyChanged(nameof(TimerPrefix)); } } }
        private string _localTimeText = ""; public string LocalTimeText { get => _localTimeText; set { if (_localTimeText != value) { _localTimeText = value; OnPropertyChanged(nameof(LocalTimeText)); } } }

        // Résolution de l'ambiguïté des Brushes
        private System.Windows.Media.Brush _timerColor = System.Windows.Media.Brushes.White;
        public System.Windows.Media.Brush TimerColor { get => _timerColor; set { if (_timerColor != value) { _timerColor = value; OnPropertyChanged(nameof(TimerColor)); } } }

        private System.Windows.Media.Brush _borderColor = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush BorderColor { get => _borderColor; set { if (_borderColor != value) { _borderColor = value; OnPropertyChanged(nameof(BorderColor)); } } }

        private bool _isAlertEnabled; public bool IsAlertEnabled { get => _isAlertEnabled; set { _isAlertEnabled = value; OnPropertyChanged(nameof(IsAlertEnabled)); if (!value) HasNotified = false; } }
        private Visibility _alertVisibility = Visibility.Visible; public Visibility AlertVisibility { get => _alertVisibility; set { if (_alertVisibility != value) { _alertVisibility = value; OnPropertyChanged(nameof(AlertVisibility)); } } }
        private double _localTimeFontSize = 20; public double LocalTimeFontSize { get => _localTimeFontSize; set { if (_localTimeFontSize != value) { _localTimeFontSize = value; OnPropertyChanged(nameof(LocalTimeFontSize)); } } }
        private bool HasNotified = false;

        public CardViewModel(ScheduleEvent d) { RawData = d; BorderColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)); LoadImage(); UpdateTimer(); }
        public void UpdateData(ScheduleEvent n) { if (RawData.startTime != n.startTime || RawData.endTime != n.endTime) { RawData = n; HasNotified = false; UpdateTimer(); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Map)); } }

        private void LoadImage()
        {
            string m = RawData.map ?? "";
            string f = m.Contains("Dam") ? "Barrage.png" : (m.Contains("Spaceport") ? "Port_spatial.png" : (m.Contains("Buried") ? "Ville_enfouie.png" : (m.Contains("Gate") ? "Portail_bleu.png" : (m.Contains("Stella") ? "Stella_montis.png" : "Barrage.png"))));
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", f);
            if (File.Exists(p)) try { BackgroundImage = new BitmapImage(new Uri(p)); } catch { }
        }

        public void UpdateTimer()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool active = (now >= RawData.startTime && now < RawData.endTime);
            IsActive = active;
            AlertVisibility = active ? Visibility.Collapsed : Visibility.Visible;
            string sTxt = MainWindow.GetTrans("timer_start_prefix", "UI"); if (sTxt == "TIMER_START_PREFIX") sTxt = "STARTS IN";
            string eTxt = MainWindow.GetTrans("timer_end_prefix", "UI"); if (eTxt == "TIMER_END_PREFIX") eTxt = "ENDS IN";

            TimeSpan diff;
            if (active)
            {
                diff = TimeSpan.FromMilliseconds(RawData.endTime - now);
                TargetTime = DateTimeOffset.FromUnixTimeMilliseconds(RawData.endTime).LocalDateTime;
                TimerPrefix = eTxt;
                TimerColor = System.Windows.Media.Brushes.OrangeRed;
                BorderColor = System.Windows.Media.Brushes.OrangeRed;
                IsAlertEnabled = false;
                LocalTimeText = "";
            }
            else
            {
                diff = TimeSpan.FromMilliseconds(RawData.startTime - now);
                TargetTime = DateTimeOffset.FromUnixTimeMilliseconds(RawData.startTime).LocalDateTime;
                TimerPrefix = sTxt;
                LocalTimeText = MainWindow.ShowLocalTime ? TargetTime.ToString("HH:mm") : "";
                if (diff.TotalSeconds <= MainWindow.NotifySeconds && diff.TotalSeconds > 0)
                {
                    TimerColor = System.Windows.Media.Brushes.Yellow; BorderColor = System.Windows.Media.Brushes.Yellow;
                    if (IsAlertEnabled && !HasNotified)
                    {
                        string pat = MainWindow.GetTrans("notify_message", "UI");
                        if (string.IsNullOrEmpty(pat) || pat == "NOTIFY_MESSAGE") pat = "STARTING IN {minutes} MIN - {map_name}";
                        MainWindow.TriggerNotification(Title, pat.Replace("{minutes}", ((int)diff.TotalMinutes).ToString()).Replace("{map_name}", Map));
                        HasNotified = true;
                    }
                }
                else
                {
                    TimerColor = System.Windows.Media.Brushes.White;
                    BorderColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60));
                    HasNotified = false;
                }
            }
            TimerText = diff.TotalHours >= 1 ? $"{(int)diff.TotalHours}h {diff.Minutes}m" : (diff.TotalSeconds > 0 ? $"{diff.Minutes:D2}:{diff.Seconds:D2}" : "00:00");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}