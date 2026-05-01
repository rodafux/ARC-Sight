#pragma warning disable CA1416

using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace ARC_Sight
{
    public partial class TwitchPlayerWindow : Window
    {
        public string StreamerName { get; set; }
        public bool IsLocked { get; private set; } = false;

        public TwitchPlayerWindow(string streamerName)
        {
            InitializeComponent();
            StreamerName = streamerName;
            this.DataContext = this;
            ApplyTranslations();
            InitializeAsync();
        }

        private void ApplyTranslations()
        {
            string liveFormat = MainWindow.GetTrans("twitch_live_format", "UI");
            if (string.IsNullOrEmpty(liveFormat) || liveFormat == "TWITCH_LIVE_FORMAT") liveFormat = "Live: {0}";
            StreamerTitleText.Text = liveFormat.Replace("{0}", StreamerName);

            string tooltip = MainWindow.GetTrans("twitch_lock_tooltip", "UI");
            if (string.IsNullOrEmpty(tooltip) || tooltip == "TWITCH_LOCK_TOOLTIP") tooltip = "Keep player visible when overlay is hidden";
            LockBtn.ToolTip = tooltip;
        }

        private async void InitializeAsync()
        {
            await Browser.EnsureCoreWebView2Async(null);
            Browser.CoreWebView2.Navigate($"https://player.twitch.tv/?channel={StreamerName}&parent=twitch.tv");
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            IsLocked = !IsLocked;
            LockBtn.Content = IsLocked ? "🔒" : "🔓";
            LockBtn.Foreground = IsLocked ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 0)) : System.Windows.Media.Brushes.White;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}