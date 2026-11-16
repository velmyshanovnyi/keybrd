using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Keybrd.Core;

namespace Keybrd.Pages
{
    public partial class LessonsPage : UserControl
    {
        private Lesson selected;
        private bool syncing;
        private double coursePart;

        public LessonsPage()
        {
            InitializeComponent();
            SizeChanged += (s, e) => UpdateBar();
            Loaded += (s, e) =>
            {
                AppConfig.Changed += Refresh;
                StatsStore.Changed += Refresh;
                selected = null;
                Refresh();
            };
            Unloaded += (s, e) =>
            {
                AppConfig.Changed -= Refresh;
                StatsStore.Changed -= Refresh;
            };
        }

        private string Layout => AppConfig.Current.Layout;

        private void Refresh()
        {
            var c = AppConfig.Current;
            syncing = true;
            (Layout == "en" ? LayoutEn : LayoutUk).IsChecked = true;
            UnlockAll.IsChecked = c.UnlockAll;
            syncing = false;

            if (selected == null || selected.Layout != Layout)
                selected = Course.Find(c.LessonFor(Layout)) ?? StatsStore.FirstOpen(Layout);

            var course = Course.For(Layout);
            int passed = course.Count(l => StatsStore.IsPassed(l.Id));
            CourseTitle.Text = Layout == "en" ? "Курс латиниці" : "Курс української";
            CourseProgress.Text = $"Зараховано {passed} з {course.Count} уроків";
            coursePart = (double)passed / course.Count;
            UpdateBar();

            BuildList(course);
            ShowDetail(selected);
        }

        private void UpdateBar()
        {
            if (CourseBar.Parent is FrameworkElement track) CourseBar.Width = track.ActualWidth * coursePart;
        }

        // ---------- список ----------

        private void BuildList(System.Collections.Generic.List<Lesson> course)
        {
            List.Children.Clear();
            AddHeader("Практика");
            AddRow(Course.Endless(Layout));
            AddRow(Course.Dynamic(Layout));
            AddRow(Course.Free(Layout));
            AddRow(Course.Weak(Layout));
            AddRow(Course.Custom(Layout));

            int stage = 0;
            foreach (var l in course)
            {
                if (l.Stage != stage)
                {
                    stage = l.Stage;
                    AddHeader($"{l.Stage}. {l.StageTitle}");
                }
                AddRow(l);
            }
        }

