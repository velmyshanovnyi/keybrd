using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Генерує текст вправи для уроку.
    ///
    /// Урок на нові клавіші будується, як у найкращих тренажерах:
    ///   1) розминка — нова клавіша кілька разів і чергування нових клавіш;
    ///   2) найчастіші склади мови з цими клавішами, кожен двічі;
    ///   3) справжні слова з новими клавішами (частина — двічі поспіль, щоб рух закріпився),
    ///      а де слів замало — вимовні псевдослова за статистикою мови.
    /// Випадкових наборів літер на кшталт «фжвфд» немає.
    /// </summary>
    public static class ExerciseGenerator
    {
        private static readonly Random Rnd = new Random();

        public static string[] Words(string layout) => LanguageModel.For(layout).Words;

        private static readonly Dictionary<string, string[]> SentenceCache = new Dictionary<string, string[]>();

        public static string[] Sentences(string layout)
        {
            if (SentenceCache.TryGetValue(layout, out var cached)) return cached;
            string text;
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream($"Keybrd.Data.sentences_{layout}.txt"))
            using (var r = new System.IO.StreamReader(s, Encoding.UTF8))
                text = r.ReadToEnd();
            return SentenceCache[layout] = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
        }

        // ---------- головне ----------

        public static string Generate(Lesson l)
        {
            switch (l.Kind)
            {
                case LessonKind.Keys: return Keys(l);
                case LessonKind.Words: return WordsText(l, false);
                case LessonKind.Capitals: return WordsText(l, true);
                case LessonKind.Sentences:
                case LessonKind.Speed: return SentencesText(l);
                case LessonKind.Digits: return Digits(l);
                case LessonKind.Symbols: return Symbols(l);
                case LessonKind.Fn: return Fn(l);
                case LessonKind.Adaptive: return Adaptive(l);
                case LessonKind.Custom: return CustomText(l);
                case LessonKind.Confusion: return ConfusionText(l);
                case LessonKind.Dynamic:
                case LessonKind.Endless: return DynamicTrainer.Generate(l);
                default: return FreeText(l);
            }
        }

        // ---------- допоміжне ----------

        private static bool Allowed(Lesson l, string word)
        {
            foreach (var ch in word)
            {
                char lower = char.ToLowerInvariant(ch);
                if (char.IsUpper(ch) && !l.Upper) return false;
                if (l.Letters.IndexOf(lower) >= 0) continue;
                if (l.Extra.IndexOf(ch) >= 0) continue;
                return false;
            }
            return true;
        }

        private static List<string> AllowedWords(Lesson l, bool allowCapitalized = false)
            => Words(l.Layout)
                .Where(w => (allowCapitalized || !w.Any(char.IsUpper)) && Allowed(l, w))
                .ToList();

        private static bool HasAny(string w, string chars) => chars.Length > 0 && w.Any(c => chars.IndexOf(char.ToLowerInvariant(c)) >= 0);

        private static string Pick(IList<string> list) => list[Rnd.Next(list.Count)];

        /// <summary>Вибір слова з перевагою частим словам мови (втричі частіше).</summary>
        private static string PickWeighted(IList<string> list, LanguageModel m)
        {
            for (int i = 0; i < 3; i++)
            {
                var w = Pick(list);
                if (i == 2 || m.IsFrequent(w)) return w;
            }
            return Pick(list);
        }

        /// <summary>Зібрати текст потрібної довжини; однакові токени дозволені двічі поспіль, але не тричі.</summary>
        private static string Build(IEnumerable<string> head, Func<string> next, int length)
        {
            var sb = new StringBuilder();
            string prev = null, prev2 = null;
            void Append(string t)
            {
                if (string.IsNullOrEmpty(t)) return;
                if (t == prev && t == prev2) return;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
                prev2 = prev; prev = t;
            }
            foreach (var t in head) Append(t);
            int guard = 0;
            while (sb.Length < length && guard++ < 2000) Append(next());
            return sb.ToString();
        }

        /// <summary>Коли з набору літер не виходить нічого вимовного (самі приголосні) — чергування клавіш: «фж жф фжф».</summary>
        private static string Combo(string fresh, string letters)
        {
            int len = Rnd.Next(2, 5);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                string src = i % 2 == 0 || letters.Length == 0 ? fresh : (Rnd.NextDouble() < 0.6 ? fresh : letters);
                char c = src[Rnd.Next(src.Length)];
                if (sb.Length >= 2 && sb[sb.Length - 1] == c && sb[sb.Length - 2] == c) c = fresh[Rnd.Next(fresh.Length)];
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------- урок на нові клавіші ----------

        private static string Keys(Lesson l)
        {
            var m = LanguageModel.For(l.Layout);
            string letters = new string(l.Letters.Where(char.IsLetter).ToArray());
            string fresh = new string(l.NewKeys.Where(c => letters.IndexOf(c) >= 0).ToArray());
            string punct = new string(l.NewKeys.Where(c => letters.IndexOf(c) < 0).ToArray());
            if (fresh.Length == 0) fresh = letters;

            // 1. Розминка: «ааа ооо ао оа»
            var head = new List<string>();
            if (fresh.Length <= 3)
            {
                foreach (var k in fresh) head.Add(new string(k, 3));
                for (int i = 0; i + 1 < fresh.Length; i++)
                {
                    head.Add($"{fresh[i]}{fresh[i + 1]}");
                    head.Add($"{fresh[i + 1]}{fresh[i]}");
                }
            }

            // 2. Найчастіші склади з новими клавішами, кожен двічі: «ва ва ло ло»
            var syllables = m.TopNgrams(2, letters, fresh, 6).Concat(m.TopNgrams(3, letters, fresh, 4)).ToList();
            foreach (var s in syllables.Take(7)) { head.Add(s); head.Add(s); }

            // 3. Слова
            var real = AllowedWords(l).Where(w => w.Length >= 2 && w.All(char.IsLetter)).ToList();
            var withNew = real.Where(w => HasAny(w, fresh)).ToList();
            bool canPseudo = letters.Any(m.IsVowel);
            string pending = null;

            // Коли складів мало (самі приголосні) — чергування клавіш замість повтору одного складу.
            string Drill() => syllables.Count >= 3 ? Pick(syllables) : Combo(fresh, letters);

            string Next()
            {
                if (pending != null) { var p = pending; pending = null; return p; }

                string w;
                bool isWord = true;
                double r = Rnd.NextDouble();
                if (r < 0.7)
                {
                    // Слово з новою клавішею: справжнє, а якщо таких мало — частково псевдослова.
                    double realShare = Math.Min(1.0, withNew.Count / 10.0);
                    if (withNew.Count > 0 && Rnd.NextDouble() < Math.Max(realShare, 0.5)) w = PickWeighted(withNew, m);
                    else
                    {
                        w = canPseudo ? m.Pseudo(letters, fresh) : null;
                        if (w == null) { w = Drill(); isWord = false; }
                    }
                }
                else if (r < 0.9)
                {
                    // Повторення вже вивченого
                    if (real.Count >= 5) w = PickWeighted(real, m);
                    else
                    {
                        w = canPseudo ? m.Pseudo(letters, "") : null;
                        if (w == null) { w = Drill(); isWord = false; }
                    }
                }
                else
                {
                    w = Drill();
                    isWord = false;
                }

                // Новий розділовий знак ставимо лише після слів; третину слів друкуємо двічі поспіль.
                if (isWord && punct.Length > 0 && w.Length >= 3 && Rnd.NextDouble() < 0.3) w += punct[Rnd.Next(punct.Length)];
                else if (w.Length >= 3 && Rnd.NextDouble() < 0.3) pending = w;
                return w;
            }

            return Build(head, Next, l.Length);
        }

        // ---------- слова ----------

        private static string WordsText(Lesson l, bool capitals)
        {
            var m = LanguageModel.For(l.Layout);
            var words = AllowedWords(l, allowCapitalized: l.Upper).Where(w => w.Length >= 2 || m.IsFrequent(w)).ToList();
            if (words.Count < 12) return Keys(l);
            var withNew = words.Where(w => HasAny(w, l.NewKeys)).ToList();
            string pending = null;

            string Next()
            {
                if (pending != null) { var p = pending; pending = null; return p; }
                var w = withNew.Count >= 4 && Rnd.NextDouble() < 0.6 ? Pick(withNew) : PickWeighted(words, m);
                if (capitals && Rnd.NextDouble() < 0.45) w = char.ToUpper(w[0]) + w.Substring(1);
                else if (!capitals && w.Length >= 4 && Rnd.NextDouble() < 0.12) pending = w;
                if (l.Extra.Contains(".") && Rnd.NextDouble() < 0.08) w += ".";
                return w;
            }
            return Build(new string[0], Next, l.Length);
        }

        /// <summary>Вільна практика: найуживаніші слова мови.</summary>
        private static string FreeText(Lesson l)
        {
            var m = LanguageModel.For(l.Layout);
            var frequent = m.Frequent.Where(w => w.Length >= 2).ToList();
            var all = m.Words.Where(w => !w.Any(char.IsUpper) && w.Length >= 3).ToList();
            if (frequent.Count < 20) frequent = all;
            return Build(new string[0], () => Rnd.NextDouble() < 0.7 ? Pick(frequent) : Pick(all), l.Length);
        }

        private static string SentencesText(Lesson l)
        {
            var all = Sentences(l.Layout);
            var ok = all.Where(s => Allowed(l, s.Replace(" ", ""))).ToList();
            if (l.Kind == LessonKind.Speed || ok.Count < 4) ok = all.ToList();
            var sb = new StringBuilder();
            var used = new HashSet<string>();
            while (sb.Length < l.Length && used.Count < ok.Count)
            {
                var s = Pick(ok);
                if (!used.Add(s)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s);
            }
            return sb.ToString();
        }

        // ---------- цифри, знаки, Fn ----------

        private static string Digits(Lesson l)
        {
            var m = LanguageModel.For(l.Layout);
            string digits = new string(l.Extra.Where(char.IsDigit).ToArray());
            string fresh = l.NewKeys;
            var words = AllowedWords(l).Where(w => w.Length >= 3).ToList();

            // Розминка: кожна нова цифра і пари сусідніх
            var head = new List<string>();
            foreach (var d in fresh) head.Add(new string(d, 2));
            for (int i = 0; i + 1 < fresh.Length; i++) head.Add($"{fresh[i]}{fresh[i + 1]}");

            string Next()
            {
                if (Rnd.NextDouble() < 0.3 && words.Count > 0) return PickWeighted(words, m);
                int len = Rnd.Next(1, 5);
                var sb = new StringBuilder();
                for (int i = 0; i < len; i++)
                    sb.Append(Rnd.NextDouble() < 0.6 ? fresh[Rnd.Next(fresh.Length)] : digits[Rnd.Next(digits.Length)]);
                return sb.ToString();
            }
            return Build(head, Next, l.Length);
        }

        private static string Symbols(Lesson l)
        {
            var m = LanguageModel.For(l.Layout);
            var words = AllowedWords(l).Where(w => w.Length >= 3).ToList();
            string fresh = l.NewKeys;
            string Next()
            {
                var w = PickWeighted(words, m);
                char s = fresh[Rnd.Next(fresh.Length)];
                switch (s)
                {
                    case '(':
                    case ')': return "(" + w + ")";
                    case '"': return "\"" + w + "\"";
                    case '-': return w + "-" + PickWeighted(words, m);
                    default: return w + s;
                }
            }
            return Build(new string[0], Next, l.Length);
        }

        private static string Fn(Lesson l)
        {
            // F1–F12 і Del. Esc не включаємо — він перезапускає вправу.
            var sb = new StringBuilder();
            char prev = '\0';
            for (int i = 0; i < 30; i++)
            {
                char c;
                do { c = (char)(KeyboardLayout.SpecialBase + 1 + Rnd.Next(13)); } while (c == prev);
                sb.Append(c);
                prev = c;
            }
            return sb.ToString();
        }

        // ---------- слабкі місця ----------

        /// <summary>
        /// «Слабкі місця»: часті склади й слова з найслабшими літерами та сполученнями.
        /// Без статистики — звичайні часті слова.
        /// </summary>
        private static string Adaptive(Lesson l)
        {
            var m = LanguageModel.For(l.Layout);
            var weakKeys = StatsStore.WeakKeys(l.Layout, 6).Where(k => char.IsLetter(k[0]) || k == "'").ToList();
            var weakPairs = StatsStore.WeakBigrams(l.Layout, 6);
            if (weakKeys.Count == 0 && weakPairs.Count == 0) return FreeText(l);

            string letters = new string(l.Letters.Where(char.IsLetter).ToArray());
            string weakLetters = string.Concat(weakKeys.Where(k => char.IsLetter(k[0])));
            var words = m.Words.Where(w => !w.Any(char.IsUpper) && w.Length >= 2).ToList();
            int Weight(string w)
            {
                int s = 0;
                foreach (var p in weakPairs) if (w.Contains(p)) s += 3;
                foreach (var k in weakKeys) if (w.Contains(k)) s += 2;
                return s;
            }
            var scored = words.Select(w => new { w, s = Weight(w) }).Where(x => x.s > 0)
                              .OrderByDescending(x => x.s).Take(100).Select(x => x.w).ToList();

            // Розминка: слабкі сполучення і найчастіші склади зі слабкими літерами, кожен двічі
            var syllables = weakPairs.Take(4).ToList();
            if (weakLetters.Length > 0) syllables.AddRange(m.TopNgrams(2, letters, weakLetters, 6).Where(s => !syllables.Contains(s)));
            var head = new List<string>();
            foreach (var s in syllables.Take(6)) { head.Add(s); head.Add(s); }

            string pending = null;
            string Next()
            {
                if (pending != null) { var p = pending; pending = null; return p; }
                double r = Rnd.NextDouble();
                string w;
                if (r < 0.75 && scored.Count > 0) w = Pick(scored);
                else if (r < 0.88 && syllables.Count > 0) return Pick(syllables);
                else w = PickWeighted(words, m);
                if (w.Length >= 3 && Rnd.NextDouble() < 0.25) pending = w;
                return w;
            }
            return Build(head, Next, l.Length);
        }

        // ---------- плутанина ----------

        /// <summary>
        /// «Плутанина»: для найчастіших пар «треба → натиснули» — обидві літери по черзі, контрастні склади,
        /// слова з однією з літер упереміш і слова з обома. Без даних — звичайні часті слова.
        /// </summary>
        private static string ConfusionText(Lesson l)
        {
            var pairs = Confusions.LetterPairs(l.Layout, 3);
            if (pairs.Count == 0) return FreeText(l);
            var m = LanguageModel.For(l.Layout);
            var words = m.Words.Where(w => !w.Any(char.IsUpper) && w.Length >= 2 && w.Length <= 9).ToList();
            string vowels = m.Vowels;

            var head = new List<string>();
            var pools = new List<List<string>>();
            foreach (var p in pairs)
            {
                char a = p.Expected, b = p.Typed;
                head.Add(new string(a, 3)); head.Add(new string(b, 3));
                head.Add($"{a}{b}"); head.Add($"{b}{a}"); head.Add($"{a}{b}{a}"); head.Add($"{b}{a}{b}");
                // Контрастні склади: та сама голосна (або приголосна) з кожною з двох літер.
                foreach (var v in (m.IsVowel(a) && m.IsVowel(b) ? "нтлс" : vowels).Take(2))
                {
                    if (v == a || v == b) continue;
                    head.Add($"{a}{v}"); head.Add($"{b}{v}");
                }
                var withA = words.Where(w => w.IndexOf(a) >= 0 && w.IndexOf(b) < 0).OrderBy(_ => Rnd.Next()).Take(14).ToList();
                var withB = words.Where(w => w.IndexOf(b) >= 0 && w.IndexOf(a) < 0).OrderBy(_ => Rnd.Next()).Take(14).ToList();
                var both = words.Where(w => w.IndexOf(a) >= 0 && w.IndexOf(b) >= 0).OrderBy(_ => Rnd.Next()).Take(10).ToList();
                // Слова чергуються: з першою літерою, з другою, з обома.
                var pool = new List<string>();
                for (int i = 0; i < 14; i++)
                {
                    if (i < withA.Count) pool.Add(withA[i]);
                    if (i < withB.Count) pool.Add(withB[i]);
                    if (i < both.Count) pool.Add(both[i]);
                }
                if (pool.Count == 0) { pool.Add($"{a}{b}"); pool.Add($"{b}{a}"); }
                pools.Add(pool);
            }

            int turn = 0;
            var index = new int[pools.Count];
            string Next()
            {
                int k = turn++ % pools.Count;
                var pool = pools[k];
                return pool[index[k]++ % pool.Count];
            }
            return Build(head, Next, l.Length);
        }

        // ---------- свій текст ----------

        /// <summary>Привести довільний текст до того, що можна набрати в розкладці: лапки, тире, пробіли.</summary>
        public static string NormalizeCustom(string text, string layout)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (var raw in text)
            {
                char c = raw;
                switch (c)
                {
                    case '«': case '»': case '“': case '”': case '„': c = '"'; break;
                    case '’': case 'ʼ': case '‘': case '`': c = '\''; break;
                    case '—': case '–': case '‑': case '−': c = '-'; break;
                    case '…': c = '.'; break; // одна крапка: розділові знаки не йдуть підряд
                    case ' ': case '\t': case '\r': case '\n': c = ' '; break;
                }
                if (c == ' ')
                {
                    if (!space && sb.Length > 0) sb.Append(' ');
                    space = true;
                    continue;
                }
                if (!KeyboardLayout.Find(layout, c, out _, out _) || KeyboardLayout.IsSpecial(c)) continue;
                sb.Append(c);
                space = false;
            }
            return sb.ToString().Trim();
        }

        /// <summary>Наступна частина свого тексту (~200 символів по межі слова), позиція зберігається в config.json.</summary>
        private static string CustomText(Lesson l)
        {
            var cfg = AppConfig.Current;
            var text = NormalizeCustom(cfg.EffectiveCustomText(l.Layout), l.Layout);
            if (text.Length < 10) return FreeText(l);

            int pos = cfg.CustomPos;
            if (pos >= text.Length - 5) pos = 0;
            while (pos > 0 && pos < text.Length && text[pos - 1] != ' ') pos++;   // не починати з середини слова
            if (pos >= text.Length) pos = 0;

            int end = Math.Min(text.Length, pos + l.Length);
            if (end < text.Length)
            {
                int cut = text.LastIndexOf(' ', end);
                if (cut > pos + l.Length / 2) end = cut;
            }

            cfg.CustomPos = end >= text.Length ? 0 : end + 1;
            cfg.Save(); // без події Changed — це не зміна налаштувань
            return text.Substring(pos, end - pos).Trim();
        }
    }
}
