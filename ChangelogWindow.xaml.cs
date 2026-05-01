using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ARC_Sight
{
    public partial class ChangelogWindow : Window
    {
        public ChangelogWindow(string notes)
        {
            InitializeComponent();
            TitleBlock.Text = MainWindow.GetTrans("changelog_header", "UI");
            CloseBtn.Content = MainWindow.GetTrans("close_btn", "UI");

            if (string.IsNullOrEmpty(TitleBlock.Text)) TitleBlock.Text = "PATCH NOTES";
            if (string.IsNullOrEmpty(CloseBtn.Content?.ToString())) CloseBtn.Content = "CLOSE";

            RenderMarkdown(notes);
        }

        private void RenderMarkdown(string markdown)
        {
            FlowDocument doc = DocViewer.Document;
            doc.Blocks.Clear();

            if (string.IsNullOrWhiteSpace(markdown)) return;

            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            System.Windows.Documents.List? list = null;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("#"))
                {
                    list = null;
                    Paragraph p = new Paragraph { Margin = new Thickness(0, 10, 0, 5) };
                    string cleanHeader = trimmed.TrimStart('#').Trim().Replace("**", "");

                    p.Inlines.Add(new Run(cleanHeader)
                    {

                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 0)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 16
                    });
                    doc.Blocks.Add(p);
                }
                else if (trimmed.StartsWith("* ") || trimmed.StartsWith("- "))
                {
                    if (list == null)
                    {
                        list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(5, 0, 0, 5) };
                        doc.Blocks.Add(list);
                    }

                    string content = trimmed.Substring(2);
                    ListItem li = new ListItem();
                    Paragraph p = new Paragraph();
                    ParseInlineFormatting(p, content);
                    li.Blocks.Add(p);
                    list.ListItems.Add(li);
                }
                else if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    list = null;
                    Paragraph p = new Paragraph { Margin = new Thickness(0, 0, 0, 5) };
                    ParseInlineFormatting(p, trimmed);
                    doc.Blocks.Add(p);
                }
                else { list = null; }
            }
        }

        private void ParseInlineFormatting(Paragraph p, string text)
        {
            string pattern = @"(\*\*.*?\*\*)";
            string[] parts = Regex.Split(text, pattern);

            foreach (var part in parts)
            {
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                {
                    string clean = part.Substring(2, part.Length - 4);

                    p.Inlines.Add(new Run(clean) { FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White });
                }
                else
                {
                    p.Inlines.Add(new Run(part));
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}