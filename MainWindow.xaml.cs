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
using Forms = System.Windows.Forms;

namespace ARC_Sight
{
    public partial class MainWindow : Window
    {
        public static string AppVersion { get; } = "1.3.3";

        private const string NOTE_URL = "https://raw.githubusercontent.com/rodafux/ARC-Sight/refs/heads/Default/msg.ini";
        private const string API_URL = "https://metaforge.app/api/arc-raiders/events-schedule";
        private const string HEARTBEAT_URL = "https://arbitrary-gertrudis-rodafux-fe0cc8aa.koyeb.app/ping";
        private const string GITHUB_REPO_URL = "https://github.com/rodafux/ARC-Sight";
        private const string GITHUB_RELEASE_API = "https://api.github.com/repos/rodafux/ARC-Sight/releases/tags/";

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

        public static readonly Dictionary<string, string> Translations = new Dictionary<string, string>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            LoadLanguage();
            LoadSoundFile();
            LoadLogoSafe();

            this.DataContext = this;

            MainTabControl.ItemsSource = Tabs;
            this.Loaded += MainWindow_Loaded;

            this.MouseMove += MainWindow_MouseMove;
            this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
        }

        private void SetupSystemTray()
        {
            _notifyIcon = new Forms.NotifyIcon();
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.ico");

                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/logo.ico")).Stream;
                    _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Erreur Icône Tray : " + ex.Message);
            }

            _notifyIcon.Visible = true;
            _notifyIcon.Text = "ARC-Sight";

            _notifyIcon.Click += (s, e) => {
                if (this.Visibility == Visibility.Visible)
                {
                    this.Hide();
                }
                else
                {
                    this.Show();
                    this.Activate();
                    this.WindowState = WindowState.Normal;
                }
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Ouvrir", null, (s, e) => { this.Show(); this.Activate(); });
            contextMenu.Items.Add("Quitter", null, (s, e) => System.Windows.Application.Current.Shutdown());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        private void LoadLogoSafe()
        {
            try
            {
                AppLogo = new BitmapImage(new Uri("pack://application:,,,/assets/logo.png"));
            }
            catch
            {
                try
                {
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
                    if (File.Exists(localPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
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

                string finalPath = "";
                if (File.Exists(mp3Path)) finalPath = mp3Path;
                else if (File.Exists(wavPath)) finalPath = wavPath;

                if (!string.IsNullOrEmpty(finalPath))
                {
                    _mediaPlayer.Open(new Uri(finalPath, UriKind.Absolute));
                }
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
                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(HttpMethod.Post, HEARTBEAT_URL);
                    request.Headers.Add("User-Agent", "ARC-Sight-Desktop-Client/1.0");
                    request.Content = content;

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
                    string message = "";

                    foreach (var line in lines)
                    {
                        if (line.StartsWith(targetKey))
                        {
                            message = line.Substring(targetKey.Length).Trim();
                            break;
                        }
                    }

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
                                var link = new Hyperlink(new Run(part))
                                {
                                    NavigateUri = new Uri(part),
                                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 240, 255))
                                };
                                link.RequestNavigate += (s, e) => {
                                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                                    e.Handled = true;
                                };
                                NoteText.Inlines.Add(link);
                            }
                            else
                            {
                                NoteText.Inlines.Add(new Run(part));
                            }
                        }
                        NoteText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        NoteText.Visibility = Visibility.Collapsed;
                    }
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
                if (newVersion != null)
                {
                    _updateInfo = newVersion;
                    this.Dispatcher.Invoke(() =>
                    {
                        UpdateBtn.Content = GetTrans("update_available_button", "UI");
                        UpdateBtn.Visibility = Visibility.Visible;
                    });
                }
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
                UpdateBtn.Visibility = Visibility.Collapsed;
                UpdateProgressPanel.Visibility = Visibility.Visible;
                UpdateBtn.IsEnabled = false;

                var mgr = new UpdateManager(new GithubSource(GITHUB_REPO_URL, null, false));
                Action<int> progressAction = percent => this.Dispatcher.Invoke(() => UpdateProgressBar.Value = percent);

                await mgr.DownloadUpdatesAsync(_updateInfo, progressAction);
                mgr.ApplyUpdatesAndRestart(_updateInfo);
            }
            catch
            {
                UpdateBtn.Content = GetTrans("update_error", "UI");
                UpdateBtn.IsEnabled = true;
                UpdateBtn.Visibility = Visibility.Visible;
                UpdateProgressPanel.Visibility = Visibility.Collapsed;
            }
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
            var style = GetWindowLong(_windowHandle, GWL_STYLE);
            SetWindowLong(_windowHandle, GWL_STYLE, style & ~WS_MAXIMIZEBOX);

