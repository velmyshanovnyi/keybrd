using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Keybrd.Controls;
using Keybrd.Core;

namespace Keybrd.Pages
{
    public partial class TrainingPage : UserControl
    {
        private static readonly string[] LegendNames = { "мізинці", "безіменні", "середні", "лівий вказівний", "правий вказівний", "великі" };
        private const int EscId = 0x01;

        private readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private TypingSession session;
        private Lesson lesson;
        private bool syncing, lShift, rShift, lastPassed;

        public TrainingPage()
        {
            InitializeComponent();
            tick.Tick += (s, e) => { UpdateStats(); CheckWindowsLayout(); };
            Kbd.TargetChanged += UpdateHint;
            SizeChanged += (s, e) => { UpdateProgress(); UpdateScroll(); };
            Loaded += (s, e) =>
            {
                AppConfig.Changed += Sync;
                ThemeManager.Changed += OnThemeChanged;
                KeyInput.Key += OnKey;
                KeyInput.ReleaseAll += OnReleaseAll;
                tick.Start();
                Sync();
                RefreshVoice();
                SpeakAhead(true);
            };
            Unloaded += (s, e) =>
            {
                AppConfig.Changed -= Sync;
                ThemeManager.Changed -= OnThemeChanged;
                KeyInput.Key -= OnKey;
                KeyInput.ReleaseAll -= OnReleaseAll;
                tick.Stop();
                FinishEndless(false); // перехід на іншу вкладку завершує урок без зупинки
                Speech.Stop();
            };
            Sync();
        }

        // ---------- налаштування ----------

        private void Sync()
        {
            var c = AppConfig.Current;
            syncing = true;
            (c.Layout == "en" ? LayoutEn : LayoutUk).IsChecked = true;
            ChipFocus.IsChecked = c.FocusMode;
            ChipLine.IsChecked = c.SingleLine;
            ChipHide.IsChecked = c.AutoHideKeyboard;
            ChipFix.IsChecked = c.FixMode;
            if (session != null) session.FixMode = c.FixMode;   // діє, доки вправу не почато
            ChipVoice.IsChecked = c.Voice;
            ChipFingers.IsChecked = c.ShowFingerColors;
            ChipAlt.IsChecked = c.ShowAltLetters;
            ChipAlt.Content = "Літери " + KeyboardLayout.ShortName(KeyboardLayout.Other(c.Layout));
            ChipFn.IsChecked = c.ShowFnRow;
            ChipLaptop.IsChecked = c.KeyboardStyle == "laptop";
            syncing = false;

            ApplyKeyboardOpacity(true);
            if (!c.Voice) Speech.Stop();

            var wanted = Course.Find(c.LessonFor(c.Layout)) ?? StatsStore.FirstOpen(c.Layout);
            if (session == null || lesson == null || lesson.Id != wanted.Id || session.Layout != c.Layout)
            {
                FinishEndless(false); // зберегти урок без зупинки, перш ніж перемикатися
                lesson = wanted;
                NewSession(false);
            }
            else
            {
                Render();
                UpdateStats();
            }
            BuildLegend();
        }

        private void Layout_Checked(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            var layout = sender == LayoutEn ? "en" : "uk";
            if (AppConfig.Current.Layout != layout) AppConfig.Update(c => c.Layout = layout);
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            bool on = ((ToggleButton)sender).IsChecked == true;
            if (sender == ChipFocus) AppConfig.Update(c => c.FocusMode = on);
            else if (sender == ChipLine) AppConfig.Update(c => c.SingleLine = on);
            else if (sender == ChipHide) AppConfig.Update(c => c.AutoHideKeyboard = on);
            else if (sender == ChipFix) AppConfig.Update(c => c.FixMode = on);
            else if (sender == ChipVoice) ToggleVoice(on);
            else if (sender == ChipFingers) AppConfig.Update(c => c.ShowFingerColors = on);
            else if (sender == ChipAlt) AppConfig.Update(c => c.ShowAltLetters = on);
            else if (sender == ChipFn) AppConfig.Update(c => c.ShowFnRow = on);
            else if (sender == ChipLaptop) AppConfig.Update(c => c.KeyboardStyle = on ? "laptop" : "standard");
        }

        // ---------- вправа ----------

        // ---------- урок без зупинки ----------

        private bool IsEndless => lesson != null && lesson.Kind == LessonKind.Endless;

