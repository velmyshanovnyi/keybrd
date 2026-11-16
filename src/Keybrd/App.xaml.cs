using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Keybrd.Core;

namespace Keybrd
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;

            AppConfig.Load();
            StatsStore.Load();
            Mastery.Load();
            ThemeManager.Init();

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ThemeManager.Shutdown();
            base.OnExit(e);
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var dir = AppConfig.DataDir ?? AppDomain.CurrentDomain.BaseDirectory;
                File.AppendAllText(Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
            }
            catch { /* нікуди писати — нічого не робимо */ }

            MessageBox.Show("Сталася неочікувана помилка. Подробиці записано в crash.log.\n\n" + e.Exception.Message,
                "Тренажер клавіатури", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