            HwndSource? source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(HwndHook);
            RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, ev) => UpdateAllTimers();
            _uiTimer.Start();

            _apiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _apiTimer.Tick += async (s, ev) => { await FetchData(); await FetchNote(); };
            _apiTimer.Start();

            await InitialLoad();
            _ = StartHeartbeat();
            _ = CheckForUpdates();
            SetupSystemTray();
        }

        private async Task InitialLoad()
        {
            StatusText.Text = "Initializing...";
            await Task.Delay(1000);
            await FetchData();
            await FetchNote();
            await CheckAndShowChangelog();
        }

        private async Task CheckAndShowChangelog()
        {
            if (AppVersion != LastSeenVersion)
            {
                await FetchAndShowChangelogData(AppVersion);
                LastSeenVersion = AppVersion;
                SaveConfig();
            }
        }

        public async Task FetchAndShowChangelogData(string versionTag)
        {
            try
            {
                string url = $"{GITHUB_RELEASE_API}{versionTag}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "ARC-Sight-App");

                var response = await _client.SendAsync(request);
                string notes = "No details available.";

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("body", out var bodyElement))
                        {
                            notes = bodyElement.GetString() ?? "No content.";
                        }
                    }
                }
                else
                {
                    notes = $"Could not fetch patch notes for {versionTag}.\n(GitHub API limit or invalid tag)";
                }

                ChangelogWindow cw = new ChangelogWindow(notes);
                cw.Owner = this;
                cw.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error fetching notes: {ex.Message}");
            }
        }

        private void UpdateLocalizedUI()
        {
            string tooltip = GetTrans("lock_tooltip", "UI");
            if (string.IsNullOrEmpty(tooltip)) tooltip = "Lock / Unlock window position";
            if (LockBtn != null) LockBtn.ToolTip = tooltip;
        }

        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox listBox && e.Delta != 0)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    for (int i = 0; i < 40; i++) { if (e.Delta > 0) scrollViewer.LineLeft(); else scrollViewer.LineRight(); }
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        private async Task FetchData()
        {
            int maxRetries = 3;
            int delayBetweenRetries = 2000;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    StatusText.Text = i == 0 ? "Updating..." : $"Retrying ({i}/{maxRetries})...";

                    if (!_client.DefaultRequestHeaders.UserAgent.Any())
                    {
                        _client.DefaultRequestHeaders.UserAgent.ParseAdd("ARC-Sight/1.0");
                    }

                    var response = await _client.GetAsync(API_URL);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var doc = JsonDocument.Parse(json);
                        JsonElement root = doc.RootElement;
                        List<ScheduleEvent>? rawEvents = null;

                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            rawEvents = JsonSerializer.Deserialize<List<ScheduleEvent>>(json);
                        }
                        else if (root.ValueKind == JsonValueKind.Object)
                        {
                            if (root.TryGetProperty("events", out var eventsElem))
                                rawEvents = JsonSerializer.Deserialize<List<ScheduleEvent>>(eventsElem.GetRawText());
                            else if (root.TryGetProperty("data", out var dataElem))
                                rawEvents = JsonSerializer.Deserialize<List<ScheduleEvent>>(dataElem.GetRawText());
                        }

                        if (rawEvents != null && rawEvents.Count > 0)
                        {
                            ProcessScheduleData(rawEvents);
                            StatusText.Text = "";
                            return;
                        }
                        else
                        {
                            StatusText.Text = "No events found";
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {i + 1} failed: {ex.Message}");
                }

                if (i < maxRetries - 1) await Task.Delay(delayBetweenRetries);
            }

            StatusText.Text = "API Error (Check Connection)";
        }

        private void ProcessScheduleData(List<ScheduleEvent> schedule)
        {
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var processedData = new List<EventDisplayData>();

            var groups = schedule.GroupBy(e => new { e.name, e.map });

            foreach (var group in groups)
            {
                var active = group.FirstOrDefault(e => e.startTime <= nowUnix && e.endTime > nowUnix);

                if (active != null)
                {
                    processedData.Add(new EventDisplayData(active));
                }
                else
                {
                    var next = group.Where(e => e.startTime > nowUnix).OrderBy(e => e.startTime).FirstOrDefault();
                    if (next != null)
                    {
                        processedData.Add(new EventDisplayData(next));
                    }
                }
            }

            UpdateUiWithProcessedData(processedData);
        }

        private void UpdateUiWithProcessedData(List<EventDisplayData> data)
        {
            var allTab = Tabs.FirstOrDefault(t => t.Header == "ALL");
            if (allTab == null) { allTab = new TabViewModel("ALL"); Tabs.Insert(0, allTab); }

            MergeCards(allTab.Cards, data);

            var grouped = data.GroupBy(e => e.Raw.name).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                string tabName = GetTrans(group.Key ?? "Unknown", "TABS");
                var tab = Tabs.FirstOrDefault(t => t.Header == tabName);
                if (tab == null) { tab = new TabViewModel(tabName); Tabs.Add(tab); }
                MergeCards(tab.Cards, group.ToList());
            }
        }

        private void MergeCards(ObservableCollection<CardViewModel> collection, List<EventDisplayData> newEvents)
        {
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                var card = collection[i];
                if (!newEvents.Any(e => e.Raw.name == card.RawData.name && e.Raw.map == card.RawData.map))
                {
                    collection.RemoveAt(i);
                }
            }

            foreach (var evt in newEvents)
            {
                var existing = collection.FirstOrDefault(c => c.RawData.name == evt.Raw.name && c.RawData.map == evt.Raw.map);
                if (existing != null)
                {
                    existing.UpdateData(evt.Raw);
                }
                else
                {
                    var newCard = new CardViewModel(evt.Raw);
                    newCard.RequestNotification += TriggerNotification;
                    collection.Add(newCard);
                }
            }
        }

        private void UpdateAllTimers()
        {
            foreach (var tab in Tabs) foreach (var card in tab.Cards) card.UpdateTimer();
        }

        public static string GetTrans(string key, string section)
        {
            if (string.IsNullOrEmpty(key)) return "";

            string k = key.Replace(" ", "_").ToLower().Trim();

            if (Translations.ContainsKey(k))
            {
                return Translations[k];
            }
            return key.ToUpper();
        }

        private void LoadConfig()
        {
            if (File.Exists(ConfigFile))
            {
                foreach (var line in File.ReadAllLines(ConfigFile))
                {
                    if (line.StartsWith("hotkey=")) Hotkey = line.Split('=')[1];
                    if (line.StartsWith("language=")) CurrentLanguage = line.Split('=')[1];
                    if (line.StartsWith("notify_minutes=") && int.TryParse(line.Split('=')[1], out int m)) NotifySeconds = m * 60;
                    if (line.StartsWith("sound_enabled=")) { if (bool.TryParse(line.Split('=')[1], out bool s)) SoundEnabled = s; }
                    if (line.StartsWith("show_local_time=")) { if (bool.TryParse(line.Split('=')[1], out bool sl)) ShowLocalTime = sl; }
                    if (line.StartsWith("last_seen_version=")) LastSeenVersion = line.Split('=')[1];
                }
            }
        }

        public static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(AppDataPath);
                string[] lines = {
                    $"hotkey={Hotkey}",
                    $"notify_minutes={NotifySeconds/60}",
                    $"language={CurrentLanguage}",
                    $"sound_enabled={SoundEnabled}",
                    $"show_local_time={ShowLocalTime}",
                    $"last_seen_version={LastSeenVersion}"
                };
                File.WriteAllLines(ConfigFile, lines);
            }
            catch { }
        }

        public static string CurrentLanguageAuthor { get; set; } = "Unknown";

        public static void LoadLanguage()
        {
            Translations.Clear();
            CurrentLanguageAuthor = "Unknown";

            string path = Path.Combine(LanguagesDir, $"lang_{CurrentLanguage}.ini");
            if (!File.Exists(path)) path = Path.Combine(LanguagesDir, "lang_en.ini");

            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Contains("="))
                    {
                        var p = line.Split(new[] { '=' }, 2);
                        if (p.Length > 1)
                        {
                            string key = p[0].Trim().ToLower();
                            string value = p[1].Trim();

                            if (key == "author")
                            {
                                CurrentLanguageAuthor = value;
                            }

                            Translations[key] = value;
                        }
                    }
                }
            }
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int WM_HOTKEY = 0x0312; private const int GWL_STYLE = -16; private const int WS_MAXIMIZEBOX = 0x10000;

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 1) { if (Visibility == Visibility.Visible) Hide(); else { Show(); Activate(); } handled = true; }
            return IntPtr.Zero;
        }

        public static uint GetVkCode(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0x78;
            if (key.StartsWith("F") && int.TryParse(key.Substring(1), out int n)) return (uint)(0x70 + n - 1);
            char c = key.ToUpper()[0];
            if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) return (uint)c;
            return 0x78;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow sw = new SettingsWindow(); sw.Owner = this;
            if (sw.ShowDialog() == true)
            {
                UnregisterHotKey(_windowHandle, 1); RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey));
                Tabs.Clear(); _ = InitialLoad(); UpdateLocalizedUI();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConfirmationWindow(GetTrans("exit_confirm_title", "UI"), GetTrans("exit_confirm_msg", "UI"), GetTrans("yes_btn", "UI"), GetTrans("no_btn", "UI"));
            dialog.Owner = this;
            if (dialog.ShowDialog() == true) System.Windows.Application.Current.Shutdown();
        }

        private void ToggleLock_Click(object sender, RoutedEventArgs e)
        {
            _isWindowLocked = !_isWindowLocked;
            LockBtn.Content = _isWindowLocked ? "🔒" : "🔓";
            LockBtn.Foreground = _isWindowLocked ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 0)) : System.Windows.Media.Brushes.White;
            this.ResizeMode = _isWindowLocked ? ResizeMode.NoResize : ResizeMode.CanResize;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!_isWindowLocked) { _isDragging = true; _dragOffset = e.GetPosition(this); this.CaptureMouse(); } }

        private void MainWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { if (_isDragging) { var diff = e.GetPosition(this) - _dragOffset; this.Left += diff.X; this.Top += diff.Y; } }

        private void MainWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { if (_isDragging) { _isDragging = false; this.ReleaseMouseCapture(); } }
    }

    public class ScheduleEvent
    {
        public string? name { get; set; }
        public string? map { get; set; }
        public string? icon { get; set; }
        public long startTime { get; set; }
        public long endTime { get; set; }
    }

    public class EventDisplayData
    {
        public ScheduleEvent Raw { get; set; }
        public EventDisplayData(ScheduleEvent raw) { Raw = raw; }
    }

    public class TabViewModel
    {
        public string Header { get; set; }
        public ObservableCollection<CardViewModel> Cards { get; set; }
        public ICollectionView SortedCards { get; set; }

        public TabViewModel(string header)
        {
            Header = header;
            Cards = new ObservableCollection<CardViewModel>();
            SortedCards = CollectionViewSource.GetDefaultView(Cards);
            SortedCards.SortDescriptions.Add(new SortDescription(nameof(CardViewModel.IsActive), ListSortDirection.Descending));
            SortedCards.SortDescriptions.Add(new SortDescription(nameof(CardViewModel.TargetTime), ListSortDirection.Ascending));

            var liveView = (ICollectionViewLiveShaping)SortedCards;
            if (liveView.CanChangeLiveSorting) { liveView.IsLiveSorting = true; liveView.LiveSortingProperties.Add(nameof(CardViewModel.IsActive)); liveView.LiveSortingProperties.Add(nameof(CardViewModel.TargetTime)); }
        }
    }

    public class CardViewModel : INotifyPropertyChanged
    {
        public ScheduleEvent RawData;
        public event Action<string, string>? RequestNotification;
        public string Title => MainWindow.GetTrans(RawData.name ?? "", "TABS");
        public string Map => MainWindow.GetTrans(RawData.map ?? "", "MAPS");
        public string AlertLabel => MainWindow.GetTrans("alert_button_label", "UI");
        public ImageSource? BackgroundImage { get; private set; }

        private bool _isActive = false;
        public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } } }

        private DateTime _targetTime = DateTime.MaxValue;
        public DateTime TargetTime { get => _targetTime; set { if (_targetTime != value) { _targetTime = value; OnPropertyChanged(nameof(TargetTime)); } } }

        private string _timerText = "--:--";
        public string TimerText { get => _timerText; set { if (_timerText != value) { _timerText = value; OnPropertyChanged(nameof(TimerText)); } } }

        private string _timerPrefix = "";
        public string TimerPrefix { get => _timerPrefix; set { if (_timerPrefix != value) { _timerPrefix = value; OnPropertyChanged(nameof(TimerPrefix)); } } }

        private string _localTimeText = "";
        public string LocalTimeText { get => _localTimeText; set { if (_localTimeText != value) { _localTimeText = value; OnPropertyChanged(nameof(LocalTimeText)); } } }

        private System.Windows.Media.Brush _timerColor = System.Windows.Media.Brushes.White;
        public System.Windows.Media.Brush TimerColor { get => _timerColor; set { if (_timerColor != value) { _timerColor = value; OnPropertyChanged(nameof(TimerColor)); } } }

        private System.Windows.Media.Brush _borderColor = System.Windows.Media.Brushes.Transparent;
        public System.Windows.Media.Brush BorderColor { get => _borderColor; set { if (_borderColor != value) { _borderColor = value; OnPropertyChanged(nameof(BorderColor)); } } }

        private bool _isAlertEnabled = false;
        public bool IsAlertEnabled { get => _isAlertEnabled; set { _isAlertEnabled = value; OnPropertyChanged(nameof(IsAlertEnabled)); if (!value) HasNotified = false; } }

        private Visibility _alertVisibility = Visibility.Visible;
        public Visibility AlertVisibility { get => _alertVisibility; set { if (_alertVisibility != value) { _alertVisibility = value; OnPropertyChanged(nameof(AlertVisibility)); } } }

        private double _localTimeFontSize = 20;
        public double LocalTimeFontSize { get => _localTimeFontSize; set { if (_localTimeFontSize != value) { _localTimeFontSize = value; OnPropertyChanged(nameof(LocalTimeFontSize)); } } }

        private bool HasNotified = false;

        public CardViewModel(ScheduleEvent data)
        {
            RawData = data;
            BorderColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60));
            LoadImage();
            UpdateTimer();
        }

        public void UpdateData(ScheduleEvent newData)
        {
            if (RawData.startTime != newData.startTime || RawData.endTime != newData.endTime)
            {
                RawData = newData;
                HasNotified = false;
                UpdateTimer();
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Map));
            }
        }

        private void LoadImage()
        {
            string mapName = RawData.map ?? "";
            string imgFile = "Barrage.png";
            if (mapName.Contains("Dam")) imgFile = "Barrage.png";
            else if (mapName.Contains("Spaceport")) imgFile = "Port_spatial.png";
            else if (mapName.Contains("Buried")) imgFile = "Ville_enfouie.png";
            else if (mapName.Contains("Gate")) imgFile = "Portail_bleu.png";
            else if (mapName.Contains("Stella")) imgFile = "Stella_montis.png";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", imgFile);
            if (File.Exists(path)) { try { BackgroundImage = new BitmapImage(new Uri(path)); } catch { } }
        }

        public void UpdateTimer()
        {
            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            bool isActive = (nowUnix >= RawData.startTime && nowUnix < RawData.endTime);
            IsActive = isActive;

            AlertVisibility = IsActive ? Visibility.Collapsed : Visibility.Visible;

            string startTxt = MainWindow.GetTrans("timer_start_prefix", "UI");
            string endTxt = MainWindow.GetTrans("timer_end_prefix", "UI");

            if (startTxt == "TIMER_START_PREFIX") startTxt = "STARTS IN";
            if (endTxt == "TIMER_END_PREFIX") endTxt = "ENDS IN";

            TimeSpan diff;

            if (isActive)
            {
                long diffMs = RawData.endTime - nowUnix;
                diff = TimeSpan.FromMilliseconds(diffMs);
                TargetTime = DateTimeOffset.FromUnixTimeMilliseconds(RawData.endTime).LocalDateTime;

                TimerPrefix = endTxt;
                TimerColor = System.Windows.Media.Brushes.OrangeRed;
                BorderColor = System.Windows.Media.Brushes.OrangeRed;
                IsAlertEnabled = false;
                LocalTimeText = "";
            }
            else
            {
                long diffMs = RawData.startTime - nowUnix;
                diff = TimeSpan.FromMilliseconds(diffMs);
                TargetTime = DateTimeOffset.FromUnixTimeMilliseconds(RawData.startTime).LocalDateTime;

                TimerPrefix = startTxt;

                if (MainWindow.ShowLocalTime)
                    LocalTimeText = TargetTime.ToString("HH:mm");
                else
                    LocalTimeText = "";

                if (diff.TotalSeconds <= MainWindow.NotifySeconds && diff.TotalSeconds > 0)
                {
                    TimerColor = System.Windows.Media.Brushes.Yellow;
                    BorderColor = System.Windows.Media.Brushes.Yellow;

                    if (IsAlertEnabled && !HasNotified)
                    {
                        string msgPattern = MainWindow.GetTrans("notify_message", "UI");
                        if (string.IsNullOrEmpty(msgPattern) || msgPattern == "NOTIFY_MESSAGE")
                            msgPattern = "STARTING IN {minutes} MIN - {map_name}";

                        string msg = msgPattern.Replace("{minutes}", ((int)diff.TotalMinutes).ToString())
                                               .Replace("{map_name}", Map);

                        RequestNotification?.Invoke(Title, msg);
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

            if (diff.TotalHours >= 1)
                TimerText = $"{(int)diff.TotalHours}h {diff.Minutes}m";
            else if (diff.TotalSeconds > 0)
                TimerText = $"{diff.Minutes:D2}:{diff.Seconds:D2}";
            else
                TimerText = "00:00";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}