using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ARC_Sight
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            HotkeyBox.Text = MainWindow.Hotkey;
            NotifyBox.Text = (MainWindow.NotifySeconds / 60).ToString();
            SoundCheck.IsChecked = MainWindow.SoundEnabled;
            TimeCheck.IsChecked = MainWindow.ShowLocalTime;
            LoadLanguages();
            ApplyTranslations();
        }

        private void ApplyTranslations()
        {
            TitleBlock.Text = MainWindow.GetTrans("header", "SETTINGS");
            HotkeyLabel.Text = MainWindow.GetTrans("hotkey_label", "SETTINGS");
            AlertLabel.Text = MainWindow.GetTrans("alert_minutes_label", "SETTINGS");
            LangLabel.Text = MainWindow.GetTrans("language_label", "SETTINGS");
            SoundCheck.Content = MainWindow.GetTrans("sound_toggle", "SETTINGS");
            TimeCheck.Content = MainWindow.GetTrans("show_local_time", "SETTINGS");
            SaveBtn.Content = MainWindow.GetTrans("save_button", "SETTINGS");
            CancelBtn.Content = MainWindow.GetTrans("cancel_button", "SETTINGS");
            AboutHeader.Text = MainWindow.GetTrans("about_header", "SETTINGS");
            PatchNotesBtn.Content = MainWindow.GetTrans("patch_notes_button", "SETTINGS");
            if (string.IsNullOrEmpty(PatchNotesBtn.Content?.ToString())) PatchNotesBtn.Content = "View Patch Notes";
            string createdTxt = MainWindow.GetTrans("created_by", "SETTINGS");
            string appAuthor = createdTxt.Replace("**{author}**", "rodafux").Replace("{author}", "rodafux");
            string transByLabel = MainWindow.GetTrans("translated_by", "SETTINGS");
            if (string.IsNullOrEmpty(transByLabel) || transByLabel == "TRANSLATED_BY") transByLabel = "Translated by:";
            CreatedBy.Text = $"{appAuthor}\n{transByLabel} {MainWindow.CurrentLanguageAuthor}";
            string versionTxt = MainWindow.GetTrans("current_version", "SETTINGS");
            CurrentVersion.Text = versionTxt.Replace("{version}", MainWindow.AppVersion);
            string apiTxt = MainWindow.GetTrans("api_source_label", "SETTINGS");
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
            Key k = (e.Key == Key.System ? e.SystemKey : e.Key);
            if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
            HotkeyBox.Text = k.ToString();
        }

        private async void PatchNotes_Click(object s, RoutedEventArgs e) { if (System.Windows.Application.Current.MainWindow is MainWindow mw) await mw.FetchAndShowChangelogData(MainWindow.AppVersion); }
        private void Save_Click(object s, RoutedEventArgs e) { MainWindow.Hotkey = HotkeyBox.Text; if (int.TryParse(NotifyBox.Text, out int min)) MainWindow.NotifySeconds = min * 60; MainWindow.SoundEnabled = SoundCheck.IsChecked ?? true; MainWindow.ShowLocalTime = TimeCheck.IsChecked ?? false; if (LangCombo.SelectedItem is ComboBoxItem i) MainWindow.CurrentLanguage = i.Tag?.ToString() ?? "en"; MainWindow.SaveConfig(); MainWindow.LoadLanguage(); this.DialogResult = true; this.Close(); }
        private void Cancel_Click(object s, RoutedEventArgs e) { this.DialogResult = false; this.Close(); }
    }
}