        private void AddHeader(string text)
        {
            var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, List.Children.Count == 0 ? 4 : 16, 0, 4) };
            List.Children.Add(t);
        }

        private void AddRow(Lesson l)
        {
            bool isPassed = StatsStore.IsPassed(l.Id);
            bool unlocked = StatsStore.IsUnlocked(l);
            bool current = l.Id == AppConfig.Current.LessonFor(l.Layout);
            bool isSelected = selected != null && selected.Id == l.Id;

            var icon = new TextBlock
            {
                Width = 22, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Text = l.Kind == LessonKind.Endless ? "" : l.Kind == LessonKind.Dynamic ? "" : l.Kind == LessonKind.Adaptive ? "" : l.Kind == LessonKind.Custom ? "" : l.IsFree ? "" : isPassed ? "" : !unlocked ? "" : current ? "" : "•",
            };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, (l.IsFree || isPassed || !unlocked || current) ? "IconFont" : "UiFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, isPassed ? "OkBrush" : current ? "AccentBrush" : "FgMutedBrush");

            var code = new TextBlock { Text = l.IsFree ? "" : l.Code, Width = 34, VerticalAlignment = VerticalAlignment.Center };
            code.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");

            var title = new TextBlock { Text = l.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (!unlocked) title.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");

            var p = StatsStore.Progress(l.Id);
            var best = new TextBlock
            {
                Text = p != null && p.Best > 0 ? TypingSession.FormatSpeed(p.Best, AppConfig.Current.SpeedUnit) : "",
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
            };
            best.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");

            var dock = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(code, Dock.Left);
            DockPanel.SetDock(best, Dock.Right);
            dock.Children.Add(icon);
            dock.Children.Add(code);
            dock.Children.Add(best);
            dock.Children.Add(title);

            var row = new Border { Child = dock, Padding = new Thickness(8, 7, 10, 7), CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand };
            if (isSelected) row.SetResourceReference(Border.BackgroundProperty, "SubtleSelectedBrush");
            else row.Background = System.Windows.Media.Brushes.Transparent;
            row.MouseEnter += (s, e) => { if (!isSelected) row.SetResourceReference(Border.BackgroundProperty, "SubtleHoverBrush"); };
            row.MouseLeave += (s, e) => { if (!isSelected) row.Background = System.Windows.Media.Brushes.Transparent; };
            row.MouseLeftButtonUp += (s, e) =>
            {
                if (selected != null && selected.Id == l.Id) return;
                selected = l;
                Refresh();
            };
            row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount >= 2 && StatsStore.IsUnlocked(l)) { selected = l; Start_Click(null, null); } };
            List.Children.Add(row);
        }

        // ---------- опис ----------

        private void ShowDetail(Lesson l)
        {
            var unit = AppConfig.Current.SpeedUnit;
            var label = TypingSession.SpeedLabel(unit);
            DStage.Text = l.IsFree ? "Вільна практика" : $"Урок {l.Code} · {l.StageTitle}";
            DTitle.Text = l.Title;
            DDesc.Text = l.Description;

            DKeys.Children.Clear();
            bool dyn = l.Kind == LessonKind.Dynamic || l.Kind == LessonKind.Endless;
            var keys = dyn ? DynamicTrainer.Focus(l.Layout).ToArray()
                     : l.Kind == LessonKind.Fn ? new[] { "F1", "…", "F12", "Del" }
                     : l.Kind == LessonKind.Adaptive ? StatsStore.WeakKeys(l.Layout, 6).Select(k => k.ToUpper()).Concat(StatsStore.WeakBigrams(l.Layout, 4)).ToArray()
                     : l.NewKeys.Select(ch => ch.ToString().ToUpper()).ToArray();
            foreach (var k in keys) DKeys.Children.Add(KeyChip(k));
            DKeys.Visibility = keys.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            DGoal.Text = l.IsFree ? "без мети" : l.MinCpm > 0
                ? $"{AppConfig.Current.UnlockAccuracy:0}% · {TypingSession.FormatSpeed(l.MinCpm, unit)} {label}"
                : $"{AppConfig.Current.UnlockAccuracy:0}%";
            var p = StatsStore.Progress(l.Id);
            DBest.Text = p != null && p.Best > 0 ? $"{TypingSession.FormatSpeed(p.Best, unit)} {label}" : "—";
            DAttempts.Text = p != null ? p.Attempts.ToString() : "0";

            bool unlocked = StatsStore.IsUnlocked(l);
            bool isPassed = StatsStore.IsPassed(l.Id);
            StartButton.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Content = isPassed ? "Пройти ще раз" : "Почати урок";
            var prev = Course.Previous(l);
            DLock.Text = dyn ? DynamicSummary(l.Layout, keys.Length > 0)
                : l.Kind == LessonKind.Adaptive && keys.Length == 0
                ? "Поки замало даних — пройдіть кілька вправ, і тут з’являться ваші слабкі клавіші. Зараз вправа складатиметься з частих слів."
                : l.Kind == LessonKind.Adaptive ? "Зараз у фокусі ці клавіші та сполучення літер."
                : !unlocked && prev != null
                ? $"Урок відкриється, коли ви пройдете урок {prev.Code} «{prev.Title}». Або увімкніть «Відкрити всі уроки»."
                : isPassed ? "Урок зараховано." : "";
            DLock.Visibility = DLock.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Свій текст: редактор замість мети й результатів.
            bool custom = l.Kind == LessonKind.Custom;
            CustomBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            MetricsRow.Visibility = custom || dyn ? Visibility.Collapsed : Visibility.Visible;
            if (dyn) StartButton.Content = "Почати";
            if (custom)
            {
                if (!CustomInput.IsKeyboardFocusWithin && CustomInput.Text != AppConfig.Current.EffectiveCustomText(Layout))
                    CustomInput.Text = AppConfig.Current.EffectiveCustomText(Layout);
                UpdateCustomInfo();
            }
        }

        private static string DynamicSummary(string layout, bool hasFocus)
        {
            int mastered = DynamicTrainer.MasteredWords(layout), total = DynamicTrainer.TotalWords(layout);
            int grams = Mastery.GramsOf(layout).Count(kv => kv.Value.Mastered);
            double pace = Mastery.PaceOf(layout);
            string open = DynamicTrainer.Letters(layout);
            var text = $"Відкрито літер і знаків: {open.Length} з {DynamicTrainer.LetterOrder(layout).Length} ({string.Join(" ", open.Select(DynamicTrainer.SymbolName))}).  " +
                       $"Вивчено слів: {mastered} з {total}.  Вивчено сполучень літер: {grams}.";
            if (pace > 0) text += $"  Ваш темп: {pace:0} мс між клавішами.";
            text += hasFocus ? "\nВище — сполучення, які зараз на донавчанні." : mastered == 0 ? "\nПочніть — перші слова будуть зовсім прості." : "";
            return text;
        }

        // ---------- свій текст ----------

        private void UpdateCustomInfo()
        {
            var text = ExerciseGenerator.NormalizeCustom(CustomInput.Text, Layout);
            if (text.Length < 10)
            {
                CustomInfo.Text = "Вставте текст (Ctrl+V) або відкрийте файл";
                return;
            }
            int parts = (int)Math.Ceiling(text.Length / 200.0);
            bool same = CustomInput.Text == AppConfig.Current.EffectiveCustomText(Layout);
            int pos = same ? Math.Min(AppConfig.Current.CustomPos, text.Length) : 0;
            bool isDefault = CustomInput.Text == AppConfig.DefaultText(Layout);
            CustomInfo.Text = $"{text.Length} символів · {parts} частин · пройдено {Math.Round(100.0 * pos / text.Length)}%" + (isDefault ? " · типовий текст" : "");
        }

        private void SaveCustom()
        {
            var c = AppConfig.Current;
            // Якщо в полі типовий текст без змін — окремо його не зберігаємо.
            string value = CustomInput.Text.Trim() == AppConfig.DefaultText(Layout) ? c.CustomText : CustomInput.Text;
            if (c.CustomText == value) return;
            c.CustomText = value;
            c.CustomPos = 0;
            c.Save();
        }

        private void CustomInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateCustomInfo();
        private void CustomInput_LostFocus(object sender, RoutedEventArgs e) => SaveCustom();

        private void ClearCustom_Click(object sender, RoutedEventArgs e)
        {
            // Очищення повертає типовий текст (якщо файл default_text.txt є).
            var c = AppConfig.Current;
            c.CustomText = "";
            c.CustomPos = 0;
            c.Save();
            CustomInput.Text = AppConfig.DefaultText(Layout);
            UpdateCustomInfo();
        }

        private void SaveDefault_Click(object sender, RoutedEventArgs e)
        {
            if (ExerciseGenerator.NormalizeCustom(CustomInput.Text, Layout).Length < 10)
            {
                CustomInfo.Text = "Спершу вставте текст";
                return;
            }
            if (!AppConfig.SaveDefaultText(CustomInput.Text, Layout))
            {
                CustomInfo.Text = "Не вдалося записати файл " + AppConfig.DefaultTextFile(Layout);
                return;
            }
            var c = AppConfig.Current;
            c.CustomText = "";
            c.CustomPos = 0;
            c.Save();
            UpdateCustomInfo();
            CustomInfo.Text = "Збережено як типовий текст (" + AppConfig.DefaultTextFile(Layout) + ")";
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Текстові файли (*.txt)|*.txt|Усі файли (*.*)|*.*",
                Title = "Відкрити текст для набору",
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var bytes = File.ReadAllBytes(dlg.FileName);
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
                catch (DecoderFallbackException) { text = Encoding.GetEncoding(1251).GetString(bytes); } // старі файли Windows
                if (text.Length > 200000) text = text.Substring(0, 200000);
                CustomInput.Text = text;
                SaveCustom();
            }
            catch (Exception ex)
            {
                CustomInfo.Text = "Не вдалося відкрити файл: " + ex.Message;
            }
        }

        private static FrameworkElement KeyChip(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Child = t, MinWidth = 42, Height = 42, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 3),
            };
            b.SetResourceReference(Border.BackgroundProperty, "KeyBgBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "KeyBorderBrush");
            return b;
        }

        // ---------- дії ----------

        private void Layout_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            var layout = sender == LayoutEn ? "en" : "uk";
            if (AppConfig.Current.Layout != layout) AppConfig.Update(c => c.Layout = layout);
        }

        private void UnlockAll_Click(object sender, RoutedEventArgs e)
        {
            bool on = UnlockAll.IsChecked == true;
            AppConfig.Update(c => c.UnlockAll = on);
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            var l = selected;
            if (l == null || !StatsStore.IsUnlocked(l)) return;
            if (l.Kind == LessonKind.Custom)
            {
                SaveCustom();
                if (ExerciseGenerator.NormalizeCustom(AppConfig.Current.EffectiveCustomText(l.Layout), l.Layout).Length < 10)
                {
                    CustomInfo.Text = "Спершу вставте текст, який можна набрати в цій розкладці";
                    return;
                }
            }
            AppConfig.Update(c => { c.Layout = l.Layout; c.SetLesson(l.Id); });
            (Window.GetWindow(this) as MainWindow)?.ShowTraining();
        }
    }
}
