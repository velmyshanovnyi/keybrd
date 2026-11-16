using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Динамічне навчання: програма сама вирішує, що давати далі.
    ///
    ///   • Починаємо з двох найпростіших літер (А і О під вказівними пальцями) і їхніх комбінацій.
    ///     Коли всі відкриті літери «в пальцях» (палець знаходить клавішу одразу, без помилок і пауз),
    ///     відкривається наступна — по одній.
    ///   • З відкритих літер складаються комбінації з повторами («аоа», «ввал»), найчастіші склади мови
    ///     і справжні слова — від простих до складних. Нова літера трапляється найчастіше.
    ///   • Слово, яке кілька разів набрано чисто й механічно, стає «вивченим» і більше не показується;
    ///     зрідка воно повертається на перевірку.
    ///   • Сполучення, на яких були помилки чи паузи, йдуть на донавчання: саме сполучення двічі,
    ///     а далі — простіші слова, де воно є, з повторами.
    /// </summary>
    public static class DynamicTrainer
    {
        public const int Levels = 20;
        private const int NewWordsPerExercise = 7;   // щонайменше стільки ще не вивчених слів «в роботі»
        private const int MaxWordsPerExercise = 18;  // і не більше стількох (для зовсім коротких слів)
        private static readonly Random Rnd = new Random();
        private static readonly Dictionary<string, string[]> OrderedCache = new Dictionary<string, string[]>();

        // ---------- порядок літер ----------

        /// <summary>
        /// У якому порядку відкриваються літери: основний ряд від вказівних пальців до мізинців,
        /// потім верхній і нижній ряди — так само, як у курсі уроків.
        /// </summary>
        public static string LetterOrder(string layout)
            => (layout == "en" ? "fjdkslaghrueityowqpvmbncxz'" : "аовлідфжпрєенкгушцщйзхїмтиьсбчюя'ґ") + Extras;

        /// <summary>Після літер: крапка, кома, велика літера (Shift), знак питання, знак оклику, тире.</summary>
        public static readonly string Extras = ".," + SentenceBuilder.ShiftKey + "?!-";

        /// <summary>Назва символу для людини: «⇧» показуємо як Shift.</summary>
        public static string SymbolName(char c) => c == SentenceBuilder.ShiftKey ? "Shift" : char.ToUpper(c).ToString();

        /// <summary>Відкриті літери. Нова додається, коли всі попередні вивчені; назад літери не закриваються.</summary>
        public static string Letters(string layout)
        {
            string order = LetterOrder(layout);
            int count = Mastery.Unlocked.TryGetValue(layout, out var n) ? n : 0;
            count = Math.Max(2, Math.Min(order.Length, count));
            if (count < order.Length && order.Substring(0, count).All(c => Mastery.IsKeyMastered(layout, c))) count++;
            if (!Mastery.Unlocked.TryGetValue(layout, out var saved) || saved != count)
            {
                Mastery.Unlocked[layout] = count;
                Mastery.Save();
                DebugLog.Write("LETTERS", $"{count}\t{order.Substring(0, count)}");
            }
            return order.Substring(0, count);
        }

        /// <summary>Відкриті літери, які ще не «в пальцях» — на них робиться наголос.</summary>
        /// <remarks>
        /// Одна випадкова помилка на вже вивченій літері не повертає її «в роботу»: після перших
        /// ~14 натискань літера вважається новою знову лише тоді, коли оцінка впала нижче 0,6.
        /// </remarks>
        public static string Fresh(string layout)
        {
            var keys = Mastery.KeysOf(layout);
            return new string(Letters(layout).Where(c =>
            {
                if (!keys.TryGetValue(c.ToString(), out var it)) return true;
                if (it.Mastered) return false;
                return it.Attempts < 14 || it.Score < 0.6;
            }).ToArray());
        }

        public static bool AllLettersOpen(string layout) => Letters(layout).Length >= LetterOrder(layout).Length;

        // ---------- слова за складністю ----------

        /// <summary>Слова словника від найпростішого до найскладнішого.</summary>
        public static string[] Ordered(string layout)
        {
            if (OrderedCache.TryGetValue(layout, out var cached)) return cached;
            var m = LanguageModel.For(layout);
            var list = m.Words
                .Where(w => w.Length >= 2 && w.Length <= 14 && !w.Any(char.IsUpper) && w.All(c => char.IsLetter(c) || c == '\''))
                .Distinct()
                .Select(w => new { w, c = Complexity(m, layout, w) })
                .OrderBy(x => x.c).ThenBy(x => x.w, StringComparer.Ordinal)
                .Select(x => x.w)
                .ToArray();
            return OrderedCache[layout] = list;
        }

        /// <summary>Складність слова: довжина + «ціна» кожної клавіші + рідкісність сполучень.</summary>
        private static double Complexity(LanguageModel m, string layout, string w)
        {
            double c = w.Length;
            foreach (var ch in w)
            {
                if (!KeyboardLayout.Find(layout, ch, out var id, out var shift)) { c += 2; continue; }
                int row = KeyboardLayout.RowOf(id);
                c += row == 2 ? 0 : row == 1 ? 0.5 : row == 3 ? 0.7 : 1.2;
                var finger = KeyboardLayout.FingerOf(id);
                if (finger == Finger.LPinky || finger == Finger.RPinky) c += 0.3;
                if (shift) c += 1;
            }
            for (int i = 0; i + 1 < w.Length; i++) c += m.BigramRarity(w.Substring(i, 2)) * 0.3;
            if (m.IsFrequent(w)) c -= 2; // часті слова потрібні раніше
            return c;
        }

        // ---------- прогрес ----------

        public static int MasteredWords(string layout)
        {
            var known = Mastery.WordsOf(layout);
            return Ordered(layout).Count(w => known.TryGetValue(w, out var it) && it.Mastered);
        }

        public static int TotalWords(string layout) => Ordered(layout).Length;

        /// <summary>
        /// Слово «відоме», якщо воно вивчене або ще не траплялось, але всі його пари літер уже механічні:
        /// рухи для нього вже є в пальцях, тому показувати його на навчання не треба.
        /// Слово, на якому були помилки чи паузи, відомим не вважається, доки не буде вивчене.
        /// </summary>
        private static bool IsKnown(string w, Dictionary<string, MasteryItem> words, Dictionary<string, MasteryItem> grams)
        {
            if (words.TryGetValue(w, out var it)) return it.Mastered;
            for (int i = 0; i + 1 < w.Length; i++)
                if (!grams.TryGetValue(w.Substring(i, 2), out var g) || !g.Mastered) return false;
            return true;
        }

        /// <summary>Скільки слів уже не потребують навчання: вивчені плюс ті, що складаються з вивчених сполучень.</summary>
        public static int KnownWords(string layout)
        {
            var words = Mastery.WordsOf(layout);
            var grams = Mastery.GramsOf(layout);
            return Ordered(layout).Count(w => IsKnown(w, words, grams));
        }

        /// <summary>Рівень 1…20: наскільки далеко в списку складності відомо щонайменше 80% слів.</summary>
        public static int Level(string layout)
        {
            var ordered = Ordered(layout);
            if (ordered.Length == 0) return 1;
            var words = Mastery.WordsOf(layout);
            var grams = Mastery.GramsOf(layout);
            int known = 0, frontier = 0;
            for (int i = 0; i < ordered.Length; i++)
            {
                if (IsKnown(ordered[i], words, grams)) known++;
                if (known >= (i + 1) * 0.8) frontier = i + 1;
            }
            return Math.Min(Levels, 1 + frontier * Levels / ordered.Length);
        }

        /// <summary>Сполучення, які зараз на донавчанні (лише з відкритих літер).</summary>
        public static List<string> Focus(string layout)
        {
            string letters = Letters(layout);
            return Mastery.WeakGrams(layout, 12).Where(g => g.All(c => letters.IndexOf(c) >= 0)).Take(3).ToList();
        }

        /// <summary>Короткий опис стану для заголовка вправи.</summary>
        public static string Status(string layout)
        {
            string letters = Letters(layout), fresh = Fresh(layout);
            var parts = new List<string>();
            if (letters.Length < LetterOrder(layout).Length)
            {
                parts.Add($"літер і знаків {letters.Length} з {LetterOrder(layout).Length}");
                if (fresh.Length > 0) parts.Add("у роботі: " + string.Join(" ", fresh.Select(SymbolName)));
            }
            parts.Add($"рівень {Level(layout)} з {Levels}");
            int due = Mastery.DueCount(layout);
            if (due > 0) parts.Add($"на повторенні: {due}");
            var focus = Focus(layout);
            if (focus.Count > 0) parts.Add("донавчання: " + string.Join(", ", focus));
            return string.Join(" · ", parts);
        }

        // ---------- вправа ----------

        public static string Generate(Lesson l) => Generate(l.Layout, l.Length, true);

        /// <param name="intro">Чи починати з розминки на нові літери (у режимі без зупинки — лише коли з'явилась нова літера).</param>
        public static string Generate(string layout, int length, bool intro)
        {

            var m = LanguageModel.For(layout);
            // Відкриті символи: літери, а після них — розділові знаки і Shift.
            string symbols = Letters(layout);
            string freshAll = Fresh(layout);
            string letters = new string(symbols.Where(c => char.IsLetter(c) || c == '\'').ToArray());
            string fresh = new string(freshAll.Where(c => char.IsLetter(c) || c == '\'').ToArray());
            bool sentences = symbols.IndexOf('.') >= 0;   // усі літери відкрито — далі друкуємо реченнями
            string drillLetters = fresh.Length > 0 ? fresh : letters;
            bool hasVowel = letters.Any(m.IsVowel);

            var known = Mastery.WordsOf(layout);
            bool Allowed(string w) => w.All(c => letters.IndexOf(c) >= 0);

            // Слова лише з відкритих літер, від простих до складних; з новою літерою — першими.
            var gramStates = Mastery.GramsOf(layout);
            var allowed = Ordered(layout).Where(Allowed).ToList();
            // Не показуємо ні вивчені слова, ні ті, що складаються з уже механічних сполучень.
            var unmastered = allowed.Where(w => !IsKnown(w, known, gramStates))
                                    .OrderBy(w => fresh.Length > 0 && w.Any(c => fresh.IndexOf(c) >= 0) ? 0 : 1)
                                    .ToList();
            int freshWords = fresh.Length == 0 ? 0 : unmastered.Count(w => w.Any(c => fresh.IndexOf(c) >= 0));

            // 1. Слова «в роботі»: слабкі (з помилками) + найпростіші ще не відомі. Кожне — двічі за порцію.
            var active = Mastery.WeakWords(layout, 3).Where(w => allowed.Contains(w)).ToList();
            int chars = active.Sum(w => w.Length + 1);
            foreach (var w in unmastered)
            {
                if (active.Count >= MaxWordsPerExercise) break;
                if (active.Count >= NewWordsPerExercise && chars * 2 >= length) break;
                if (active.Contains(w)) continue;
                active.Add(w);
                chars += w.Length + 1;
            }

            // 2. Донавчання: слабкі сполучення і простіші слова з ними.
            var focus = Focus(layout);
            var focusWords = new Dictionary<string, List<string>>();
            foreach (var g in focus) focusWords[g] = allowed.Where(w => w.Contains(g)).Take(10).ToList();

            // 3. Інтервальне повторення: вивчені слова, яким настав час перевірки, і слова
            //    зі сполученнями на перевірці. Кожне — один раз за порцію, разом до третини порції.
            var review = Review(layout, allowed, length / 3);

            // 4. Моторика: комбінації з повторами, часті склади й вимовні псевдослова з відкритих літер.
            var syllables = m.TopNgrams(2, letters, drillLetters, 6).Concat(m.TopNgrams(3, letters, drillLetters, 4)).ToList();
            string Drill()
            {
                double r = Rnd.NextDouble();
                // Псевдослова — лише поки справжніх слів з новою літерою замало.
                if (hasVowel && letters.Length >= 4 && freshWords < 5 && r < 0.4)
                {
                    var p = m.Pseudo(letters, drillLetters);
                    if (p != null) return p;
                }
                // Комбінації з повторами — на перших літерах; далі вистачає складів і псевдослів.
                if (syllables.Count >= 3 && (letters.Length > 4 || r < 0.6)) return syllables[Rnd.Next(syllables.Count)];
                return Combo(drillLetters, letters);
            }

            var sb = new StringBuilder();
            string prev = null, prev2 = null;
            void Append(string t)
            {
                if (string.IsNullOrEmpty(t) || (t == prev && t == prev2)) return;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
                prev2 = prev; prev = t;
            }

            // Початок: нова літера кілька разів, її сполучення з уже знайомими, часті склади двічі.
            if (intro && fresh.Length > 0 && fresh.Length <= 3)
            {
                foreach (var k in fresh) Append(new string(k, 3));
                if (fresh.Length >= 2)
                    for (int i = 0; i + 1 < fresh.Length; i++) { Append($"{fresh[i]}{fresh[i + 1]}"); Append($"{fresh[i + 1]}{fresh[i]}"); }
                else
                {
                    char k = fresh[0];
                    foreach (var other in letters.Where(c => c != k).Take(2)) { Append($"{k}{other}"); Append($"{other}{k}"); }
                }
                foreach (var s in syllables.Take(4)) { Append(s); Append(s); }
            }

            // Щойно відкритий розділовий знак (або Shift): коротка розминка, щоб запам'ятати, де він.
            // Знак щоразу стоїть після слова; кілька знаків підряд не буває ніколи.
            char newest = symbols[symbols.Length - 1];
            if (intro && Extras.IndexOf(newest) >= 0 && freshAll.IndexOf(newest) >= 0)
            {
                string[] w = layout == "en" ? new[] { "yes", "no", "here", "there" } : new[] { "так", "ні", "тут", "там" };
                string[] q = layout == "en" ? new[] { "who", "what", "where", "how" } : new[] { "хто", "що", "де", "як" };
                switch (newest)
                {
                    case '.': foreach (var x in w) Append(x + "."); break;
                    case ',': Append($"{w[0]}, {w[1]}, {w[2]}, {w[3]}."); Append($"{q[0]}, {q[1]}, {q[2]}, {q[3]}."); break;
                    case '?': foreach (var x in q) Append(x + "?"); break;
                    case '!': foreach (var x in w) Append(x + "!"); break;
                    case '-': Append($"{w[0]} - {w[1]}, {w[2]} - {w[3]}."); Append($"{q[0]} - {q[1]}, {q[2]} - {q[3]}."); break;
                    default: // Shift
                        foreach (var x in w.Concat(q)) Append(char.ToUpper(x[0]) + x.Substring(1) + ".");
                        break;
                }
            }

            // Слабкі сполучення: пара літер двічі і два простих слова з нею, теж двічі.
            foreach (var g in focus.Take(2))
            {
                if (g.Length == 2) { Append(g); Append(g); }
                foreach (var w in focusWords[g].OrderBy(_ => Rnd.Next()).Take(2)) { Append(w); Append(w); }
            }

            // Основна частина. Поки справжніх слів мало (перші літери) — більше комбінацій і складів.
            var bag = active.OrderBy(_ => Rnd.Next()).Concat(active.OrderBy(_ => Rnd.Next())).ToList();
            int next = 0;
            // Склади й комбінації потрібні, поки закріплюється нова літера або бракує справжніх слів.
            double drillShare = active.Count >= NewWordsPerExercise ? (fresh.Length > 0 ? 0.2 : 0)
                              : active.Count >= 3 ? 0.45 : 0.8;
            // Слова для речень: з донавчання і «в роботі». Список поповнюється в міру використання.
            var targets = new List<string>();
            void Refill()
            {
                // Слова на повторенні йдуть у речення першими.
                for (int k = 0; k < 2 && review.Count > 0; k++) { targets.Add(review[0]); review.RemoveAt(0); }
                if (focus.Count > 0)
                {
                    var list = focusWords[focus[Rnd.Next(focus.Count)]];
                    if (list.Count > 0) targets.Add(list[Rnd.Next(list.Count)]);
                }
                for (int k = 0; k < 4 && bag.Count > 0; k++)
                {
                    if (next >= bag.Count) { bag = active.OrderBy(_ => Rnd.Next()).ToList(); next = 0; }
                    targets.Add(bag[next++]);
                }
            }

            bool lastFrame = false;   // двох рамок «… слово X» поспіль не даємо
            int guard = 0;
            while (sb.Length < length && guard++ < 600)
            {
                // Речення: завжди, коли відкрито крапку; до того — частина тексту короткими фразами без знаків.
                if (sentences || (letters.Length >= 8 && fresh.Length == 0 && Rnd.NextDouble() < 0.4)
                              || (letters.Length >= 8 && Rnd.NextDouble() < 0.25))
                {
                    if (targets.Count < 3) Refill();
                    var sentence = SentenceBuilder.Build(layout, letters, symbols, freshAll, targets,
                        !lastFrame, w => IsKnown(w, known, gramStates), out bool frame);
                    if (sentence != null) { lastFrame = frame; Append(sentence); continue; }
                    if (sentences && targets.Count > 0)
                    {
                        // Речення не склалося — слово окремим «реченням», щоб текст лишався схожим на людський.
                        string word = targets[0];
                        targets.RemoveAt(0);
                        if (symbols.IndexOf(SentenceBuilder.ShiftKey) >= 0) word = char.ToUpper(word[0]) + word.Substring(1);
                        Append(word + ".");
                        continue;
                    }
                }

                double r = Rnd.NextDouble();
                if (focus.Count > 0 && r < 0.2)
                {
                    var g = focus[Rnd.Next(focus.Count)];
                    var list = focusWords[g];
                    Append(list.Count > 0 ? list[Rnd.Next(list.Count)] : g);
                }
                else if (review.Count > 0 && r > 0.65)
                {
                    Append(review[0]);
                    review.RemoveAt(0);
                }
                else if (bag.Count == 0 || Rnd.NextDouble() < drillShare)
                {
                    Append(Drill());
                }
                else
                {
                    if (next >= bag.Count) { bag = active.OrderBy(_ => Rnd.Next()).ToList(); next = 0; }
                    Append(bag[next++]);
                }
            }
            return sb.ToString();
        }

        /// <summary>Слова на повторення сьогодні: ті, чий час настав, і по одному слову на кожне сполучення на перевірці.</summary>
        public static List<string> Review(string layout, List<string> allowed, int maxChars)
        {
            var set = new HashSet<string>(allowed);
            var list = Mastery.DueWords(layout, 40).Where(set.Contains).ToList();
            foreach (var g in Mastery.DueGrams(layout, 12))
            {
                var w = allowed.FirstOrDefault(x => x.Contains(g) && !list.Contains(x));
                if (w != null) list.Add(w);
            }
            var result = new List<string>();
            int chars = 0;
            foreach (var w in list)
            {
                if (chars + w.Length + 1 > maxChars) break;
                result.Add(w);
                chars += w.Length + 1;
            }
            return result;
        }

        /// <summary>
        /// Комбінація з 2–4 літер, де одна літера може повторюватись: «аоа», «оо», «ввал».
        /// Нові літери трапляються частіше за вже знайомі.
        /// </summary>
        private static string Combo(string fresh, string letters)
        {
            int len = Rnd.Next(2, 5);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                string src = Rnd.NextDouble() < 0.6 ? fresh : letters;
                char c = src[Rnd.Next(src.Length)];
                // дубль попередньої літери — частий, потрійний — ніколи
                for (int attempt = 0; attempt < 8 && sb.Length >= 2 && sb[sb.Length - 1] == c && sb[sb.Length - 2] == c; attempt++)
                    c = letters[Rnd.Next(letters.Length)];
                if (sb.Length >= 2 && sb[sb.Length - 1] == c && sb[sb.Length - 2] == c) break;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
