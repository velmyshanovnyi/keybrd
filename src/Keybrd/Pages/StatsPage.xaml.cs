using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Keybrd.Controls;
using Keybrd.Core;

namespace Keybrd.Pages
{
    public partial class StatsPage : UserControl
    {
        private int days = 30;
        private bool slow, syncing;
        private double coursePart;

        public StatsPage()
        {
            InitializeComponent();
            syncing = true;
            R30.IsChecked = true;
            HeatErrors.IsChecked = true;
            syncing = false;
            SizeChanged += (s, e) => UpdateBar();
            Loaded += (s, e) =>
            {
                AppConfig.Changed += Refresh;
                StatsStore.Changed += Refresh;
                ThemeManager.Changed += Refresh;
                Refresh();
            };
            Unloaded += (s, e) =>
            {
                AppConfig.Changed -= Refresh;
                StatsStore.Changed -= Refresh;
                ThemeManager.Changed -= Refresh;
            };
        }

        private static string Layout => AppConfig.Current.Layout;
        private static string Unit => AppConfig.Current.SpeedUnit;
        private static string Speed(double cpm) => $"{TypingSession.FormatSpeed(cpm, Unit)}";
        private static string SpeedL(double cpm) => $"{TypingSession.FormatSpeed(cpm, Unit)} {TypingSession.SpeedLabel(Unit)}";

        // ---------- фільтри ----------

        private void Range_Checked(object sender, RoutedEventArgs e)
        {
            days = sender == R7 ? 7 : sender == R30 ? 30 : sender == R90 ? 90 : 0;
            if (!syncing && IsLoaded) Refresh();
        }

        private void Layout_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            var layout = sender == LayoutEn ? "en" : "uk";
            if (AppConfig.Current.Layout != layout) AppConfig.Update(c => c.Layout = layout);
        }

        private void Heat_Checked(object sender, RoutedEventArgs e)
        {
            slow = sender == HeatSlow;
            if (!syncing && IsLoaded) UpdateHeat();
        }

        private void TrainWeak_Click(object sender, RoutedEventArgs e)
        {
            var layout = Layout;
            AppConfig.Update(c => c.SetLesson(layout + "-weak"));
            (Window.GetWindow(this) as MainWindow)?.ShowTraining();
        }

        // ---------- оновлення ----------

        private void Refresh()
        {
            syncing = true;
            (Layout == "en" ? LayoutEn : LayoutUk).IsChecked = true;
            syncing = false;

            var all = StatsStore.SessionsOf(Layout);
            var week = all.Where(s => s.Ts >= DateTime.Today.AddDays(-6)).ToList();

            var last = all.LastOrDefault();
            MLast.Text = last != null ? Speed(last.Cpm) : "—";
            MLastSub.Text = last != null ? $"{TypingSession.SpeedLabel(Unit)} · {last.Accuracy:0.#}%" : "ще немає вправ";

            var best = all.OrderByDescending(s => s.Cpm).FirstOrDefault(s => s.Accuracy >= 90) ?? all.OrderByDescending(s => s.Cpm).FirstOrDefault();
            MBest.Text = best != null ? Speed(best.Cpm) : "—";
            MBestSub.Text = best != null ? $"{TypingSession.SpeedLabel(Unit)} · {best.Ts:dd.MM.yyyy}" : "";

            double weekChars = week.Sum(s => Math.Max(1, s.Chars));
            MAvg.Text = week.Count > 0 ? Speed(week.Sum(s => s.Cpm * Math.Max(1, s.Chars)) / weekChars) : "—";
            var prevWeek = all.Where(s => s.Ts >= DateTime.Today.AddDays(-13) && s.Ts < DateTime.Today.AddDays(-6)).ToList();
            if (week.Count > 0 && prevWeek.Count > 0)
            {
                double now = week.Sum(s => s.Cpm * Math.Max(1, s.Chars)) / weekChars;
                double before = prevWeek.Sum(s => s.Cpm * Math.Max(1, s.Chars)) / prevWeek.Sum(s => Math.Max(1, s.Chars));
                double diff = Unit == "wpm" ? (now - before) / 5 : now - before;
                MAvgSub.Text = $"{(diff >= 0 ? "+" : "−")}{Math.Abs(Math.Round(diff))} до попереднього тижня";
            }
            else MAvgSub.Text = $"{week.Count} вправ";

            MAcc.Text = week.Count > 0 ? $"{week.Sum(s => s.Accuracy * Math.Max(1, s.Chars)) / weekChars:0.#}%" : "—";
            MAccSub.Text = week.Count > 0 ? $"помилок: {week.Sum(s => s.Errors)}" : "";

            var total = TimeSpan.FromSeconds(all.Sum(s => s.DurationSec));
            MTime.Text = total.TotalHours >= 1 ? $"{(int)total.TotalHours} год {total.Minutes} хв" : $"{total.Minutes} хв";
            MTimeSub.Text = $"{all.Count} вправ";

            // графіки
            SpeedTitle.Text = $"Швидкість по днях, {TypingSession.SpeedLabel(Unit)}";
            var daily = StatsStore.Daily(Layout, days);
            SpeedChart.SetData(daily.Select(d => new LineChart.Point
            {
                Day = d.Day, Value = Unit == "wpm" ? d.Cpm / 5 : d.Cpm,
                Tip = $"{d.Day:dd.MM}: {SpeedL(d.Cpm)}, {d.Sessions} вправ, {Math.Round(d.Minutes)} хв",
            }), v => Math.Round(v).ToString());

            double accMin = daily.Count > 0 ? Math.Min(90, Math.Floor(daily.Min(d => d.Accuracy)) - 2) : 90;
            AccChart.SetData(daily.Select(d => new LineChart.Point
            {
                Day = d.Day, Value = d.Accuracy,
                Tip = $"{d.Day:dd.MM}: {d.Accuracy:0.#}%",
            }), v => Math.Round(v).ToString(), accMin, 100);

            UpdateHeat();
            BuildRecent(all);
            UpdateCourse();
        }

