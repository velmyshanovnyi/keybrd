using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Вступний тест: за одним текстом визначає, які літери людина вже знає, і дозволяє не починати з нуля.
    /// Літера «знайома», якщо в тесті на ній не було помилок і пауз, помітно довших за власний темп.
    /// </summary>
    public static class Placement
    {
        public const double MinAccuracy = 90, MinCpm = 60;

        /// <summary>Текст тесту: звичайні речення плюс слова, щоб кожна літера трапилась щонайменше тричі.</summary>
        public static string Text(string layout, int length)
        {
            var rnd = new Random();
            var sb = new StringBuilder();
            foreach (var s in ExerciseGenerator.Sentences(layout).OrderBy(_ => rnd.Next()))
            {
                if (sb.Length >= length * 0.6) break;
                var t = ExerciseGenerator.NormalizeCustom(s, layout);
                if (t.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }

            string letters = new string(DynamicTrainer.LetterOrder(layout).Where(c => char.IsLetter(c) || c == '\'').ToArray());
            var words = LanguageModel.For(layout).Words
                .Where(w => w.Length >= 2 && w.Length <= 8 && !w.Any(char.IsUpper) && w.All(c => letters.IndexOf(c) >= 0))
                .OrderBy(_ => rnd.Next()).ToList();
            foreach (var ch in letters)
            {
                int have = sb.ToString().ToLowerInvariant().Count(c => c == ch);
                foreach (var w in words.Where(w => w.IndexOf(ch) >= 0).Take(Math.Max(0, 3 - have)))
                {
                    sb.Append(' ').Append(w);
                }
            }
            return sb.ToString();
        }

        /// <summary>Оцінити щойно завершений тест і, якщо результат достатній, відкрити знайоме. Повертає підсумок для людини.</summary>
        public static string Apply(TypingSession s)
        {
            string lay = s.Layout, text = s.Text;
            string order = DynamicTrainer.LetterOrder(lay);

            // Власний темп у цьому тесті: медіана інтервалів усередині слів.
            var gaps = new List<double>();
            for (int i = 1; i < s.Pos && i < text.Length; i++)
                if (char.IsLetter(text[i]) && char.IsLetter(text[i - 1]) && !s.HadError[i] && s.TimeAt[i] > 0 && s.TimeAt[i] < 2000) gaps.Add(s.TimeAt[i]);
            gaps.Sort();
            double median = gaps.Count > 0 ? gaps[gaps.Count / 2] : 300;
            double slow = Math.Max(median * 1.8, 350);

            var count = new Dictionary<char, int>();
            var errors = new Dictionary<char, int>();
            var time = new Dictionary<char, List<double>>();
            void Note(char key, int i, bool timed)
            {
                count[key] = count.TryGetValue(key, out var n) ? n + 1 : 1;
                if (s.HadError[i]) errors[key] = errors.TryGetValue(key, out var e) ? e + 1 : 1;
                if (timed && !s.HadError[i] && s.TimeAt[i] > 0)
                {
                    if (!time.TryGetValue(key, out var list)) time[key] = list = new List<double>();
                    list.Add(Math.Min(s.TimeAt[i], 2000));
                }
            }
            for (int i = 0; i < s.Pos && i < text.Length; i++)
            {
                char c = text[i];
                bool inWord = i > 0 && (char.IsLetter(text[i - 1]) || text[i - 1] == '\'');
                if (char.IsLetter(c) || c == '\'')
                {
                    Note(char.ToLowerInvariant(c), i, inWord);
                    if (char.IsUpper(c)) Note(SentenceBuilder.ShiftKey, i, false);
                }
                else if (c != ' ') Note(c, i, false);
            }

            bool Known(char key)
            {
                if (!count.TryGetValue(key, out var n) || n < 2) return false;
                if (errors.ContainsKey(key)) return false;
                return !time.TryGetValue(key, out var list) || list.Average() <= slow;
            }

            var lettersOnly = order.Where(c => char.IsLetter(c) || c == '\'').ToList();
            var known = lettersOnly.Where(Known).ToList();
            var unsure = lettersOnly.Where(c => !Known(c)).ToList();

            if (s.Accuracy < MinAccuracy || s.Cpm < MinCpm)
                return $"Точність {s.Accuracy:0.#}% і швидкість {s.Cpm:0} зн/хв поки що нижчі за поріг ({MinAccuracy:0}% і {MinCpm:0} зн/хв), " +
                       "тому нічого не змінено. Радимо починати з перших уроків або з «Щоденного заняття» — програма сама поведе далі.";

            // Динамічне навчання: відкриваємо все до першого незнайомого символу за порядком.
            int prefix = 0;
            while (prefix < order.Length && Known(order[prefix])) prefix++;
            var keys = Mastery.KeysOf(lay);
            for (int i = 0; i < prefix; i++)
            {
                string k = order[i].ToString();
                if (!keys.TryGetValue(k, out var it)) keys[k] = it = new MasteryItem();
                it.Score = Math.Max(it.Score, 0.9);
                it.Attempts = Math.Max(it.Attempts, 14);
                if (it.Interval == 0) { it.Interval = Mastery.Intervals[0]; it.Due = Mastery.Today + 1; }
            }
            int before = Mastery.Unlocked.TryGetValue(lay, out var u) ? u : 0;
            Mastery.Unlocked[lay] = Math.Max(before, Math.Max(2, prefix));
            Mastery.Save();
            int open = DynamicTrainer.Letters(lay).Length;

            // Курс: зараховуємо уроки, поки всі їхні нові клавіші знайомі.
            int passed = 0;
            foreach (var l in Course.For(lay))
            {
                bool lettersLesson = l.Kind == LessonKind.Keys || l.Kind == LessonKind.Words;
                if (!lettersLesson || !l.NewKeys.All(c => Known(char.ToLowerInvariant(c)))) break;
                if (!StatsStore.Lessons.TryGetValue(l.Id, out var p)) StatsStore.Lessons[l.Id] = p = new LessonProgress();
                if (!p.Passed) { p.Passed = true; passed++; }
            }
            StatsStore.Save();
            var next = StatsStore.FirstOpen(lay);
            AppConfig.Current.SetLesson(next.Id);   // курс продовжиться з першого незарахованого уроку
            AppConfig.Current.Save();

            var parts = new List<string> { $"Знайомі літери: {known.Count} з {lettersOnly.Count}" };
            if (unsure.Count > 0) parts.Add("ще не впевнено: " + string.Join(" ", unsure.Take(12).Select(c => char.ToUpper(c).ToString())) + (unsure.Count > 12 ? "…" : ""));
            parts.Add(open >= order.Length
                ? "у динамічному навчанні відкрито всі літери й знаки"
                : $"динамічне навчання продовжить з «{DynamicTrainer.SymbolName(order[Math.Min(order.Length - 1, open - 1)])}» (відкрито {open} з {order.Length})");
            parts.Add(passed > 0 ? $"зараховано уроків курсу: {passed}, наступний — {next.Code} «{next.Title}»" : $"курс продовжується з уроку {next.Code} «{next.Title}»");
            return string.Join(".  ", parts) + ".";
        }
    }
}
