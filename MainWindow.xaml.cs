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
using Velopack;
using Velopack.Sources;

namespace ARC_Sight
{
    public partial class MainWindow : Window
    {
        public static string AppVersion { get; } = "v1.2.1";

        private const string NOTE_URL = "https://raw.githubusercontent.com/rodafux/ARC-Sight/refs/heads/Default/msg.ini";
        private const string API_URL = "https://metaforge.app/api/arc-raiders/event-timers";
        private const string HEARTBEAT_URL = "https://arc-sight-stats-viewer.onrender.com/ping";
        private const string GITHUB_REPO_URL = "https://github.com/rodafux/ARC-Sight";

        public static string AppDataPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARC-Sight");
        public static string ConfigFile { get; } = Path.Combine(AppDataPath, "config.ini");
        public static string LanguagesDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "languages");

        private readonly HttpClient _client = new HttpClient();
        private DispatcherTimer? _uiTimer;
        private DispatcherTimer? _apiTimer;
        private IntPtr _windowHandle;

        private static MediaPlayer _mediaPlayer = new MediaPlayer();
        private Velopack.UpdateInfo? _updateInfo;

        public ObservableCollection<TabViewModel> Tabs { get; set; } = new ObservableCollection<TabViewModel>();

        public ImageSource? AppLogo { get; set; }

        public static string Hotkey { get; set; } = "F9";
        public static int NotifySeconds { get; set; } = 300;
        public static bool SoundEnabled { get; set; } = true;
        public static bool ShowLocalTime { get; set; } = false;
        public static string CurrentLanguage { get; set; } = "en";

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
                    request.Headers.Add("X-App-Secret", "ARC-RAIDERS-OPS");
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
                var content = await _client.GetStringAsync(NOTE_URL);

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