        /// <summary>Вікно закривається: зберегти незавершений урок без зупинки.</summary>
        public void SaveUnfinished() => FinishEndless(false);
        private int displayStart;      // з якої позиції тексту малюємо
        private int masteryPos;        // до якої позиції модель уже оновлено
        private string lastFresh = ""; // літери «в роботі» під час попередньої порції
        private int startWords, startLetters;

        /// <summary>Оновити модель за щойно набраним і дописати наступну порцію тексту вже з урахуванням цього.</summary>
        private void ExtendEndless()
        {
            // Оцінюємо лише цілі слова: до початку слова, яке зараз друкується.
            int checkpoint = session.Pos > 0 ? session.Text.LastIndexOf(' ', session.Pos - 1) + 1 : 0;
            if (checkpoint > masteryPos)
            {
                Mastery.Update(session, masteryPos, checkpoint);
                masteryPos = checkpoint;
                session.Floor = masteryPos;   // оцінене вже не стирається
            }

            string fresh = DynamicTrainer.Fresh(lesson.Layout);
            bool intro = fresh != lastFresh;   // розминка — лише коли з'явилась нова літера
            lastFresh = fresh;
            // Порція з новою літерою коротша: швидше видно, чи літера закріпилась, і можна відкривати наступну.
            string chunk = DynamicTrainer.Generate(lesson.Layout, intro ? 80 : lesson.Length, intro);
            session.Append(" " + chunk);
            DebugLog.Write("APPEND", chunk);
            DebugLog.Write("STATE", DynamicTrainer.Status(lesson.Layout) + $" | слів {DynamicTrainer.MasteredWords(lesson.Layout)}, відомих {DynamicTrainer.KnownWords(lesson.Layout)}");
            ExerciseName.Text = "без зупинки · " + DynamicTrainer.Status(lesson.Layout);
        }

        /// <summary>Завершити урок без зупинки: дооцінити набране й записати вправу в статистику.</summary>
        private void FinishEndless(bool render)
        {
            if (!IsEndless || session == null || session.Finished || !session.Started) return;
            session.Stop();
            onBreak = false;
            Speech.Stop();
            StatsStore.Record(session, lesson, masteryPos);
            masteryPos = session.Pos;
            DebugLog.Write("END", $"{session.Pos}\t{session.Errors}\t{session.Cpm:0}");
            if (render) { Render(); UpdateStats(); }
        }

        private void NewSession(bool sameText)
        {
            FinishEndless(false); // незавершений урок без зупинки не має пропасти

            var text = IsEndless ? DynamicTrainer.Generate(lesson.Layout, lesson.Length, true)
                : sameText && session != null && session.Layout == lesson.Layout
                ? session.Text
                : ExerciseGenerator.Generate(lesson);
            session = new TypingSession(text, lesson.Layout) { FixMode = AppConfig.Current.FixMode };
            lastPassed = false;
            displayStart = 0;
            masteryPos = 0;
            onBreak = false;
            breakNumber = 0;
            cleanStreak = 0;
            spokenUntil = 0;
            note = null;
            ScheduleBreak();

            if (IsEndless)
            {
                lastFresh = DynamicTrainer.Fresh(lesson.Layout);
                startWords = DynamicTrainer.MasteredWords(lesson.Layout);
                startLetters = DynamicTrainer.Letters(lesson.Layout).Length;
                ExerciseName.Text = "без зупинки · " + DynamicTrainer.Status(lesson.Layout);
            }
            else if (lesson.Kind == LessonKind.Dynamic)
            {
                ExerciseName.Text = "динамічне навчання · " + DynamicTrainer.Status(lesson.Layout);
            }
            else
            {
                ExerciseName.Text = PracticeName();
            }

            DebugLog.Write("TEXT", text);
            DebugLog.Write("NAME", ExerciseName.Text);

            // Урок на F1–F12 без показаного ряду Fn не має сенсу.
            if (lesson.Kind == LessonKind.Fn && !AppConfig.Current.ShowFnRow)
                Dispatcher.BeginInvoke(new Action(() => AppConfig.Update(c => c.ShowFnRow = true)));

            Render();
            UpdateStats();
            RefreshVoice();
            CheckWindowsLayout();
            ApplyKeyboardOpacity(true);
            if (IsLoaded) SpeakAhead(true);
        }

        /// <summary>Підпис над текстом для уроків курсу і вільної практики.</summary>
        private string PracticeName()
        {
            switch (lesson.Kind)
            {
                case LessonKind.Adaptive: return "слабкі місця";
                case LessonKind.Confusion:
                    var pairs = Confusions.LetterPairs(lesson.Layout, 3);
                    return pairs.Count > 0 ? "плутанина · " + string.Join(", ", pairs.Select(p => p.Text)) : "плутанина · стійких пар поки немає";
                default:
                    return lesson.IsFree ? "вільна практика" : $"урок {lesson.Code} · {lesson.Title}";
            }
        }

