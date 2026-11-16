using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Keybrd.Core;
using Keybrd.Pages;

namespace Keybrd
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, UserControl> pages = new Dictionary<string, UserControl>();
        private readonly RadioButton[] tabs;

        public MainWindow()
        {
            InitializeComponent();
            ThemeManager.AttachChrome(this);
            tabs = new[] { TabTraining, TabLessons, TabStats, TabSettings };

            RestorePlacement();

            SourceInitialized += (s, e) =>
            {
                ThemeManager.ApplyToWindow(this);
                KeyInput.Attach();
            };
            Deactivated += (s, e) => KeyInput.RaiseReleaseAll();
            StateChanged += (s, e) => UpdateMaximizedState();
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
            ThemeManager.Changed += UpdateThemeButton;

            UpdateThemeButton();
            TabTraining.IsChecked = true;
            ShowStartupNotices();
        }

        // ---------- сторінки ----------

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            var key = (string)((FrameworkElement)sender).Tag;
            if (!pages.TryGetValue(key, out var page))
            {
                switch (key)
                {
                    case "training": page = new TrainingPage(); break;
                    case "lessons": page = new LessonsPage(); break;
                    case "stats": page = new StatsPage(); break;
                    default: page = new SettingsPage(); break;
                }
                pages[key] = page;
            }
            PageHost.Content = page;
        }

        public void ShowTraining() => TabTraining.IsChecked = true;

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // У полі введення (свій текст) клавіші належать полю.
            if (Keyboard.FocusedElement is TextBox && e.Key != Key.Tab) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!ctrl)
            {
                // На тренуванні Tab, пробіл і Enter — це символи вправи, а не навігація по кнопках.
                if (PageHost.Content is TrainingPage && (e.Key == Key.Tab || e.Key == Key.Space || e.Key == Key.Enter))
                    e.Handled = true;
                return;
            }

            if (e.Key == Key.F)
            {
                AppConfig.Update(c => c.ShowFingerColors = !c.ShowFingerColors);
                e.Handled = true;
            }
            else if (e.Key == Key.K)
            {
                AppConfig.Update(c => c.ShowAltLetters = !c.ShowAltLetters);
                e.Handled = true;
            }
            else if (e.Key == Key.T)
            {
                ThemeManager.Toggle();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                bool back = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                int i = Array.FindIndex(tabs, t => t.IsChecked == true);
                tabs[(i + (back ? tabs.Length - 1 : 1)) % tabs.Length].IsChecked = true;
                e.Handled = true;
            }
            else if (e.Key == Key.L)
            {
                AppConfig.Update(c => c.Layout = KeyboardLayout.Other(c.Layout));
                e.Handled = true;
            }
        }

        // ---------- повідомлення ----------

        private void ShowStartupNotices()
        {
            var notes = new List<string>();
            if (AppConfig.WasReset)
                notes.Add("Файл config.json був пошкоджений, тому налаштування скинуто. Стару копію збережено як config.bak.json.");
            if (StatsStore.WasReset)
                notes.Add("Файл stats.json був пошкоджений, тому статистику почато заново. Стару копію збережено як stats.bak.json.");
            if (DataStore.UsingFallback)
                notes.Add($"У папку з програмою не можна записувати, тому налаштування й статистика зберігаються в {DataStore.Dir}.");
            if (notes.Count > 0)
            {
                InfoText.Text = string.Join("\n", notes);
                InfoBar.Visibility = Visibility.Visible;
            }
            CheckForUpdate();
        }

        /// <summary>У фоні питаємо GitHub про нову версію; якщо вона є — показуємо смужку з кнопкою.</summary>
        private void CheckForUpdate()
        {
            if (!AppConfig.Current.CheckUpdates) return;
            Updater.Check(found =>
            {
                if (found == null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string text = $"Є нова версія тренажера: {found} (у вас {Updater.Current}). Завантажте новий Keybrd.exe і замініть ним цей файл — налаштування й статистика збережуться.";
                    InfoText.Text = InfoBar.Visibility == Visibility.Visible ? InfoText.Text + "\n" + text : text;
                    InfoAction.Content = "Завантажити";
                    InfoAction.Visibility = Visibility.Visible;
                    InfoBar.Visibility = Visibility.Visible;
                }));
            });
        }

        private void InfoAction_Click(object sender, RoutedEventArgs e) => Updater.OpenDownload();

        private void InfoClose_Click(object sender, RoutedEventArgs e) => InfoBar.Visibility = Visibility.Collapsed;

        // ---------- заголовок вікна ----------

        private void UpdateThemeButton()
        {
            // Показуємо ту тему, на яку перемкнемо.
            ThemeButton.Content = ThemeManager.IsDark ? "" : "";
            ThemeButton.ToolTip = ThemeManager.IsDark ? "Світла тема (Ctrl+T)" : "Темна тема (Ctrl+T)";
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateMaximizedState()
        {
            bool max = WindowState == WindowState.Maximized;
            MaxButton.Content = max ? "" : "";
            MaxButton.ToolTip = max ? "Відновити" : "Розгорнути";

            // Розгорнуте вікно з власним заголовком виходить за край екрана на товщину рамки.
            if (max)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double fx = (OsInfo.GetSystemMetrics(OsInfo.SM_CXSIZEFRAME) + OsInfo.GetSystemMetrics(OsInfo.SM_CXPADDEDBORDER)) / scale;
                double fy = (OsInfo.GetSystemMetrics(OsInfo.SM_CYSIZEFRAME) + OsInfo.GetSystemMetrics(OsInfo.SM_CXPADDEDBORDER)) / scale;
                Root.Margin = new Thickness(fx, fy, fx, fy);
            }
            else
            {
                Root.Margin = new Thickness(0);
            }
        }

        // ---------- положення вікна ----------

        private void RestorePlacement()
        {
            var c = AppConfig.Current;
            Width = c.WinW;
            Height = c.WinH;

            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            bool hasPos = !double.IsNaN(c.WinX) && !double.IsNaN(c.WinY)
                          && screen.IntersectsWith(new Rect(c.WinX, c.WinY, Math.Min(c.WinW, 200), 40));

            if (hasPos)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = c.WinX;
                Top = c.WinY;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (c.WinMaximized) WindowState = WindowState.Maximized;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            (PageHost.Content as TrainingPage)?.SaveUnfinished();
            var c = AppConfig.Current;
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!r.IsEmpty)
            {
                c.WinX = r.Left; c.WinY = r.Top; c.WinW = r.Width; c.WinH = r.Height;
            }
            c.WinMaximized = WindowState == WindowState.Maximized;
            c.Save();
            ThemeManager.Changed -= UpdateThemeButton;
        }
    }
}
