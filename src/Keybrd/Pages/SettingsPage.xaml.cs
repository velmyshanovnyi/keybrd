using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Keybrd.Core;

namespace Keybrd.Pages
{
    public partial class SettingsPage : UserControl
    {
        // true до першої синхронізації: під час InitializeComponent повзунки «проходять» через Minimum
        // і викликають ValueChanged — це не має потрапити в config.json.
        private bool syncing = true;

        public SettingsPage()
        {
            InitializeComponent();
            DataPath.Text = DataStore.Dir;
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Тренажер клавіатури {v.Major}.{v.Minor}.{v.Build} · працює без інтернету";
            BuildAccents();
            Loaded += (s, e) => ShowVoiceStatus();
            Sync();
            Loaded += (s, e) =>
            {
                ThemeManager.Changed += Sync;
                AppConfig.Changed += Sync;
                Sync();
            };
            Unloaded += (s, e) =>
            {
                ThemeManager.Changed -= Sync;
                AppConfig.Changed -= Sync;
            };
        }

        // ---------- акцентні кольори ----------

        private void BuildAccents()
        {
            AccentPanel.Children.Clear();
            foreach (var a in ThemeManager.Accents) AddSwatch(a.Id, a.Name);
            AddSwatch("system", "Як у Windows");
        }

        private void AddSwatch(string id, string name)
        {
            var rb = new RadioButton { GroupName = "Accent", Tag = id, ToolTip = name };
            rb.SetResourceReference(StyleProperty, "SwatchRadio");
            rb.Checked += (s, e) => { if (!syncing) ThemeManager.SetAccent(id); };
            AccentPanel.Children.Add(rb);
        }

        private void PaintSwatches()
        {
            foreach (RadioButton rb in AccentPanel.Children)
            {
                var id = (string)rb.Tag;
                var b = new SolidColorBrush(ThemeManager.AccentColor(id, ThemeManager.IsDark));
                b.Freeze();
                rb.Background = b;
                rb.IsChecked = id == AppConfig.Current.Accent;
            }
            foreach (var a in ThemeManager.Accents)
                if (a.Id == AppConfig.Current.Accent) AccentName.Text = a.Name;
            if (AppConfig.Current.Accent == "system") AccentName.Text = "Як у Windows (колір з налаштувань персоналізації)";
        }

        // ---------- синхронізація ----------

        private void Sync()
        {
            var c = AppConfig.Current;
            syncing = true;
            switch (c.Theme)
            {
                case "light": ThemeLight.IsChecked = true; break;
                case "dark": ThemeDark.IsChecked = true; break;
                default: ThemeSystem.IsChecked = true; break;
            }
            PaintSwatches();
            FontSizeSlider.Value = c.TextFontSize;
            FontSizeValue.Text = $"{c.TextFontSize:0} пт";
            FontPreview.FontSize = c.TextFontSize;

            (c.KeyboardStyle == "laptop" ? StyleLaptop : StyleStandard).IsChecked = true;
            FingerColors.IsChecked = c.ShowFingerColors;
            AltLetters.IsChecked = c.ShowAltLetters;
            FnRow.IsChecked = c.ShowFnRow;
            FocusMode.IsChecked = c.FocusMode;
            SingleLine.IsChecked = c.SingleLine;
            AutoHide.IsChecked = c.AutoHideKeyboard;
            Eggs.IsChecked = c.Eggs;
            VoiceToggle.IsChecked = c.Voice;
            VoiceRateSlider.Value = c.VoiceRate;
            VoiceRateValue.Text = c.VoiceRate == 0 ? "звичайна" : c.VoiceRate > 0 ? $"швидше +{c.VoiceRate}" : $"повільніше {c.VoiceRate}";
            foreach (var rb in new[] { Break0, Break5, Break10, Break15, Break20, Break30 })
                if ((string)rb.Tag == c.BreakMinutes.ToString()) rb.IsChecked = true;

            (c.SpeedUnit == "wpm" ? UnitWpm : UnitCpm).IsChecked = true;
            AccuracySlider.Value = c.UnlockAccuracy;
            AccuracyValue.Text = $"{c.UnlockAccuracy:0}%";
            UnlockAll.IsChecked = c.UnlockAll;
            Sound.IsChecked = c.Sound;

            StrictShift.IsChecked = c.StrictShift;
            FixMode.IsChecked = c.FixMode;
            Metronome.IsChecked = c.Metronome;
            Pacer.IsChecked = c.Pacer;
            TargetSlider.Value = c.TargetCpm;
            TargetValue.Text = $"{TypingSession.FormatSpeed(c.TargetCpm, c.SpeedUnit)} {TypingSession.SpeedLabel(c.SpeedUnit)}";
            foreach (var rb in new[] { Goal5, Goal10, Goal15, Goal20, Goal30 })
                if ((string)rb.Tag == c.DailyGoal.ToString()) rb.IsChecked = true;
            foreach (var rb in new[] { Exam1, Exam3, Exam5 })
                if ((string)rb.Tag == c.ExamMinutes.ToString()) rb.IsChecked = true;
            syncing = false;
        }