        private void GoNext()
        {
            var next = lastPassed ? Course.Next(lesson) : null;
            if (next != null) AppConfig.Update(c => c.SetLesson(next.Id)); // Sync створить нову вправу
            else NewSession(false);
        }

        private void NewText_Click(object sender, RoutedEventArgs e) => GoNext();
        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (onBreak) FinishEndless(true);   // на перерві друга кнопка — «Завершити»
            else NewSession(true);
        }

        private void OnReleaseAll() => lShift = rShift = false;

        // ---------- перерви в уроці без зупинки ----------

        private bool onBreak;
        private double nextBreakMs;
        private int breakNumber;

        private void ScheduleBreak()
        {
            int minutes = AppConfig.Current.BreakMinutes;
            nextBreakMs = minutes > 0 ? session.ElapsedMs + minutes * 60000.0 : double.MaxValue;
        }

        /// <summary>Час блоку вийшов: зупиняємось на межі слова і питаємо, чи продовжувати.</summary>
        private void StartBreak()
        {
            onBreak = true;
            breakNumber++;
            Speech.Stop();
            int checkpoint = session.Pos > 0 ? session.Text.LastIndexOf(' ', session.Pos - 1) + 1 : 0;
            if (checkpoint > masteryPos) { Mastery.Update(session, masteryPos, checkpoint); masteryPos = checkpoint; }
            DebugLog.Write("BREAK", $"{breakNumber}\t{session.Pos}");
        }

        private void EndBreak()
        {
            onBreak = false;
            cleanStreak = 0;
            ScheduleBreak();
            Render();
            UpdateStats();
            SpeakAhead(true);
        }

        // ---------- клавіатура, що згасає ----------

        private int cleanStreak;   // скільки клавіш поспіль натиснуто без помилки