        private void UpdateHeat()
        {
            var keys = StatsStore.KeysOf(Layout);

            // Кілька символів на одній клавіші (1 і !) — сумуємо.
            var byId = new Dictionary<int, KeyStat>();
            var names = new Dictionary<int, List<string>>();
            foreach (var kv in keys)
            {
                int id = IdFor(kv.Key);
                if (id == 0) continue;
                if (!byId.TryGetValue(id, out var s)) { byId[id] = s = new KeyStat(); names[id] = new List<string>(); }
                s.Hits += kv.Value.Hits; s.Errors += kv.Value.Errors; s.Timed += kv.Value.Timed; s.TotalMs += kv.Value.TotalMs;
                names[id].Add(kv.Key == " " ? "пробіл" : kv.Key);
            }

            var cells = new Dictionary<int, KeyboardView.HeatCell>();
            var enough = byId.Where(kv => kv.Value.Total >= 5).ToList();
            if (!slow)
            {
                double max = Math.Max(0.03, enough.Select(kv => kv.Value.ErrorRate).DefaultIfEmpty(0).Max());
                foreach (var kv in enough)
                    cells[kv.Key] = new KeyboardView.HeatCell { Level = kv.Value.ErrorRate / max, Tip = Tip(names[kv.Key], kv.Value) };
                HeatHint.Text = "Чим червоніша клавіша, тим частіше на ній помилки. Наведіть, щоб побачити цифри.";
            }
            else
            {
                var timed = enough.Where(kv => kv.Value.Timed >= 3).ToList();
                double lo = timed.Select(kv => kv.Value.AvgMs).DefaultIfEmpty(0).Min();
                double hi = timed.Select(kv => kv.Value.AvgMs).DefaultIfEmpty(0).Max();
                foreach (var kv in timed)
                    cells[kv.Key] = new KeyboardView.HeatCell { Level = hi > lo ? (kv.Value.AvgMs - lo) / (hi - lo) : 0, Tip = Tip(names[kv.Key], kv.Value) };
                HeatHint.Text = "Чим жовтіша клавіша, тим довше ви її шукаєте. Наведіть, щоб побачити цифри.";
            }
            Heat.SetHeat(cells, slow);

            var weak = StatsStore.WeakKeys(Layout, 6).Select(KeyLabel).ToList();
            var pairs = StatsStore.WeakBigrams(Layout, 4);
            WeakText.Text = weak.Count == 0
                ? "Пройдіть кілька вправ, і тут з’являться ваші найслабші клавіші."
                : "Найслабші клавіші: " + string.Join(", ", weak) + (pairs.Count > 0 ? ".  Сполучення: " + string.Join(", ", pairs) : "");
        }