        // ---------- обробники ----------

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            ThemeManager.Set((string)((FrameworkElement)sender).Tag);
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            bool playSound = sender == Sound && Sound.IsChecked == true;
            bool playVoice = sender == VoiceToggle && VoiceToggle.IsChecked == true;
            AppConfig.Update(c =>
            {
                c.KeyboardStyle = StyleLaptop.IsChecked == true ? "laptop" : "standard";
                c.ShowFingerColors = FingerColors.IsChecked == true;
                c.ShowAltLetters = AltLetters.IsChecked == true;
                c.ShowFnRow = FnRow.IsChecked == true;
                c.FocusMode = FocusMode.IsChecked == true;
                c.SingleLine = SingleLine.IsChecked == true;
                c.AutoHideKeyboard = AutoHide.IsChecked == true;
                c.Eggs = Eggs.IsChecked == true;
                if (sender == Eggs && c.Eggs) c.AutoHideKeyboard = true;   // пасхалки видно лише на місці схованої клавіатури
                c.Voice = VoiceToggle.IsChecked == true;
                c.SpeedUnit = UnitWpm.IsChecked == true ? "wpm" : "cpm";
                c.UnlockAll = UnlockAll.IsChecked == true;
                c.Sound = Sound.IsChecked == true;
                c.StrictShift = StrictShift.IsChecked == true;
                c.FixMode = FixMode.IsChecked == true;
                c.Metronome = Metronome.IsChecked == true;
                c.Pacer = Pacer.IsChecked == true;
            });
            if (playSound) Sounds.Error(); // одразу дати почути, як він звучить
            if (playVoice) Speech.Say(AppConfig.Current.Layout == "en" ? "Voice is on." : "Озвучення увімкнено.", AppConfig.Current.Layout);
        }

        private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || FontSizeValue == null) return;
            double v = System.Math.Round(e.NewValue);
            AppConfig.Update(c => c.TextFontSize = v);
        }

        private void Target_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || TargetValue == null) return;
            int v = (int)(System.Math.Round(e.NewValue / 10) * 10);
            AppConfig.Update(c => c.TargetCpm = v);
        }

        private void Goal_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            int minutes = int.Parse((string)((FrameworkElement)sender).Tag);
            AppConfig.Update(c => c.DailyGoal = minutes);
        }

        private void Exam_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            int minutes = int.Parse((string)((FrameworkElement)sender).Tag);
            AppConfig.Update(c => c.ExamMinutes = minutes);
        }

        private void Break_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            int minutes = int.Parse((string)((FrameworkElement)sender).Tag);
            AppConfig.Update(c => c.BreakMinutes = minutes);
        }

        private void VoiceRate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || VoiceRateValue == null) return;
            int v = (int)System.Math.Round(e.NewValue);
            AppConfig.Update(c => c.VoiceRate = v);
        }

        /// <summary>Які голоси знайдено у Windows і що робити, якщо українського немає.</summary>
        private void ShowVoiceStatus()
        {
            string uk = Speech.VoiceName("uk"), en = Speech.VoiceName("en");
            VoiceStatus.Text =
                "Українська: " + (uk ?? "голосу немає. Перевірте, чи є український голос у «Параметри → Час і мова → Мовлення», або встановіть сторонній голос SAPI 5, наприклад безкоштовний RHVoice.") +
                "\nАнглійська: " + (en ?? "голосу немає.");
        }

        private void Accuracy_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || AccuracyValue == null) return;
            double v = System.Math.Round(e.NewValue);
            AppConfig.Update(c => c.UnlockAccuracy = v);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", "\"" + DataStore.Dir + "\""); } catch { }
        }

        private void ResetStats_Click(object sender, RoutedEventArgs e)
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "Видалити всю статистику і прогрес уроків?\n\nПоточний файл буде збережено як stats.bak.json, тож його можна повернути вручну.",
                "Скинути статистику", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            StatsStore.Reset();
            Mastery.Reset();
            ResetInfo.Text = "Статистику скинуто. Копія — stats.bak.json.";
            ResetInfo.Visibility = Visibility.Visible;
        }
    }
}