        /// <summary>
        /// Якщо ввімкнено «Ховати клавіатуру»: після 8 чистих натискань клавіатура починає згасати
        /// і за наступні 20 зникає зовсім. Помилка одразу повертає її, щоб було видно потрібну клавішу.
        /// </summary>
        private void ApplyKeyboardOpacity(bool instant)
        {
            var c = AppConfig.Current;
            double value = c.FocusMode ? 0.35 : 1;
            if (c.AutoHideKeyboard && session != null && !session.Finished && !onBreak)
            {
                double factor = cleanStreak < 8 ? 1 : Math.Max(0, 1 - (cleanStreak - 8) / 20.0);
                value *= factor;
            }
            if (instant)
            {
                Kbd.BeginAnimation(OpacityProperty, null);
                Kbd.Opacity = value;
                return;
            }
            // Повертається швидко (помилка), згасає плавно.
            var duration = value > Kbd.Opacity ? TimeSpan.FromMilliseconds(90) : TimeSpan.FromMilliseconds(350);
            Kbd.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(value, duration));
        }

        // ---------- озвучення ----------

        private int spokenUntil;   // до якої позиції текст уже озвучено

        /// <summary>
        /// Озвучити наступний відрізок: до кінця речення, а якщо речень немає — чотири слова.
        /// Викликається, коли курсор доходить до ще не озвученого місця.
        /// </summary>
        private void SpeakAhead(bool force)
        {
            if (!AppConfig.Current.Voice || session == null || session.Finished || onBreak) return;
            if (!force && session.Pos < spokenUntil) return;

            var text = session.Text;
            int start = session.Pos;
            if (start >= text.Length) return;
            int end = start, words = 0;
            while (end < text.Length && end - start < 90)
            {
                char ch = text[end++];
                if (ch == '.' || ch == '?' || ch == '!') break;
                if (ch == ' ' && ++words >= 4 && text.IndexOfAny(new[] { '.', '?', '!' }, end, Math.Min(40, text.Length - end)) < 0) break;
            }
            spokenUntil = end;
            var sb = new StringBuilder();
            for (int k = start; k < end; k++)
                sb.Append(KeyboardLayout.IsSpecial(text[k]) ? " " + KeyboardLayout.SpecialName(text[k]) + " " : text[k].ToString());
            Speech.Say(sb.ToString(), session.Layout);
        }

        private void OnKey(int id, bool down, bool repeat)
        {
            Kbd.Press(id, down);
            if (id == KeyboardLayout.LShift) lShift = down;
            if (id == KeyboardLayout.RShift) rShift = down;
            if (!down || repeat || session == null) return;

            if (id == EscId)
            {
                // В уроці без зупинки Esc завершує його і показує підсумок; в інших — починає вправу заново.
                if (IsEndless && session.Started && !session.Finished) FinishEndless(true);
                else NewSession(true);
                return;
            }
            if (onBreak)
            {
                // Перерва: Enter — продовжити далі, решта клавіш нічого не роблять.
                if (id == KeyboardLayout.Enter) EndBreak();
                return;
            }
            if (session.Finished)
            {
                if (id == KeyboardLayout.Enter) GoNext();
                return;
            }

            // Ctrl/Alt-комбінації — це гарячі клавіші програми, а не набір.
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

            if (id == KeyboardLayout.Backspace)
            {
                // Режим виправлення: Backspace стирає останній символ.
                if (session.Backspace())
                {
                    cleanStreak = 0;
                    Render();
                    UpdateStats();
                    UpdateHint();
                }
                return;
            }

            bool caps = Keyboard.IsKeyToggled(Key.CapsLock);
            var ch = KeyboardLayout.CharFor(session.Layout, id, lShift || rShift, caps);
            if (ch == null) return;
            // F1–F12 рахуються лише в уроці ряду Fn.
            if (KeyboardLayout.IsSpecial(ch[0]) && lesson.Kind != LessonKind.Fn) return;

            // Правильний Shift — на протилежній руці від клавіші. Не той Shift зараховуємо, але підказуємо;
            // у суворому режимі це помилка.
            bool badShift = false;
            if ((lShift || rShift) && session.Current != null && ch[0] == session.Current.Value
                && KeyboardLayout.RowOf(id) >= 0 && KeyboardLayout.Find(session.Layout, ch[0], out _, out bool needShift) && needShift)
            {
                int right = KeyboardLayout.ShiftFor(id);
                bool usedRight = right == KeyboardLayout.RShift ? rShift : lShift;
                if (usedRight) session.ShiftOk++;
                else
                {
                    badShift = true;
                    session.ShiftWrong++;
                    note = $"{KeyboardLayout.DisplayName(ch[0])} набирайте з {(right == KeyboardLayout.RShift ? "правим" : "лівим")} Shift: Shift має бути на іншій руці, ніж літера";
                    notePos = session.Pos;
                    Kbd.Flash(right, true);
                    DebugLog.Write("SHIFT", $"{session.Pos}\t{ch}");
                }
            }

            var result = session.Type(ch, badShift && AppConfig.Current.StrictShift);
            if (result == TypeResult.Ignored) return;
            Kbd.Flash(id, result != TypeResult.Wrong);
            if (result == TypeResult.Wrong && AppConfig.Current.Sound) Sounds.Error();
            if (result == TypeResult.Wrong) DebugLog.Write("ERR", $"{session.Pos}	{session.Current}");
            if (result == TypeResult.Wrong && session.FixMode)
            {
                note = "Помилка: зітріть її клавішею Backspace і наберіть правильно";
                notePos = session.Pos;
            }
            cleanStreak = result == TypeResult.Wrong ? 0 : cleanStreak + 1;
            if (IsEndless)
            {
                // Текст не закінчується: щойно лишається мало — дописуємо наступну порцію.
                if (session.Text.Length - session.Pos < 40) ExtendEndless();
                // Блок часу вийшов — зупиняємось після щойно завершеного слова.
                if (result != TypeResult.Wrong && ch == " " && session.ElapsedMs >= nextBreakMs) StartBreak();
            }
            else if (result == TypeResult.Finished) lastPassed = StatsStore.Record(session, lesson);
            Render();
            UpdateStats();
            UpdateHint();
            ApplyKeyboardOpacity(false);
            if (result != TypeResult.Wrong) SpeakAhead(false);
        }

        // Коротка порада під клавіатурою (не той Shift, «сповільніться»): видно ще кільканадцять знаків.
        private string note;
        private int notePos;

        // ---------- відображення ----------

        /// <summary>Стиль символу: 0 — набраний, 1 — набраний з помилкою, 2 — поточний, 3 — ще попереду, 4 — пробіл попереду, 5 — невиправлена помилка.</summary>
        private int StyleAt(int i)
        {
            if (i < session.Pos) return session.Wrong[i] ? 5 : session.HadError[i] ? 1 : 0;
            if (i == session.Pos) return 2;
            return session.Text[i] == ' ' ? 4 : 3;
        }

        private static string Display(char c)
        {
            if (c == ' ') return "·​";                       // крапка + невидимий розрив рядка
            if (KeyboardLayout.IsSpecial(c)) return " " + KeyboardLayout.SpecialName(c) + " ​";
            return c.ToString();
        }

        private void Render()
        {
            var c = AppConfig.Current;
            bool done = session.Finished || onBreak;
            bool line = c.SingleLine;
            SampleHost.Visibility = done || line ? Visibility.Collapsed : Visibility.Visible;
            TickerHost.Visibility = !done && line ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
            var target = line ? TickerText : SampleText;

            if (done)
            {
                ShowResult();
                Kbd.SetTarget(null);
                return;
            }

            double fs = c.TextFontSize * (c.FocusMode ? 1.35 : 1);
            target.FontSize = fs;
            if (line) TickerHost.Height = Math.Round(fs * 1.5);
            else SampleText.LineHeight = Math.Round(fs * 1.5);
            target.Inlines.Clear();

            var text = session.Text;

            // Урок без зупинки: давно набране не малюємо, інакше текст росте безмежно.
            // Початок показу зсувається рідко й одразу на багато, по межі слова.
            if (session.Pos - displayStart > 500)
            {
                int cut = text.LastIndexOf(' ', Math.Max(0, session.Pos - 160));
                if (cut > displayStart) { displayStart = cut + 1; tickerSession = null; }
            }
            if (displayStart > session.Pos) displayStart = 0;

            // Біжучий рядок: слова від n−2 до n+2 чіткі, далі з кожним словом блідіші.
            int[] fade = null;
            if (line)
            {
                var word = new int[text.Length];
                for (int k = displayStart + 1; k < text.Length; k++) word[k] = word[k - 1] + (text[k - 1] == ' ' ? 1 : 0);
                int cur = word[Math.Min(session.Pos, text.Length - 1)];
                fade = new int[text.Length];
                for (int k = displayStart; k < text.Length; k++)
                {
                    int d = Math.Abs(word[k] - cur);
                    fade[k] = d <= 2 ? 0 : d == 3 ? 1 : d == 4 ? 2 : 3;
                }
            }

            int i = displayStart;
            while (i < text.Length)
            {
                int style = StyleAt(i);
                int level = fade == null ? 0 : fade[i];
                int j = i + 1;
                if (style != 2)
                    while (j < text.Length && StyleAt(j) == style && (fade == null || fade[j] == level)) j++;

                var sb = new StringBuilder();
                for (int k = i; k < j; k++) sb.Append(Display(text[k]));
                var run = new Run(sb.ToString());
                if (style == 2)
                {
                    run.SetResourceReference(TextElement.BackgroundProperty, session.HadError[i] ? "ErrorBrush" : "AccentBrush");
                    run.SetResourceReference(TextElement.ForegroundProperty, "AccentFgBrush");
                    currentRun = run;
                }
                else if (style == 5)
                {
                    run.SetResourceReference(TextElement.BackgroundProperty, "ErrorBrush");
                    run.SetResourceReference(TextElement.ForegroundProperty, "AccentFgBrush");
                }
                else
                {
                    // У біжучому рядку набране лишається добре видимим (сіре, але не бліде).
                    string key = style == 1 ? "ErrorBrush"
                               : style == 3 ? "FgBrush"
                               : style == 0 && line ? "FgMutedBrush"
                               : "FgTypedBrush";
                    if (level == 0) run.SetResourceReference(TextElement.ForegroundProperty, key);
                    else run.Foreground = Faded(key, level);
                }
                target.Inlines.Add(run);
                i = j;
            }

            // Є невиправлена помилка — підказуємо Backspace замість наступної літери.
            if (session.Uncorrected > 0) Kbd.SetTargetKey(KeyboardLayout.Backspace);
            else Kbd.SetTarget(session.Current);
            UpdateProgress();
            Dispatcher.BeginInvoke(new Action(UpdateScroll), DispatcherPriority.Loaded);
        }

        private static readonly double[] FadeOpacity = { 1, 0.5, 0.25, 0.1 };

        /// <summary>Колір теми з прозорістю для далеких слів біжучого рядка.</summary>
        private System.Windows.Media.Brush Faded(string key, int level)
        {
            var color = (TryFindResource(key) as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Gray;
            color.A = (byte)(color.A * FadeOpacity[level]);
            var b = new System.Windows.Media.SolidColorBrush(color);
            b.Freeze();
            return b;
        }

        private void OnThemeChanged()
        {
            BuildLegend();
            if (session != null) Render(); // бліді слова пофарбовані напряму, їх треба перемалювати
        }

        private Run currentRun;
        private TypingSession tickerSession;

        /// <summary>
        /// Біжучий рядок: зсуваємо текст так, щоб поточна літера стояла по центру.
        /// Зсув плавний; на початку нової вправи — миттєвий.
        /// </summary>
        private void UpdateTicker()
        {
            if (currentRun == null || !currentRun.ContentStart.IsInSameDocument(TickerText.ContentStart)) return;
            TickerText.UpdateLayout();
            var start = currentRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            var end = currentRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (start.IsEmpty || end.IsEmpty || double.IsInfinity(start.Left) || double.IsInfinity(end.Right)) return;

            // Вертикально центруємо рядок у смузі
            Canvas.SetTop(TickerText, Math.Max(0, (TickerHost.Height - TickerText.ActualHeight) / 2));

            double center = (start.Left + end.Right) / 2;
            double x = Math.Round(TickerHost.ActualWidth / 2 - center);
            bool jump = tickerSession != session || session.Pos == 0;
            tickerSession = session;
            if (jump)
            {
                TickerShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                TickerShift.X = x;
            }
            else
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(x, TimeSpan.FromMilliseconds(110))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                };
                TickerShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
            }
        }

        /// <summary>
        /// У невеликому вікні показуємо 2 рядки тексту (інакше 3) і прокручуємо так,
        /// щоб поточний символ був видно. Звільнене місце дістається клавіатурі.
        /// </summary>
        private void UpdateScroll()
        {
            if (session == null || session.Finished) return;
            bool compact = ActualHeight > 0 && ActualHeight < 600;
            if (AppConfig.Current.SingleLine)
            {
                UpdateTicker();
                Legend.Visibility = AppConfig.Current.ShowFingerColors && !compact ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            int maxLines = compact ? 2 : 3;
            double lh = SampleText.LineHeight;
            if (lh <= 0) return;

            double total = SampleText.ActualHeight;
            if (total <= lh * maxLines + 1)
            {
                SampleHost.Height = double.NaN;
                SampleHost.ScrollToVerticalOffset(0);
            }
            else
            {
                SampleHost.Height = lh * maxLines;
                SampleText.UpdateLayout();
                double top = 0;
                if (currentRun != null && currentRun.ContentStart.IsInSameDocument(SampleText.ContentStart))
                {
                    var rect = currentRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.IsEmpty || double.IsNaN(rect.Top) || double.IsInfinity(rect.Top)) return; // розкладка ще не готова
                    top = rect.Top;
                }
                int line = Math.Max(0, (int)Math.Floor((top + 2) / lh));
                // 3 рядки: поточний — другий (видно, що вже набрано); 2 рядки: поточний — перший.
                int first = maxLines >= 3 ? Math.Max(0, line - 1) : line;
                int totalLines = (int)Math.Round(total / lh);
                first = Math.Max(0, Math.Min(first, totalLines - maxLines));
                SampleHost.ScrollToVerticalOffset(first * lh);
            }

            Legend.Visibility = AppConfig.Current.ShowFingerColors && !compact ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateStats()
        {
            if (session == null) return;
            var unit = AppConfig.Current.SpeedUnit;
            SpeedLabel.Text = TypingSession.SpeedLabel(unit);
            StatSpeed.Text = session.Started && session.Pos > 0 ? TypingSession.FormatSpeed(session.Cpm, unit) : "—";
            StatAcc.Text = session.Started ? Math.Floor(session.Accuracy) + "%" : "—";
            StatErr.Text = session.Errors.ToString();
            StatTime.Text = TypingSession.FormatTime(session.ElapsedMs);
        }

        private void ShowResult()
        {
            var c = AppConfig.Current;
            var unit = c.SpeedUnit;
            double acc = session.Accuracy;
            var next = Course.Next(lesson);

            if (IsEndless)
                ResultTitle.Text = onBreak
                    ? $"{TypingSession.FormatTime(session.ElapsedMs)} позаду — перепочиньте. Продовжити далі?"
                    : $"Набрано {session.Pos} знаків за {TypingSession.FormatTime(session.ElapsedMs)}";
            else if (lesson.IsFree)
                ResultTitle.Text = acc >= 95 ? "Вправу завершено — чудова точність" : "Вправу завершено";
            else if (lastPassed)
                ResultTitle.Text = next != null ? $"Урок {lesson.Code} зараховано — далі «{next.Title}»" : $"Урок {lesson.Code} зараховано — курс пройдено";
            else
                ResultTitle.Text = $"Урок {lesson.Code} ще не зараховано";

            ResultDetails.Text =
                $"{TypingSession.FormatSpeed(session.Cpm, unit)} {TypingSession.SpeedLabel(unit)}  ·  " +
                $"точність {acc:0.#}%  ·  помилок {session.Errors}  ·  час {TypingSession.FormatTime(session.ElapsedMs)}";
            if (session.FixMode) ResultDetails.Text += $"  ·  виправлень {session.Corrections}";
            if (session.ShiftOk + session.ShiftWrong > 0)
                ResultDetails.Text += $"  ·  правильний Shift {session.ShiftOk} з {session.ShiftOk + session.ShiftWrong}";

            var worst = session.WorstChars(3).Select(KeyboardLayout.DisplayName).ToList();
            var worstText = worst.Count > 0 ? "Найбільше помилок: " + string.Join(", ", worst) : "Без жодної помилки";
            if (!lesson.IsFree && !lastPassed)
            {
                var need = $"Потрібно: точність від {c.UnlockAccuracy:0}%";
                if (lesson.MinCpm > 0) need += $" і швидкість від {TypingSession.FormatSpeed(lesson.MinCpm, unit)} {TypingSession.SpeedLabel(unit)}";
                worstText = need + ".  " + worstText;
            }
            if (IsEndless)
            {
                // Підсумок усього заходу: що відкрилось і що стало вивченим від його початку.
                string lay = lesson.Layout;
                string open = DynamicTrainer.Letters(lay), fresh = DynamicTrainer.Fresh(lay);
                int words = DynamicTrainer.MasteredWords(lay);
                var parts = new System.Collections.Generic.List<string>
                {
                    open.Length > startLetters
                        ? $"Відкрито літер і знаків: {startLetters} → {open.Length} з {DynamicTrainer.LetterOrder(lay).Length}"
                        : $"Відкрито літер і знаків: {open.Length} з {DynamicTrainer.LetterOrder(lay).Length}",
                    $"вивчено слів за цей раз: {Math.Max(0, words - startWords)} (усього {words})",
                };
                if (fresh.Length > 0) parts.Add("ще закріплюємо: " + string.Join(" ", fresh.Select(DynamicTrainer.SymbolName)));
                var focus = DynamicTrainer.Focus(lay);
                if (focus.Count > 0) parts.Add("на донавчанні: " + string.Join(", ", focus));
                worstText = string.Join(".  ", parts) + ".  " + worstText;
            }
            else if (lesson.Kind == LessonKind.Dynamic)
            {
                // У динамічному режимі головне — що стало вивченим і що пішло на донавчання.
                var summary = Mastery.LastReport.Summary();
                var focus = DynamicTrainer.Focus(lesson.Layout);
                var parts = new System.Collections.Generic.List<string>();
                if (summary.Length > 0) parts.Add(summary);
                if (focus.Count > 0) parts.Add("На донавчанні: " + string.Join(", ", focus));
                string open = DynamicTrainer.Letters(lesson.Layout), fresh = DynamicTrainer.Fresh(lesson.Layout);
                if (open.Length < DynamicTrainer.LetterOrder(lesson.Layout).Length)
                    parts.Add(fresh.Length > 0
                        ? $"Відкрито літер і знаків: {open.Length}, ще закріплюємо: {string.Join(" ", fresh.Select(DynamicTrainer.SymbolName))}"
                        : $"Відкрито літер і знаків: {open.Length}");
                parts.Add($"Вивчено {DynamicTrainer.MasteredWords(lesson.Layout)} з {DynamicTrainer.TotalWords(lesson.Layout)} слів");
                worstText = string.Join(".  ", parts) + ".";
            }
            ResultWorst.Text = worstText;

            // Урок без зупинки: на перерві — «Продовжити далі» і «Завершити»; після завершення — лише «Продовжити далі».
            NextLabel.Text = IsEndless ? "Продовжити далі" : lastPassed && next != null ? "Наступний урок" : "Новий текст";
            RestartLabel.Text = onBreak ? "Завершити" : "Ще раз";
            RestartButton.Visibility = IsEndless && !onBreak ? Visibility.Collapsed : Visibility.Visible;
            HintText.Text = onBreak ? "Enter — продовжити далі, Esc — завершити урок"
                : IsEndless ? "Enter — продовжити далі"
                : lastPassed && next != null
                ? "Enter — наступний урок, Esc — повторити цей текст"
                : "Enter — новий текст, Esc — повторити цей текст";
            DebugLog.Write("RESULT", $"{ResultTitle.Text}\t{ResultDetails.Text}\t{ResultWorst.Text}");
            UpdateProgress();
        }

        private void UpdateHint()
        {
            if (session == null || session.Finished || onBreak) return;
            if (session.Uncorrected > 0)
            {
                HintText.Text = $"Невиправлених помилок: {session.Uncorrected}. Зітріть до помилки клавішею Backspace (правий мізинець) і наберіть правильно";
                return;
            }
            if (Kbd.TargetId == 0 || session.Current == null) { HintText.Text = ""; return; }

            var hint = $"Наступна: {KeyboardLayout.DisplayName(session.Current.Value)} — {KeyboardLayout.FingerName(KeyboardLayout.FingerOf(Kbd.TargetId))}";
            if (note != null && session.Pos >= notePos && session.Pos - notePos <= 15) hint = note;
            else note = null;
            if (Kbd.TargetShiftId != 0)
                hint += Kbd.TargetShiftId == KeyboardLayout.RShift ? " + правий Shift" : " + лівий Shift";
            if (IsEndless) hint += "   ·   Esc — завершити";
            HintText.Text = hint;
        }

        private void CheckWindowsLayout()
        {
            if (session == null) return;
            bool ok = lesson.Kind == LessonKind.Fn || OsInfo.WindowsLayoutMatches(session.Layout);
            var notes = new System.Collections.Generic.List<string>();
            if (!ok)
                notes.Add((session.Layout == "en" ? "Розкладка Windows зараз не англійська." : "Розкладка Windows зараз не українська.")
                    + " Тренажер усе одно правильно зарахує натискання, але для звички перемкніть її (Win+Пробіл або Alt+Shift).");
            if (AppConfig.Current.Voice && !voiceAvailable)
                notes.Add(session.Layout == "en"
                    ? "Озвучення не працює: у Windows немає англійського голосу."
                    : "Озвучення не працює: у Windows немає українського голосу. Як його додати — на вкладці «Налаштування».");

            LayoutWarn.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (notes.Count > 0)
            {
                LayoutWarnText.Text = string.Join("  ", notes);
                LayoutWarn.ToolTip = string.Join("\n", notes);
            }
        }

        // Наявність голосу перевіряємо рідко: це звернення до системи мовлення.
        private bool voiceAvailable = true;

        private void RefreshVoice()
        {
            voiceAvailable = !AppConfig.Current.Voice || session == null || Speech.Available(session.Layout);
        }

        private void ToggleVoice(bool on)
        {
            AppConfig.Update(c => c.Voice = on);
            RefreshVoice();
            CheckWindowsLayout();
            if (on) SpeakAhead(true); else Speech.Stop();
        }

        private void BuildLegend()
        {
            Legend.Children.Clear();
            Legend.Visibility = AppConfig.Current.ShowFingerColors ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < LegendNames.Length; i++)
            {
                var swatch = new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                    Background = KeyboardView.FingerFill(i), VerticalAlignment = VerticalAlignment.Center,
                };
                var label = new TextBlock { Text = LegendNames[i], Margin = new Thickness(6, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(StyleProperty, "Muted");
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
                item.Children.Add(swatch);
                item.Children.Add(label);
                Legend.Children.Add(item);
            }
        }

        private void UpdateProgress()
        {
            if (session == null || !(ProgressFill.Parent is FrameworkElement track)) return;
            double part = session.Text.Length == 0 ? 0 : (double)session.Pos / session.Text.Length;
            if (IsEndless && !session.Finished)
            {
                // Кінця немає, тому смужка показує, скільки лишилось до відкриття наступної літери
                // (найслабша з літер «у роботі»), а коли всі літери відкрито — рівень складності слів.
                string fresh = DynamicTrainer.Fresh(lesson.Layout);
                if (fresh.Length > 0)
                {
                    var keys = Mastery.KeysOf(lesson.Layout);
                    double worst = fresh.Min(ch => keys.TryGetValue(ch.ToString(), out var it) ? it.Score : Mastery.StartScore);
                    part = Math.Max(0, Math.Min(1, (worst - Mastery.StartScore) / (Mastery.MasteredScore - Mastery.StartScore)));
                }
                else part = (double)DynamicTrainer.Level(lesson.Layout) / DynamicTrainer.Levels;
            }
            ProgressFill.Width = track.ActualWidth * part;
        }
    }
}
