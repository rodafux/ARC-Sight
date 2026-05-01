using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ARC_Sight
{
    public partial class SettingsWindow : Window
    {
        private readonly HttpClient _client = new HttpClient();

        public SettingsWindow()
        {
            InitializeComponent();
            HotkeyBox.Text = MainWindow.Hotkey;
            NotifyBox.Text = (MainWindow.NotifySeconds / 60).ToString();
            SoundCheck.IsChecked = MainWindow.SoundEnabled;
            TimeCheck.IsChecked = MainWindow.ShowLocalTime;
            TwitchUserBox.Text = MainWindow.TwitchUser;
            LoadLanguages();
            ApplyTranslations();
        }

        private async void VerifyTwitch_Click(object sender, RoutedEventArgs e)
        {
            string user = TwitchUserBox.Text.Trim();
            if (string.IsNullOrEmpty(user)) return;

            string loadTxt = MainWindow.GetTrans("verify_loading", "SETTINGS");
            VerifyBtn.Content = string.IsNullOrEmpty(loadTxt) || loadTxt == "VERIFY_LOADING" ? "..." : loadTxt;

            try
            {
                var res = await _client.GetStringAsync($"https://arbitrary-gertrudis-rodafux-fe0cc8aa.koyeb.app/verify-twitch/{user}");
                using (JsonDocument doc = JsonDocument.Parse(res))
                {
                    if (doc.RootElement.TryGetProperty("avatar", out var avatarUrl))
                    {
                        TwitchAvatarBrush.ImageSource = new BitmapImage(new Uri(avatarUrl.GetString()!));
                        TwitchAvatarCircle.Visibility = Visibility.Visible;

                        string okTxt = MainWindow.GetTrans("verify_success", "SETTINGS");
                        VerifyBtn.Content = string.IsNullOrEmpty(okTxt) || okTxt == "VERIFY_SUCCESS" ? "OK !" : okTxt;
                    }
                    else
                    {
                        string errTxt = MainWindow.GetTrans("verify_error", "SETTINGS");
                        VerifyBtn.Content = string.IsNullOrEmpty(errTxt) || errTxt == "VERIFY_ERROR" ? "ERROR" : errTxt;
                    }
                }
            }
            catch
            {
                string errTxt = MainWindow.GetTrans("verify_error", "SETTINGS");
                VerifyBtn.Content = string.IsNullOrEmpty(errTxt) || errTxt == "VERIFY_ERROR" ? "ERROR" : errTxt;
            }
        }

        private void ApplyTranslations()
        {
            TitleBlock.Text = MainWindow.GetTrans("header", "SETTINGS");
            HotkeyLabel.Text = MainWindow.GetTrans("hotkey_label", "SETTINGS");
            AlertLabel.Text = MainWindow.GetTrans("alert_minutes_label", "SETTINGS");
            LangLabel.Text = MainWindow.GetTrans("language_label", "SETTINGS");

            string twitchUsr = MainWindow.GetTrans("twitch_user_label", "SETTINGS");
            TwitchUserLabel.Text = string.IsNullOrEmpty(twitchUsr) || twitchUsr == "TWITCH_USER_LABEL" ? "Twitch Username:" : twitchUsr;

            string twitchHelp = MainWindow.GetTrans("twitch_help_text", "SETTINGS");
            TwitchHelpText.Text = string.IsNullOrEmpty(twitchHelp) || twitchHelp == "TWITCH_HELP_TEXT" ? "Once your username is saved, you will appear in the contributors list by clicking the Twitch logo on the overlay during your lives." : twitchHelp;

            string verifyBtnText = MainWindow.GetTrans("verify_button", "SETTINGS");
            VerifyBtn.Content = string.IsNullOrEmpty(verifyBtnText) || verifyBtnText == "VERIFY_BUTTON" ? "VERIFY" : verifyBtnText;

            SoundCheck.Content = MainWindow.GetTrans("sound_toggle", "SETTINGS");
            TimeCheck.Content = MainWindow.GetTrans("show_local_time", "SETTINGS");
            SaveBtn.Content = MainWindow.GetTrans("save_button", "SETTINGS");
            CancelBtn.Content = MainWindow.GetTrans("cancel_button", "SETTINGS");
            AboutHeader.Text = MainWindow.GetTrans("about_header", "SETTINGS");

            string patchTxt = MainWindow.GetTrans("patch_notes_button", "SETTINGS");
            PatchNotesBtn.Content = string.IsNullOrEmpty(patchTxt) || patchTxt == "PATCH_NOTES_BUTTON" ? "View Patch Notes" : patchTxt;

            string createdTxt = MainWindow.GetTrans("created_by", "SETTINGS");
            if (string.IsNullOrEmpty(createdTxt) || createdTxt == "CREATED_BY") createdTxt = "App created by **{author}**.";
            string appAuthor = createdTxt.Replace("**{author}**", "rodafux").Replace("{author}", "rodafux");

            string transByLabel = MainWindow.GetTrans("translated_by", "SETTINGS");
            if (string.IsNullOrEmpty(transByLabel) || transByLabel == "TRANSLATED_BY") transByLabel = "Translated by:";

            CreatedBy.Text = $"{appAuthor}\n{transByLabel} {MainWindow.CurrentLanguageAuthor}";

            string versionTxt = MainWindow.GetTrans("current_version", "SETTINGS");
            if (string.IsNullOrEmpty(versionTxt) || versionTxt == "CURRENT_VERSION") versionTxt = "Current Version: {version}";
            CurrentVersion.Text = versionTxt.Replace("{version}", MainWindow.AppVersion);

            string apiTxt = MainWindow.GetTrans("api_source_label", "SETTINGS");
            if (string.IsNullOrEmpty(apiTxt) || apiTxt == "API_SOURCE_LABEL") apiTxt = "API Source: {api_link}";
            ApiSource.Text = apiTxt.Replace("{api_link}", "metaforge.app");
        }

        private async void LoadLanguages()
        {
            LangCombo.Items.Clear();
            var languages = new Dictionary<string, string>();
            if (Directory.Exists(MainWindow.LanguagesDir))
            {
                foreach (var file in Directory.GetFiles(MainWindow.LanguagesDir, "lang_*.ini"))
                {
                    string code = Path.GetFileName(file).Replace("lang_", "").Replace(".ini", "");
                    languages[code] = GetLocalLanguageName(file, code);
                }
            }
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                var serverLangs = await mw.GetLanguagesMetadataFromServerAsync();
                foreach (var kvp in serverLangs) languages[kvp.Key] = kvp.Value;
            }
            foreach (var lang in languages.OrderBy(l => l.Value))
            {
                var item = new ComboBoxItem { Content = lang.Value, Tag = lang.Key };
                LangCombo.Items.Add(item);
                if (lang.Key == MainWindow.CurrentLanguage) item.IsSelected = true;
            }
        }

        private string GetLocalLanguageName(string path, string code)
        {
            try
            {
                foreach (var line in File.ReadAllLines(path))
                    if (line.ToLower().StartsWith("language_name")) return line.Split('=')[1].Trim();
            }
            catch { }
            return code.ToUpper();
        }

        private void HotkeyBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            System.Windows.Input.Key k = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key);
            if (k == System.Windows.Input.Key.LeftCtrl || k == System.Windows.Input.Key.RightCtrl || k == System.Windows.Input.Key.LeftAlt || k == System.Windows.Input.Key.RightAlt || k == System.Windows.Input.Key.LeftShift || k == System.Windows.Input.Key.RightShift || k == System.Windows.Input.Key.LWin || k == System.Windows.Input.Key.RWin) return;
            HotkeyBox.Text = k.ToString();
        }

        private async void PatchNotes_Click(object s, RoutedEventArgs e) { if (System.Windows.Application.Current.MainWindow is MainWindow mw) await mw.FetchAndShowChangelogData(MainWindow.AppVersion); }
        private void Save_Click(object s, RoutedEventArgs e) { MainWindow.Hotkey = HotkeyBox.Text; if (int.TryParse(NotifyBox.Text, out int min)) MainWindow.NotifySeconds = min * 60; MainWindow.SoundEnabled = SoundCheck.IsChecked ?? true; MainWindow.ShowLocalTime = TimeCheck.IsChecked ?? false; MainWindow.TwitchUser = TwitchUserBox.Text.Trim().ToLower(); if (LangCombo.SelectedItem is ComboBoxItem i) MainWindow.CurrentLanguage = i.Tag?.ToString() ?? "en"; MainWindow.SaveConfig(); MainWindow.LoadLanguage(); this.DialogResult = true; this.Close(); }
        private void Cancel_Click(object s, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }
}