                        NoteText.Text = $"{header} {message}";
                        NoteText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        NoteText.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                NoteText.Visibility = Visibility.Collapsed;
            }
        }

        private async Task CheckForUpdates()
        {
            try
            {
#if DEBUG
                this.Dispatcher.Invoke(() => {
                    UpdateBtn.Content = GetTrans("update_available_button", "UI"); // AJOUTÉ : Traduction dynamique
                    UpdateBtn.Visibility = Visibility.Visible;
                    System.Diagnostics.Debug.WriteLine("DEBUG : Bouton UPDATE forcé avec traduction.");
                });
#else
        var mgr = new UpdateManager(new GithubSource(GITHUB_REPO_URL, null, false));
        var newVersion = await mgr.CheckForUpdatesAsync();

        if (newVersion != null)
        {
            _updateInfo = newVersion;
            this.Dispatcher.Invoke(() =>
            {
                UpdateBtn.Content = GetTrans("update_available_button", "UI"); // AJOUTÉ : Traduction dynamique
                UpdateBtn.Visibility = Visibility.Visible;
            });
        }
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update Check Error: {ex.Message}");
            }
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            MessageBox.Show("TEST : Détection réussie !");
#else
    if (_updateInfo == null) return;

    try
    {
        // Utilise les clés 'update_downloading' et 'update_installing' de votre fichier INI
        UpdateBtn.Content = GetTrans("update_downloading", "UI"); 
        UpdateBtn.IsEnabled = false;

        var mgr = new UpdateManager(new GithubSource(GITHUB_REPO_URL, null, false));
        await mgr.DownloadUpdatesAsync(_updateInfo);

        UpdateBtn.Content = GetTrans("update_installing", "UI");
        mgr.ApplyUpdatesAndRestart(_updateInfo);
    }
    catch (Exception ex)
    {
        UpdateBtn.Content = GetTrans("update_error", "UI");
        UpdateBtn.IsEnabled = true;
    }
#endif
        }

        public static void TriggerNotification(string title, string message)
        {
            if (SoundEnabled)
            {
                try
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Play();
                }
                catch { }
            }

            Application.Current.Dispatcher.Invoke(() => { try { new ToastWindow(title, message).Show(); } catch { } });
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Left = 0;
            this.Top = 0;
            this.Width = SystemParameters.PrimaryScreenWidth;

            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(HwndHook);
            RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, ev) => UpdateAllTimers();
            _uiTimer.Start();

            _apiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _apiTimer.Tick += async (s, ev) =>
            {
                await FetchData();
                await FetchNote();
            };
            _apiTimer.Start();

            _ = FetchData();
            _ = FetchNote();
            _ = StartHeartbeat();
            _ = CheckForUpdates();
        }

        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ListBox listBox && e.Delta != 0)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
                if (scrollViewer != null)
                {
                    if (e.Delta > 0) scrollViewer.LineLeft();
                    else scrollViewer.LineRight();
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
            try
            {
                StatusText.Text = "Updating...";
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("ARC-Sight/1.0");
                var json = await _client.GetStringAsync(API_URL);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<ApiResult>(json, options);

                if (result?.data != null)
                {
                    UpdateUiData(result.data);
                    StatusText.Text = "";
                }
            }
            catch
            {
                StatusText.Text = "API Error";
            }
        }

        private void UpdateUiData(List<EventData> data)
        {
            var allTab = Tabs.FirstOrDefault(t => t.Header == "ALL");
            if (allTab == null)
            {
                allTab = new TabViewModel("ALL");
                Tabs.Insert(0, allTab);
            }
            MergeCards(allTab.Cards, data);

            var grouped = data.GroupBy(e => e.name).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                string tabName = GetTrans(group.Key ?? "Unknown", "TABS");
                var tab = Tabs.FirstOrDefault(t => t.Header == tabName);
                if (tab == null) { tab = new TabViewModel(tabName); Tabs.Add(tab); }
                MergeCards(tab.Cards, group.ToList());
            }
        }

        private void MergeCards(ObservableCollection<CardViewModel> collection, List<EventData> newEvents)
        {
            foreach (var evt in newEvents)
            {
                var existing = collection.FirstOrDefault(c => c.RawData.name == evt.name && c.RawData.map == evt.map);
                if (existing != null) existing.RawData = evt;
                else
                {
                    var newCard = new CardViewModel(evt);
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
            string k = key.Replace(" ", "_").ToLower();
            return Translations.ContainsKey(k) ? Translations[k] : key.ToUpper();
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
                }
            }
        }

        public static void LoadLanguage()
        {
            Translations.Clear();
            string path = Path.Combine(LanguagesDir, $"lang_{CurrentLanguage}.ini");
            if (!File.Exists(path)) path = Path.Combine(LanguagesDir, "lang_en.ini");

            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Contains("="))
                    {
                        var p = line.Split(new[] { '=' }, 2);
                        if (p.Length > 1) Translations[p[0].Trim().ToLower()] = p[1].Trim();
                    }
                }
            }
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312;

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 1)
            {
                if (Visibility == Visibility.Visible) Hide(); else { Show(); Activate(); }
                handled = true;
            }
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
            SettingsWindow sw = new SettingsWindow();
            sw.Owner = this;
            if (sw.ShowDialog() == true)
            {
                UnregisterHotKey(_windowHandle, 1);
                RegisterHotKey(_windowHandle, 1, 0, GetVkCode(Hotkey));
                Tabs.Clear();
                _ = FetchData();
                _ = FetchNote();

                if (UpdateBtn.Visibility == Visibility.Visible && UpdateBtn.IsEnabled)
                {
                    UpdateBtn.Content = GetTrans("update_available_button", "UI");
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            string title = GetTrans("exit_confirm_title", "UI");
            string message = GetTrans("exit_confirm_msg", "UI");
            string yesText = GetTrans("yes_btn", "UI");
            string noText = GetTrans("no_btn", "UI");

            if (string.IsNullOrEmpty(yesText)) yesText = "YES";
            if (string.IsNullOrEmpty(noText)) noText = "NO";

            var dialog = new ConfirmationWindow(title, message, yesText, noText);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                Application.Current.Shutdown();
            }
        }
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
            if (liveView.CanChangeLiveSorting)
            {
                liveView.IsLiveSorting = true;
                liveView.LiveSortingProperties.Add(nameof(CardViewModel.IsActive));
                liveView.LiveSortingProperties.Add(nameof(CardViewModel.TargetTime));
            }
        }
    }

    public class CardViewModel : INotifyPropertyChanged
    {
        public EventData RawData;
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
        private Brush _timerColor = Brushes.White;
        public Brush TimerColor { get => _timerColor; set { if (_timerColor != value) { _timerColor = value; OnPropertyChanged(nameof(TimerColor)); } } }
        private Brush _borderColor = Brushes.Transparent;
        public Brush BorderColor { get => _borderColor; set { if (_borderColor != value) { _borderColor = value; OnPropertyChanged(nameof(BorderColor)); } } }
        private bool _isAlertEnabled = false;
        public bool IsAlertEnabled { get => _isAlertEnabled; set { _isAlertEnabled = value; OnPropertyChanged(nameof(IsAlertEnabled)); if (!value) HasNotified = false; } }
        private Visibility _alertVisibility = Visibility.Visible;
        public Visibility AlertVisibility { get => _alertVisibility; set { if (_alertVisibility != value) { _alertVisibility = value; OnPropertyChanged(nameof(AlertVisibility)); } } }
        private bool HasNotified = false;
        public CardViewModel(EventData data) { RawData = data; BorderColor = new SolidColorBrush(Color.FromRgb(60, 60, 60)); LoadImage(); UpdateTimer(); }
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
            if (RawData.times == null || RawData.times.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            DateTime? foundTarget = null;
            bool foundActive = false;
            DateTime? nextStart = null;
            var candidates = new List<DateTime>();
            foreach (var slot in RawData.times)
            {
                try
                {
                    if (string.IsNullOrEmpty(slot.start) || string.IsNullOrEmpty(slot.end)) continue;
                    var sParts = slot.start.Split(':').Select(int.Parse).ToArray();
                    var eParts = slot.end.Split(':').Select(int.Parse).ToArray();
                    DateTime tStart = now.Date.AddHours(sParts[0]).AddMinutes(sParts[1]);
                    DateTime tEnd = now.Date.AddHours(eParts[0]).AddMinutes(eParts[1]);
                    if (tEnd <= tStart) tEnd = tEnd.AddDays(1);
                    if (now >= tStart && now < tEnd) { foundActive = true; foundTarget = tEnd; break; }
                    DateTime tStartPrev = tStart.AddDays(-1);
                    DateTime tEndPrev = tEnd.AddDays(-1);
                    if (now >= tStartPrev && now < tEndPrev) { foundActive = true; foundTarget = tEndPrev; break; }
                    if (tStart > now) candidates.Add(tStart); else candidates.Add(tStart.AddDays(1));
                }
                catch { continue; }
            }
            IsActive = foundActive;
            AlertVisibility = IsActive ? Visibility.Collapsed : Visibility.Visible;
            string lang = MainWindow.CurrentLanguage;
            string startTxt = "STARTS IN"; string endTxt = "ENDS IN";
            if (lang == "fr") { startTxt = "DÉBUT DANS"; endTxt = "FIN DANS"; }
            else if (lang == "de") { startTxt = "START IN"; endTxt = "ENDET IN"; }
            else if (lang == "es") { startTxt = "INICIA EN"; endTxt = "TERMINA EN"; }
            else if (lang == "it") { startTxt = "INIZIA TRA"; endTxt = "TERMINA TRA"; }

            if (foundActive && foundTarget.HasValue)
            {
                TargetTime = foundTarget.Value; TimeSpan diff = foundTarget.Value - now;
                TimerText = $"{diff.Hours}h {diff.Minutes}m"; TimerPrefix = endTxt;
                TimerColor = Brushes.OrangeRed; BorderColor = Brushes.OrangeRed; IsAlertEnabled = false; LocalTimeText = "";
            }
            else if (candidates.Count > 0)
            {
                TargetTime = candidates.OrderBy(t => t).First(); nextStart = TargetTime;
                TimeSpan diff = TargetTime - now; TimerPrefix = startTxt;
                if (diff.TotalHours >= 1) TimerText = $"{diff.Hours}h {diff.Minutes}m";
                else TimerText = $"{diff.Minutes:D2}:{diff.Seconds:D2}";
                if (MainWindow.ShowLocalTime && nextStart.HasValue) LocalTimeText = nextStart.Value.ToLocalTime().ToString("HH:mm"); else LocalTimeText = "";
                if (diff.TotalSeconds <= MainWindow.NotifySeconds)
                {
                    TimerColor = Brushes.Yellow; BorderColor = Brushes.Yellow;
                    if (IsAlertEnabled && !HasNotified)
                    {
                        string msgPattern = MainWindow.GetTrans("notify_message", "UI");

                        if (string.IsNullOrEmpty(msgPattern)) msgPattern = "STARTING IN {minutes} MIN - {map_name}";

                        string msg = msgPattern
                            .Replace("{minutes}", ((int)diff.TotalMinutes).ToString())
                            .Replace("{map_name}", Map);

                        RequestNotification?.Invoke(Title, msg);
                        HasNotified = true;
                    }
                }
                else { TimerColor = Brushes.White; BorderColor = new SolidColorBrush(Color.FromRgb(60, 60, 60)); HasNotified = false; }
            }
            else { TargetTime = DateTime.MaxValue; TimerText = "--:--"; TimerPrefix = ""; TimerColor = Brushes.Gray; LocalTimeText = ""; }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ApiResult { public List<EventData>? data { get; set; } }
    public class EventData { public string? name { get; set; } public string? map { get; set; } public List<TimeSlot>? times { get; set; } }
    public class TimeSlot { public string? start { get; set; } public string? end { get; set; } }
}