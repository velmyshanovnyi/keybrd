using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Keybrd.Core
{
    /// <summary>Світла / темна / системна тема, темний заголовок і фон Mica.</summary>
    public static class ThemeManager
    {
        private const string LightUri = "pack://application:,,,/Keybrd;component/Themes/Light.xaml";
        private const string DarkUri = "pack://application:,,,/Keybrd;component/Themes/Dark.xaml";

        public static bool IsDark { get; private set; }
        public static event Action Changed;

        public static void Init()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Apply();
        }

        public static void Shutdown() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Змінилась системна тема або системний акцентний колір.
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color) return;
            if (AppConfig.Current.Theme != "system" && AppConfig.Current.Accent != "system") return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
        }

        /// <summary>Встановлює тему: "light", "dark" або "system".</summary>
        public static void Set(string theme)
        {
            if (AppConfig.Current.Theme == theme) return;
            AppConfig.Current.Theme = theme;
            AppConfig.Current.Save();
            Apply();
        }

        public static void SetAccent(string id)
        {
            if (AppConfig.Current.Accent == id) return;
            AppConfig.Current.Accent = id;
            AppConfig.Current.Save();
            Apply();
        }

        /// <summary>Ctrl+T і кнопка в заголовку: перемикає на протилежну до поточної.</summary>
        public static void Toggle() => Set(IsDark ? "light" : "dark");

        public static void Apply()
        {
            var t = AppConfig.Current.Theme;
            bool dark = t == "dark" || (t == "system" && SystemIsDark());
            IsDark = dark;

            var dicts = Application.Current.Resources.MergedDictionaries;
            dicts[0] = new ResourceDictionary { Source = new Uri(dark ? DarkUri : LightUri) };
            ApplyAccent(dark);

            foreach (Window w in Application.Current.Windows)
                ApplyToWindow(w);

            Changed?.Invoke();
        }

        // ---------- акцентний колір ----------

        /// <summary>id, назва, колір для світлої теми, колір для темної.</summary>
        public static readonly (string Id, string Name, string Light, string Dark)[] Accents =
        {
            ("blue", "Синій", "#0067C0", "#4CC2FF"),
            ("teal", "Бірюзовий", "#0B7A75", "#4FD1C5"),
            ("green", "Зелений", "#107C10", "#6CCB5F"),
            ("purple", "Фіолетовий", "#6B4FBB", "#B9A5FF"),
            ("orange", "Помаранчевий", "#C04F15", "#FFA06B"),
            ("pink", "Рожевий", "#BF0077", "#FF8CC6"),
        };

        public static Color AccentColor(string id, bool dark)
        {
            if (id == "system" && TrySystemAccent(out var sys))
            {
                // Світлий варіант для темної теми й темніший — для світлої, щоб текст на ньому читався.
                return dark ? Mix(sys, Colors.White, Luma(sys) < 0.55 ? 0.45 : 0.1) : Mix(sys, Colors.Black, Luma(sys) > 0.45 ? 0.35 : 0);
            }
            foreach (var a in Accents)
                if (a.Id == id) return (Color)ColorConverter.ConvertFromString(dark ? a.Dark : a.Light);
            return (Color)ColorConverter.ConvertFromString(dark ? Accents[0].Dark : Accents[0].Light);
        }

        private static void ApplyAccent(bool dark)
        {
            var c = AccentColor(AppConfig.Current.Accent, dark);
            var res = Application.Current.Resources;
            res["AccentBrush"] = Frozen(c);
            res["AccentFgBrush"] = Frozen(Luma(c) > 0.55 ? Colors.Black : Colors.White);
        }

        private static bool TrySystemAccent(out Color c)
        {
            c = default;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (!(k?.GetValue("AccentColor") is int abgr)) return false;
                    c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                    return true;
                }
            }
            catch { return false; }
        }

        private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

        private static Color Mix(Color a, Color b, double t)
            => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private static SolidColorBrush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        // ---------- вікна ----------

        public static bool MicaSupported => OsInfo.Build >= 22621;
        public static bool UseMica => AppConfig.Current.UseMica && MicaSupported;

        /// <summary>Створює WindowChrome для вікна з власним заголовком. Викликати в конструкторі вікна.</summary>
        public static void AttachChrome(Window w)
        {
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                CaptionHeight = 32,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
                // -1: Mica заповнює все вікно; 1px знизу — щоб Windows 10 малювала тінь вікна.
                GlassFrameThickness = UseMica ? new Thickness(-1) : new Thickness(0, 0, 0, 1),
            });
        }

        /// <summary>Темний режим DWM (впливає на відтінок Mica і системне меню) та сам фон.</summary>
        public static void ApplyToWindow(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

            if (OsInfo.Build >= 22000)
            {
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }

            if (UseMica)
            {
                int backdrop = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                w.Background = Brushes.Transparent;
                var src = HwndSource.FromHwnd(hwnd);
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            else
            {
                w.SetResourceReference(Window.BackgroundProperty, "WindowBgBrush");
            }
        }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_MAINWINDOW = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
