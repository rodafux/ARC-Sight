using System;
using System.Threading;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace ARC_Sight
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "ARC_Sight_SingleInstance_Mutex";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                Application.Current.Shutdown();
                return;
            }

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