        private static string KeyLabel(string k) => k == " " ? "пробіл" : k.ToUpper();

        private static string Tip(List<string> names, KeyStat s)
            => $"{string.Join(" ", names.Select(n => n.Length == 1 ? n.ToUpper() : n).Distinct())}\n" +
               $"натискань: {s.Total}, помилок: {s.Errors} ({s.ErrorRate * 100:0.#}%)\n" +
               (s.Timed > 0 ? $"у середньому {s.AvgMs:0} мс" : "");

        private static int IdFor(string name)
        {
            if (name == " ") return KeyboardLayout.Space;
            if (name.Length == 1)
                return KeyboardLayout.Find(Layout, name[0], out var id, out _) ? id : 0;
            for (int i = 0; i < 14; i++)
            {
                char c = (char)(KeyboardLayout.SpecialBase + i);
                if (KeyboardLayout.SpecialName(c) == name && KeyboardLayout.Find(Layout, c, out var id, out _)) return id;
            }
            return 0;
        }

        private void BuildRecent(List<SessionRecord> all)
        {
            Recent.Children.Clear();
            var items = Enumerable.Reverse(all).Take(10).ToList();
            if (items.Count == 0)
            {
                var t = new TextBlock { Text = "Тут з’являться ваші вправи. Сесії, коротші за 10 секунд, не записуються." };
                t.SetResourceReference(StyleProperty, "Muted");
                Recent.Children.Add(t);
                return;
            }

            foreach (var s in items)
            {
                var l = Course.Find(s.Lesson);
                string name = l == null ? s.Lesson : l.IsFree ? l.Title : $"{l.Code} {l.Title}";

                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

                Cell(g, 0, s.Ts.ToString("dd.MM HH:mm"), muted: true);
                Cell(g, 1, name);
                Cell(g, 2, SpeedL(s.Cpm), right: true);
                Cell(g, 3, $"{s.Accuracy:0.#}%", right: true, muted: true);
                if (s.Passed)
                {
                    var ok = new TextBlock { Text = "", HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Урок зараховано" };
                    ok.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                    ok.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                    Grid.SetColumn(ok, 4);
                    g.Children.Add(ok);
                }
                Recent.Children.Add(g);
            }
        }

        private static void Cell(Grid g, int col, string text, bool right = false, bool muted = false)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            if (muted) t.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        private void UpdateCourse()
        {
            var course = Course.For(Layout);
            int passed = course.Count(l => StatsStore.IsPassed(l.Id));
            CourseText.Text = $"{passed} з {course.Count}";
            coursePart = (double)passed / course.Count;
            UpdateBar();
            var next = StatsStore.FirstOpen(Layout);
            CourseSub.Text = passed == course.Count
                ? "Курс пройдено повністю."
                : $"Наступний урок: {next.Code} «{next.Title}»";

            // Динамічне навчання: скільки слів і сполучень уже «в пальцях»
            int mastered = DynamicTrainer.MasteredWords(Layout), total = DynamicTrainer.TotalWords(Layout);
            int grams = Mastery.GramsOf(Layout).Count(kv => kv.Value.Mastered);
            var weak = DynamicTrainer.Focus(Layout);
            DynText.Text = $"{mastered} з {total} слів";
            dynPart = total > 0 ? (double)mastered / total : 0;
            DynSub.Text = $"Відкрито літер і знаків: {DynamicTrainer.Letters(Layout).Length} з {DynamicTrainer.LetterOrder(Layout).Length} · вивчено сполучень: {grams}"
                          + (weak.Count > 0 ? "\nНа донавчанні: " + string.Join(", ", weak) : "");
            UpdateBar();
        }

        private double dynPart;

        private void UpdateBar()
        {
            if (CourseBar.Parent is FrameworkElement track) CourseBar.Width = track.ActualWidth * coursePart;
            if (DynBar.Parent is FrameworkElement track2) DynBar.Width = track2.ActualWidth * dynPart;
        }
    }
}
