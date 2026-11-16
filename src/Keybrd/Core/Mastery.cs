using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Keybrd.Core
{
    /// <summary>Наскільки добре вивчено одне слово або сполучення літер.</summary>
    public sealed class MasteryItem
    {
        /// <summary>Оцінка 0…1: згладжене середнє останніх спроб (1 — чисто й механічно, 0 — помилка).</summary>
        public double Score = Mastery.StartScore;
        public int Attempts, Streak, Errors, LastDay;
        public double AvgMs;
        /// <summary>Інтервальне повторення: поточний інтервал у днях (0 — ще не вивчено) і день перевірки.</summary>
        public int Interval, Due;

        /// <summary>Вивчений елемент, якому настав час перевірки.</summary>
        public bool IsDue => Mastered && Interval > 0 && Due <= Mastery.Today;

        public bool Mastered => Attempts >= 1 && Score >= Mastery.MasteredScore;
        public bool Weak => Attempts >= 2 && Score < Mastery.WeakScore;
    }

    /// <summary>Що змінилося після однієї вправи.</summary>
    public sealed class MasteryReport
    {
        public readonly List<string> NewWords = new List<string>();
        public readonly List<string> NewGrams = new List<string>();
        public readonly List<string> LostWords = new List<string>();
        public readonly List<string> LostGrams = new List<string>();
        /// <summary>Скільки вивчених елементів пройшли планову перевірку.</summary>
        public int Reviewed;

        public string Summary()
        {
            var parts = new List<string>();
            if (NewWords.Count > 0) parts.Add("Вивчено слова: " + string.Join(", ", NewWords.Take(8)) + (NewWords.Count > 8 ? $" і ще {NewWords.Count - 8}" : ""));
            if (NewGrams.Count > 0) parts.Add($"вивчено сполучень: {NewGrams.Count}");
            if (LostWords.Count > 0) parts.Add("повертаються в роботу: " + string.Join(", ", LostWords.Take(5)));
            if (Reviewed > 0) parts.Add($"перевірено на повторенні: {Reviewed}");
            return string.Join(".  ", parts);
        }
    }

    /// <summary>
    /// Модель «м'язової пам'яті».
    ///
    /// Кожна спроба набрати слово чи сполучення оцінюється:
    ///   1   — без помилок і механічно: рівний ритм, без пауз на згадування;
    ///   0.5 — без помилок, але повільно або з паузою;
    ///   0   — з помилкою.
    /// Оцінка елемента — згладжене середнє цих спроб. Елемент вважається вивченим, коли оцінка
    /// сягає 0,85: це приблизно 4 чисті механічні спроби поспіль, а якщо всі сполучення слова вже
    /// вивчені — 2. Помилка відкидає оцінку назад, і елемент повертається в роботу.
    /// «Механічність» міряється відносно власного темпу людини (Pace), тому планка росте разом із навичкою.
    /// </summary>
    public static class Mastery
    {
        public const string FileName = "mastery.json";
        public const double StartScore = 0.25, MasteredScore = 0.85, WeakScore = 0.5;
        // FastAlpha = 1: якщо всі пари літер слова вже механічні і слово набрано чисто — воно вивчене з першого разу.
        private const double WordAlpha = 0.35, FastAlpha = 1.0, GramAlpha = 0.3;
        // Літера трапляється часто, тому згладжування повільніше: ~10 чистих механічних натискань до «вивчено».
        private const double KeyAlpha = 0.16;

        /// <summary>розкладка → літера → стан (моторика знаходження клавіші)</summary>
        public static readonly Dictionary<string, Dictionary<string, MasteryItem>> Keys = new Dictionary<string, Dictionary<string, MasteryItem>>();
        /// <summary>розкладка → скільки літер відкрито в динамічному навчанні (лише зростає)</summary>
        public static readonly Dictionary<string, int> Unlocked = new Dictionary<string, int>();

        public static Dictionary<string, MasteryItem> KeysOf(string layout) => Of(Keys, layout);
        public static bool IsKeyMastered(string layout, char key) => KeysOf(layout).TryGetValue(key.ToString(), out var it) && it.Mastered;

        /// <summary>розкладка → слово → стан</summary>
        public static readonly Dictionary<string, Dictionary<string, MasteryItem>> Words = new Dictionary<string, Dictionary<string, MasteryItem>>();
        /// <summary>розкладка → сполучення з 2–3 літер → стан</summary>
        public static readonly Dictionary<string, Dictionary<string, MasteryItem>> Grams = new Dictionary<string, Dictionary<string, MasteryItem>>();
        /// <summary>розкладка → типовий інтервал між клавішами всередині слова, мс</summary>
        public static readonly Dictionary<string, double> Pace = new Dictionary<string, double>();

        public static MasteryReport LastReport { get; private set; } = new MasteryReport();
        public static event Action Changed;

        private static string FilePath => DataStore.PathOf(FileName);
        /// <summary>Номер сьогоднішнього дня. Прихований параметр debugDayOffset зсуває його для тестів.</summary>
        public static int Today => (int)(DateTime.Today - new DateTime(2020, 1, 1)).TotalDays + AppConfig.Current.DebugDayOffset;

        /// <summary>Інтервали перевірки вивченого, у днях.</summary>
        public static readonly int[] Intervals = { 1, 3, 7, 21, 60 };

        private static int NextInterval(int current)
        {
            foreach (var i in Intervals) if (i > current) return i;
            return Intervals[Intervals.Length - 1];
        }

        /// <summary>Вивчені слова, яким настав час перевірки: найдавніші спочатку.</summary>
        public static List<string> DueWords(string layout, int count)
            => WordsOf(layout).Where(kv => kv.Value.IsDue)
                .OrderBy(kv => kv.Value.Due).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key).Take(count).ToList();

        public static List<string> DueGrams(string layout, int count)
            => GramsOf(layout).Where(kv => kv.Value.IsDue && kv.Key.Length == 2)
                .OrderBy(kv => kv.Value.Due).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key).Take(count).ToList();

        public static int DueCount(string layout)
            => WordsOf(layout).Count(kv => kv.Value.IsDue) + GramsOf(layout).Count(kv => kv.Value.IsDue && kv.Key.Length == 2);

        /// <summary>Скільки перевірок вивченого відбулося від запуску програми (для журналу тестів).</summary>
        public static int Reviewed { get; private set; }

        public static Dictionary<string, MasteryItem> WordsOf(string layout) => Of(Words, layout);
        public static Dictionary<string, MasteryItem> GramsOf(string layout) => Of(Grams, layout);
        public static double PaceOf(string layout) => Pace.TryGetValue(layout, out var p) ? p : 0;

        private static Dictionary<string, MasteryItem> Of(Dictionary<string, Dictionary<string, MasteryItem>> d, string layout)
        {
            if (!d.TryGetValue(layout, out var m)) d[layout] = m = new Dictionary<string, MasteryItem>();
            return m;
        }

        public static bool IsMastered(string layout, string word) => WordsOf(layout).TryGetValue(word, out var it) && it.Mastered;
        public static bool IsGramMastered(string layout, string gram) => GramsOf(layout).TryGetValue(gram, out var it) && it.Mastered;

        /// <summary>Сполучення, на яких людина помиляється або зупиняється: найгірші спочатку.</summary>
        public static List<string> WeakGrams(string layout, int count)
            => GramsOf(layout).Where(kv => kv.Value.Weak)
                .OrderBy(kv => kv.Value.Score).ThenBy(kv => kv.Key.Length).ThenByDescending(kv => kv.Value.Attempts)
                .Select(kv => kv.Key).Take(count).ToList();

        public static List<string> WeakWords(string layout, int count)
            => WordsOf(layout).Where(kv => kv.Value.Weak)
                .OrderBy(kv => kv.Value.Score).Select(kv => kv.Key).Take(count).ToList();

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'';

        // ---------- оновлення після вправи ----------

        /// <param name="from">З якої позиції тексту оцінювати (урок без зупинки оновлює модель частинами).</param>
        /// <param name="to">До якої позиції; -1 — до поточної.</param>
        public static MasteryReport Update(TypingSession s, int from = 0, int to = -1)
        {
            var rep = new MasteryReport();
            int reviewedBefore = Reviewed;
            string lay = s.Layout, text = s.Text;
            int done = Math.Min(to < 0 ? s.Pos : Math.Min(to, s.Pos), text.Length);
            from = Math.Max(0, Math.Min(from, done));

            // Час і помилки для кожної позиції тексту.
            var time = new double[text.Length];
            var err = new bool[text.Length];
            int pos = 0;
            foreach (var st in s.Strokes)
            {
                if (pos >= text.Length) break;
                time[pos] += st.Ms;
                if (st.Ok) pos++; else err[pos] = true;
            }

            // Власний темп: медіана інтервалів усередині слів.
            var gaps = new List<double>();
            for (int i = Math.Max(1, from); i < done; i++)
                if (IsWordChar(text[i]) && IsWordChar(text[i - 1]) && !err[i] && time[i] > 0 && time[i] < 2000) gaps.Add(time[i]);
            gaps.Sort();
            double sessionPace = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;
            double pace = PaceOf(lay);
            double reference = pace > 0 ? pace : sessionPace > 0 ? sessionPace : 400;

            // Механічний набір: не повільніше власного темпу (з запасом 15%) і без пауз удвічі довших за темп.
            double fluentGap = Math.Min(reference * 1.15, 500);
            double pauseGap = Math.Min(reference * 2.0, 900);

            var model = LanguageModel.For(lay);
            var words = WordsOf(lay);
            var grams = GramsOf(lay);

            // Окремі літери: чи знаходить палець клавішу одразу. Після пробілу паузу на читання не штрафуємо.
            var keys = KeysOf(lay);
            var unused = new List<string>();
            for (int i = from; i < done; i++)
            {
                char ch = text[i];
                if (IsWordChar(ch))
                {
                    bool inWord = i > 0 && IsWordChar(text[i - 1]);
                    double r = err[i] ? 0 : !inWord || time[i] <= fluentGap ? 1 : time[i] <= pauseGap ? 0.5 : 0.2;
                    Apply(keys, char.ToLowerInvariant(ch).ToString(), r, inWord ? time[i] : 0, KeyAlpha, unused, unused);
                    // Велика літера — це ще й навичка Shift; їй і розділовим знакам даємо більше часу.
                    if (char.IsUpper(ch))
                        Apply(keys, SentenceBuilder.ShiftKey.ToString(), err[i] ? 0 : time[i] <= pauseGap * 1.5 ? 1 : 0.5, 0, KeyAlpha, unused, unused);
                }
                else if (ch != ' ' && !KeyboardLayout.IsSpecial(ch))
                {
                    Apply(keys, ch.ToString(), err[i] ? 0 : time[i] <= pauseGap * 1.5 ? 1 : 0.5, 0, KeyAlpha, unused, unused);
                }
            }

            int a = from;
            while (a < done)
            {
                if (!IsWordChar(text[a])) { a++; continue; }
                int b = a;
                while (b < text.Length && IsWordChar(text[b])) b++;
                if (b > done) break; // слово не добрано до кінця
                string w = text.Substring(a, b - a).ToLowerInvariant();

                // Сполучення: оцінка другої (третьої) літери за умови попередніх.
                var pair = new double[w.Length];
                bool allPairsMastered = true;
                for (int k = 1; k < w.Length; k++)
                {
                    int p = a + k;
                    pair[k] = err[p] ? 0 : time[p] <= fluentGap ? 1 : time[p] <= pauseGap ? 0.5 : 0.2;
                    var g2 = w.Substring(k - 1, 2);
                    Apply(grams, g2, pair[k], time[p], GramAlpha, rep.NewGrams, rep.LostGrams);
                    if (!grams[g2].Mastered) allPairsMastered = false;
                    if (k >= 2)
                        Apply(grams, w.Substring(k - 2, 3), Math.Min(pair[k - 1], pair[k]), time[p], GramAlpha, rep.NewGrams, rep.LostGrams);
                }

                if (w.Length >= 2 && model.HasWord(w))
                {
                    bool anyError = false;
                    double sum = 0, max = 0;
                    for (int k = 0; k < w.Length; k++)
                    {
                        if (err[a + k]) anyError = true;
                        if (k == 0) continue; // перша літера після пробілу — це час на читання слова
                        sum += time[a + k];
                        max = Math.Max(max, time[a + k]);
                    }
                    double mean = sum / (w.Length - 1);
                    double result = anyError ? 0 : mean <= fluentGap && max <= pauseGap ? 1 : 0.5;
                    // Якщо всі сполучення слова вже механічні — слово підтверджується швидше.
                    double alpha = result == 1 && allPairsMastered ? FastAlpha : WordAlpha;
                    Apply(words, w, result, mean, alpha, rep.NewWords, rep.LostWords);
                }
                a = b;
            }

            if (sessionPace > 0) Pace[lay] = pace > 0 ? pace * 0.7 + sessionPace * 0.3 : sessionPace;

            rep.Reviewed = Reviewed - reviewedBefore;
            LastReport = rep;
            Save();
            Changed?.Invoke();
            return rep;
        }

        private static void Apply(Dictionary<string, MasteryItem> d, string key, double result, double ms, double alpha,
                                  List<string> gained, List<string> lost)
        {
            if (!d.TryGetValue(key, out var it)) d[key] = it = new MasteryItem();
            bool was = it.Mastered;
            int today = Today;
            bool wasDue = was && it.Interval > 0 && it.Due <= today;
            it.Score += alpha * (result - it.Score);
            it.Attempts++;
            it.Streak = result >= 1 ? it.Streak + 1 : 0;
            if (result <= 0) it.Errors++;
            if (ms > 0) it.AvgMs = it.AvgMs > 0 ? it.AvgMs * 0.7 + ms * 0.3 : ms;
            it.LastDay = today;
            // Інтервальне повторення: вивчене перевіряється через 1, 3, 7, 21, 60 днів.
            if (!it.Mastered) { it.Interval = 0; it.Due = 0; }
            else if (!was || it.Interval == 0) { it.Interval = Intervals[0]; it.Due = today + it.Interval; }
            else if (wasDue)
            {
                // Чисто й механічно — наступний інтервал; чисто, але повільно — той самий, з завтра.
                if (result >= 1) { it.Interval = NextInterval(it.Interval); it.Due = today + it.Interval; }
                else it.Due = today + 1;
                Reviewed++;
            }
            if (!was && it.Mastered && !gained.Contains(key)) { gained.Add(key); lost.Remove(key); }
            else if (was && !it.Mastered && !lost.Contains(key)) { lost.Add(key); gained.Remove(key); }
        }

        // ---------- файл ----------

        public static void Load()
        {
            DataStore.Resolve();
            Words.Clear(); Grams.Clear(); Keys.Clear(); Pace.Clear(); Unlocked.Clear();
            if (!File.Exists(FilePath)) return;
            try
            {
                var root = Json.Parse(File.ReadAllText(FilePath, DataStore.Utf8)) as IDictionary<string, object>;
                if (root == null) return;
                var pace = Json.GetObject(root, "pace");
                if (pace != null) foreach (var kv in pace) if (kv.Value is double v) Pace[kv.Key] = v;
                Read(Json.GetObject(root, "words"), Words);
                Read(Json.GetObject(root, "grams"), Grams);
                Read(Json.GetObject(root, "keys"), Keys);
                var unlocked = Json.GetObject(root, "unlocked");
                if (unlocked != null) foreach (var kv in unlocked) if (kv.Value is double n) Unlocked[kv.Key] = (int)n;
            }
            catch (Exception)
            {
                try { File.Copy(FilePath, DataStore.PathOf("mastery.bak.json"), true); } catch { }
                Words.Clear(); Grams.Clear(); Keys.Clear(); Pace.Clear(); Unlocked.Clear();
            }
        }

        private static void Read(IDictionary<string, object> src, Dictionary<string, Dictionary<string, MasteryItem>> dst)
        {
            if (src == null) return;
            foreach (var lay in src)
            {
                if (!(lay.Value is IDictionary<string, object> items)) continue;
                var m = dst[lay.Key] = new Dictionary<string, MasteryItem>();
                foreach (var kv in items)
                {
                    if (!(kv.Value is List<object> v) || v.Count < 6) continue;
                    double N(int i) => v[i] is double x ? x : 0;
                    var it = m[kv.Key] = new MasteryItem
                    {
                        Score = Math.Max(0, Math.Min(1, N(0))), Attempts = (int)N(1), Streak = (int)N(2),
                        AvgMs = N(3), LastDay = (int)N(4), Errors = (int)N(5),
                    };
                    if (v.Count >= 8) { it.Interval = (int)N(6); it.Due = (int)N(7); }
                    // Файл старої версії: вивчене перевіряємо наступного дня після останньої спроби.
                    if (it.Mastered && it.Interval == 0) { it.Interval = Intervals[0]; it.Due = it.LastDay + 1; }
                }
            }
        }

        // Один елемент — один рядок: [оцінка, спроб, чистих поспіль, середній інтервал мс, день, помилок]
        private static Dictionary<string, object> Write(Dictionary<string, Dictionary<string, MasteryItem>> src)
        {
            var result = new Dictionary<string, object>();
            foreach (var lay in src)
            {
                var m = new Dictionary<string, object>();
                foreach (var kv in lay.Value.OrderBy(k => k.Key, StringComparer.Ordinal))
                    m[kv.Key] = new List<object>
                    {
                        Math.Round(kv.Value.Score, 3), kv.Value.Attempts, kv.Value.Streak,
                        Math.Round(kv.Value.AvgMs), kv.Value.LastDay, kv.Value.Errors,
                        kv.Value.Interval, kv.Value.Due,
                    };
                result[lay.Key] = m;
            }
            return result;
        }

        public static void Save()
        {
            var pace = new Dictionary<string, object>();
            foreach (var kv in Pace) pace[kv.Key] = Math.Round(kv.Value);
            var root = new Dictionary<string, object>
            {
                ["version"] = 1,
                ["format"] = "слово або сполучення: [оцінка 0..1, спроб, чистих поспіль, середній інтервал мс, день, помилок, інтервал повторення в днях, день перевірки]",
                ["pace"] = pace,
                ["words"] = Write(Words),
                ["grams"] = Write(Grams),
                ["keys"] = Write(Keys),
                ["unlocked"] = Unlocked.ToDictionary(kv => kv.Key, kv => (object)kv.Value),
            };
            try { DataStore.WriteAtomic(FilePath, Json.Write(root)); } catch { }
        }

        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Copy(FilePath, DataStore.PathOf("mastery.bak.json"), true); } catch { }
            Words.Clear(); Grams.Clear(); Keys.Clear(); Pace.Clear(); Unlocked.Clear();
            LastReport = new MasteryReport();
            Save();
            Changed?.Invoke();
        }
    }
}
