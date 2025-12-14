using System.Windows;

namespace ARC_Sight
{
    public partial class ConfirmationWindow : Window
    {
        public ConfirmationWindow(string title, string message, string yesText, string noText)
        {
            InitializeComponent();
            TitleBlock.Text = title;
            MessageBlock.Text = message;
            YesBtn.Content = yesText;
            NoBtn.Content = noText;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}