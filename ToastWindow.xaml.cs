using System;
using System.Windows;
using System.Windows.Threading;

namespace ARC_Sight
{
    public partial class ToastWindow : Window
    {
        public ToastWindow(string title, string message)
        {
            InitializeComponent();
            
            TitleTxt.Text = title.ToUpper();
            MessageTxt.Text = message;

            var desktop = SystemParameters.WorkArea;
            this.Left = desktop.Right - this.Width - 20;
            this.Top = desktop.Top + 100;

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(6);
            timer.Tick += (s, e) => 
            { 
                timer.Stop(); 
                this.Close(); 
            };
            timer.Start();
        }
    }
}