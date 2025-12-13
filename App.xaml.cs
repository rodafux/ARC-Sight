using System.Windows;
using Velopack;
using Velopack.Sources;

namespace ARC_Sight
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            VelopackApp.Build().Run();

            base.OnStartup(e);
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (MainWindow == null)
            {
                new MainWindow().Show();
            }
        }
    }
}