using System;
using System.Collections.Generic;
using System.Linq;

namespace Keybrd.Core
{
    /// <summary>Одна стійка помилка: замість Expected натискають Typed.</summary>
    public sealed class ConfusionPair
    {
        public char Expected, Typed;
        public int Count, Swaps;
        public string Kind;

        public string Text => $"{Confusions.Name(Expected)} → {Confusions.Name(Typed)}";
    }

    /// <summary>
    /// Аналіз плутанини: не лише «де помилка», а «що натиснули замість потрібного».
    /// Зберігається у stats.json: розкладка → пара «треба+натиснули» → [випадків, з них перестановок].
    /// Та сама літера в іншому регістрі записується з позначкою ShiftMark попереду.
    /// </summary>
    public static class Confusions
    {
        /// <summary>Від скількох випадків пара вважається стійкою.</summary>
        public const int Stable = 3;
        private const char ShiftMark = '^';

        public static readonly Dictionary<string, Dictionary<string, int[]>> Data = new Dictionary<string, Dictionary<string, int[]>>();

        public static Dictionary<string, int[]> Of(string layout)
        {
            if (!Data.TryGetValue(layout, out var m)) Data[layout] = m = new Dictionary<string, int[]>();
            return m;
        }

        public static string Name(char c) => c == ' ' ? "пробіл" : KeyboardLayout.IsSpecial(c) ? KeyboardLayout.SpecialName(c) : c.ToString().ToUpper();

        /// <summary>Записати помилки однієї вправи. Пара, якої у вправі не було при 10+ вірних натисканнях літери, слабшає.</summary>
        public static void Record(TypingSession s)
        {
            var m = Of(s.Layout);
            var seen = new HashSet<string>();
            var hits = new Dictionary<char, int>();
            foreach (var st in s.Strokes)
            {
                char e = char.ToLowerInvariant(st.Expected), t = char.ToLowerInvariant(st.Typed);
                if (st.Ok) { hits[e] = hits.TryGetValue(e, out var n) ? n + 1 : 1; continue; }
                if (st.Typed == (char)0 || KeyboardLayout.IsSpecial(st.Expected) || KeyboardLayout.IsSpecial(st.Typed)) continue;
                string key = e == t ? ShiftMark.ToString() + e : e.ToString() + t;
                if (!m.TryGetValue(key, out var v)) m[key] = v = new int[2];
                v[0]++;
                if (e != t && st.Next != (char)0 && char.ToLowerInvariant(st.Next) == t) v[1]++;
                seen.Add(key);
            }
            foreach (var key in m.Keys.ToList())
            {
                if (seen.Contains(key)) continue;
                char e = key[0] == ShiftMark ? key[1] : key[0];
                if (!hits.TryGetValue(e, out var n) || n < 10) continue;
                var v = m[key];
                if (--v[0] <= 0) m.Remove(key);
                else v[1] = Math.Min(v[1], v[0]);
            }
        }

        public static string KindOf(string layout, char expected, char typed, int count, int swaps)
        {
            if (expected == typed) return "регістр (Shift)";
            if (swaps * 2 >= count) return "переставлені літери";
            if (typed == ' ' || expected == ' ') return "пробіл не на місці";
            if (KeyboardLayout.Find(layout, expected, out int a, out _) && KeyboardLayout.Find(layout, typed, out int b, out _))
            {
                if (a == b) return "регістр (Shift)";
                if (KeyboardLayout.Adjacent(a, b))
                    return KeyboardLayout.FingerOf(a) == KeyboardLayout.FingerOf(b) ? "сусідня клавіша, той самий палець" : "сусідня клавіша, інший палець";
                if (KeyboardLayout.Mirror(a, b)) return "дзеркальна клавіша іншої руки";
                if (KeyboardLayout.FingerOf(a) == KeyboardLayout.FingerOf(b)) return "той самий палець, інший ряд";
            }
            return "інше";
        }

        /// <summary>Стійкі пари, найчастіші спочатку.</summary>
        public static List<ConfusionPair> Top(string layout, int count)
            => Of(layout).Where(kv => kv.Value[0] >= Stable)
                .OrderByDescending(kv => kv.Value[0]).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(count)
                .Select(kv =>
                {
                    bool shift = kv.Key[0] == ShiftMark;
                    char e = shift ? kv.Key[1] : kv.Key[0], t = kv.Key[1];
                    return new ConfusionPair { Expected = e, Typed = t, Count = kv.Value[0], Swaps = kv.Value[1], Kind = KindOf(layout, e, t, kv.Value[0], kv.Value[1]) };
                }).ToList();

        /// <summary>Пари літер для вправи «Плутанина»: лише різні літери.</summary>
        public static List<ConfusionPair> LetterPairs(string layout, int count)
            => Top(layout, 40).Where(p => p.Expected != p.Typed && char.IsLetter(p.Expected) && char.IsLetter(p.Typed)).Take(count).ToList();

        // ---------- файл (частина stats.json) ----------

        public static void Read(IDictionary<string, object> src)
        {
            Data.Clear();
            if (src == null) return;
            foreach (var lay in src)
            {
                if (!(lay.Value is IDictionary<string, object> items)) continue;
                var m = Of(lay.Key);
                foreach (var kv in items)
                    if (kv.Key.Length == 2 && kv.Value is List<object> v && v.Count >= 2 && v[0] is double a && v[1] is double b && a > 0)
                        m[kv.Key] = new[] { (int)a, (int)b };
            }
        }

        public static Dictionary<string, object> Write()
        {
            var result = new Dictionary<string, object>();
            foreach (var lay in Data)
            {
                var m = new Dictionary<string, object>();
                foreach (var kv in lay.Value.OrderBy(k => k.Key, StringComparer.Ordinal))
                    m[kv.Key] = new List<object> { kv.Value[0], kv.Value[1] };
                result[lay.Key] = m;
            }
            return result;
        }
    }
}
