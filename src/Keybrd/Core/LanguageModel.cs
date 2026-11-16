using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Статистика мови зі словника: які пари й трійки літер трапляються найчастіше і як літери
    /// йдуть одна за одною. З неї беруться часті склади для вправ і «вимовні» псевдослова
    /// (як у keybr): вони схожі на справжні слова, тож пальці вчать ті самі рухи, що й у живому тексті.
    /// </summary>
    public sealed class LanguageModel
    {
        private const int FrequentWeight = 8; // часті слова важать більше, ніж решта словника

        private static readonly Dictionary<string, LanguageModel> Cache = new Dictionary<string, LanguageModel>();
        private static readonly Random Rnd = new Random();

        public static LanguageModel For(string layout)
        {
            if (!Cache.TryGetValue(layout, out var m)) Cache[layout] = m = new LanguageModel(layout);
            return m;
        }

        public readonly string Layout;
        public readonly string Vowels;
        /// <summary>Усі слова словника (як у файлі, з великими літерами в назвах).</summary>
        public readonly string[] Words;
        /// <summary>Найуживаніші слова мови.</summary>
        public readonly string[] Frequent;

        private readonly HashSet<string> frequentSet;
        private readonly Dictionary<string, double> bigrams = new Dictionary<string, double>();
        private readonly Dictionary<string, double> trigrams = new Dictionary<string, double>();
        // Ланцюг Маркова 2-го порядку: дві попередні літери → можливі наступні ('^' — початок, '$' — кінець слова).
        private readonly Dictionary<string, List<KeyValuePair<char, double>>> chain = new Dictionary<string, List<KeyValuePair<char, double>>>();

        private LanguageModel(string layout)
        {
            Layout = layout;
            Vowels = layout == "en" ? "aeiouy" : "аеєиіїоуюя";
            Frequent = Load($"frequent_{layout}.txt");
            frequentSet = new HashSet<string>(Frequent);
            Words = Frequent.Concat(Load($"words_{layout}.txt")).Distinct().ToArray();

            var next = new Dictionary<string, Dictionary<char, double>>();
            foreach (var raw in Words)
            {
                var w = raw.ToLowerInvariant();
                if (w.Length < 2 || !w.All(c => char.IsLetter(c) || c == '\'')) continue;
                double weight = frequentSet.Contains(raw) ? FrequentWeight : 1;

                for (int i = 0; i + 1 < w.Length; i++) Add(bigrams, w.Substring(i, 2), weight);
                for (int i = 0; i + 2 < w.Length; i++) Add(trigrams, w.Substring(i, 3), weight);

                string padded = "^^" + w + "$";
                for (int i = 2; i < padded.Length; i++)
                {
                    string key = padded.Substring(i - 2, 2);
                    if (!next.TryGetValue(key, out var d)) next[key] = d = new Dictionary<char, double>();
                    d[padded[i]] = (d.TryGetValue(padded[i], out var v) ? v : 0) + weight;
                }
            }
            foreach (var kv in next) chain[kv.Key] = kv.Value.ToList();
        }

        private static void Add(Dictionary<string, double> d, string key, double weight)
            => d[key] = (d.TryGetValue(key, out var v) ? v : 0) + weight;

        private static string[] Load(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Keybrd.Data." + name))
            {
                if (s == null) return new string[0];
                using (var r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd().Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
            }
        }

        private HashSet<string> lowerWords;

        /// <summary>Чи є таке слово у словнику (без урахування регістру).</summary>
        public bool HasWord(string lower)
        {
            if (lowerWords == null) lowerWords = new HashSet<string>(Words.Select(w => w.ToLowerInvariant()));
            return lowerWords.Contains(lower);
        }

        private double maxBigram;

        /// <summary>Рідкісність пари літер: 0 — найчастіша в мові, ~3 — дуже рідкісна або невідома.</summary>
        public double BigramRarity(string pair)
        {
            if (maxBigram <= 0) maxBigram = bigrams.Count > 0 ? bigrams.Values.Max() : 1;
            double w = bigrams.TryGetValue(pair, out var v) ? v : 0.5;
            return Math.Min(3, Math.Log10(maxBigram / w));
        }

        public bool IsFrequent(string word) => frequentSet.Contains(word);
        public bool IsVowel(char c) => Vowels.IndexOf(c) >= 0;

        /// <summary>
        /// Найчастіші сполучення з n літер, які складаються лише з дозволених літер і містять
        /// хоча б одну з «нових». Якщо серед дозволених є голосні, сполучення мусить мати голосну —
        /// тоді воно читається як склад.
        /// </summary>
        public List<string> TopNgrams(int n, string allowed, string mustContain, int count)
        {
            bool needVowel = allowed.Any(IsVowel);
            return (n == 2 ? bigrams : trigrams)
                .Where(kv => kv.Key.All(c => allowed.IndexOf(c) >= 0)
                             && (mustContain.Length == 0 || kv.Key.Any(c => mustContain.IndexOf(c) >= 0))
                             && (!needVowel || kv.Key.Any(IsVowel))
                             // трійка не закінчується двома приголосними: «про», «ста» — так, «ожл» — ні
                             && (!needVowel || n < 3 || IsVowel(kv.Key[1]) || IsVowel(kv.Key[2]))
                             && kv.Key.Distinct().Count() > 1)
                .OrderByDescending(kv => kv.Value)
                .Take(count)
                .Select(kv => kv.Key)
                .ToList();
        }

        /// <summary>
        /// Псевдослово з дозволених літер, побудоване за статистикою мови. Літери з mustContain
        /// трапляються частіше. null — якщо з такого набору літер нічого вимовного не виходить.
        /// </summary>
        public string Pseudo(string allowed, string mustContain)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var sb = new StringBuilder();
                char c1 = '^', c2 = '^';
                bool done = false;
                while (sb.Length < 8)
                {
                    if (!chain.TryGetValue(new string(new[] { c1, c2 }), out var options)) break;
                    double total = 0;
                    var usable = new List<KeyValuePair<char, double>>();
                    foreach (var o in options)
                    {
                        double w;
                        if (o.Key == '$') { if (sb.Length < 3) continue; w = o.Value * (sb.Length >= 5 ? 3 : 1); }
                        else if (allowed.IndexOf(o.Key) < 0) continue;
                        else w = o.Value * (mustContain.IndexOf(o.Key) >= 0 ? 4 : 1);
                        usable.Add(new KeyValuePair<char, double>(o.Key, w));
                        total += w;
                    }
                    if (usable.Count == 0) break;

                    double r = Rnd.NextDouble() * total;
                    char pick = usable[usable.Count - 1].Key;
                    foreach (var u in usable) { r -= u.Value; if (r <= 0) { pick = u.Key; break; } }
                    if (pick == '$') { done = true; break; }
                    sb.Append(pick);
                    c1 = c2; c2 = pick;
                }

                string w2 = sb.ToString();
                if (!done && w2.Length < 4) continue;
                if (w2.Length < 3 || w2.Length > 8) continue;
                if (!w2.Any(IsVowel)) continue;
                if (mustContain.Length > 0 && !w2.Any(c => mustContain.IndexOf(c) >= 0)) continue;
                if (HasTriple(w2) || ConsonantRun(w2) > 3) continue;
                return w2;
            }
            return null;
        }

        private static bool HasTriple(string w)
        {
            for (int i = 2; i < w.Length; i++)
                if (w[i] == w[i - 1] && w[i] == w[i - 2]) return true;
            return false;
        }

        private int ConsonantRun(string w)
        {
            int best = 0, run = 0;
            foreach (var c in w)
            {
                run = IsVowel(c) ? 0 : run + 1;
                best = Math.Max(best, run);
            }
            return best;
        }
    }
}
