using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Keybrd.Core
{
    public sealed class SessionRecord
    {
        public DateTime Ts;
        public string Layout, Lesson;
        public double DurationSec, Cpm, Accuracy;
        public int Chars, Errors;
        public bool Passed;
    }

    public sealed class LessonProgress
    {
        public double Best, BestAccuracy;
        public bool Passed;
        public int Attempts;
    }

    public sealed class KeyStat
    {
        public int Hits, Errors, Timed;
        public long TotalMs;
        public int Total => Hits + Errors;
        public double AvgMs => Timed > 0 ? (double)TotalMs / Timed : 0;
        public double ErrorRate => Total > 0 ? (double)Errors / Total : 0;
    }

    /// <summary>Підсумок одного дня для графіків.</summary>
    public sealed class DayStat
    {
        public DateTime Day;
        public double Cpm, Accuracy, Minutes;
        public int Sessions;
    }

    /// <summary>Динаміка навчання у stats.json поруч з exe.</summary>
    public static class StatsStore
    {
        public const string FileName = "stats.json";
        public const double MinSessionSec = 10;

        public static readonly List<SessionRecord> Sessions = new List<SessionRecord>();
        public static readonly Dictionary<string, LessonProgress> Lessons = new Dictionary<string, LessonProgress>();
        /// <summary>розкладка → символ → статистика</summary>
        public static readonly Dictionary<string, Dictionary<string, KeyStat>> Keys = new Dictionary<string, Dictionary<string, KeyStat>>();
        /// <summary>розкладка → пара літер → статистика другої літери пари</summary>
        public static readonly Dictionary<string, Dictionary<string, KeyStat>> Bigrams = new Dictionary<string, Dictionary<string, KeyStat>>();

        public static bool WasReset { get; private set; }
        public static event Action Changed;

        public static string FilePath => DataStore.PathOf(FileName);

        // ---------- уроки ----------

        public static bool IsPassed(string lessonId) => Lessons.TryGetValue(lessonId, out var p) && p.Passed;
        public static LessonProgress Progress(string lessonId) => Lessons.TryGetValue(lessonId, out var p) ? p : null;

        public static bool IsUnlocked(Lesson l)
        {
            if (l == null || l.IsFree || AppConfig.Current.UnlockAll) return true;
            var prev = Course.Previous(l);
            return prev == null || IsPassed(prev.Id) || IsPassed(l.Id);
        }

        /// <summary>Перший незарахований і відкритий урок курсу.</summary>
        public static Lesson FirstOpen(string layout)
            => Course.For(layout).FirstOrDefault(l => !IsPassed(l.Id) && IsUnlocked(l)) ?? Course.For(layout).Last();

        public static bool MeetsGoal(TypingSession s, Lesson l)
            => !l.IsFree && s.Accuracy >= AppConfig.Current.UnlockAccuracy && s.Cpm >= l.MinCpm;

        // ---------- аналітика ----------

        private static Dictionary<string, KeyStat> Of(Dictionary<string, Dictionary<string, KeyStat>> d, string layout)
            => d.TryGetValue(layout, out var m) ? m : new Dictionary<string, KeyStat>();

        public static Dictionary<string, KeyStat> KeysOf(string layout) => Of(Keys, layout);

        /// <summary>Оцінка «слабкості»: помилки важать утричі більше за повільність відносно медіани.</summary>
        private static List<KeyValuePair<string, KeyStat>> Weak(Dictionary<string, KeyStat> m, int minTotal, Func<string, bool> filter)
        {
            var items = m.Where(kv => kv.Value.Total >= minTotal && filter(kv.Key)).ToList();
            if (items.Count == 0) return items;
            var timed = items.Where(kv => kv.Value.Timed > 0).Select(kv => kv.Value.AvgMs).OrderBy(x => x).ToList();
            double median = timed.Count > 0 ? timed[timed.Count / 2] : 0;
            double Score(KeyStat k) => k.ErrorRate * 3 + (median > 0 && k.Timed > 0 ? Math.Max(0, k.AvgMs / median - 1) : 0);
            // Слабкою вважаємо клавішу з помилками від ~4% або повільнішу за медіану на 15%+.
            return items.Where(kv => Score(kv.Value) >= 0.12).OrderByDescending(kv => Score(kv.Value)).ToList();
        }

        /// <summary>Найслабші символи (літери й розділові знаки, без пробілу та F-клавіш).</summary>
        public static List<string> WeakKeys(string layout, int count)
            => Weak(Of(Keys, layout), 8, k => k.Length == 1 && k != " ").Take(count).Select(kv => kv.Key).ToList();

        public static List<string> WeakBigrams(string layout, int count)
            => Weak(Of(Bigrams, layout), 4, k => true).Take(count).Select(kv => kv.Key).ToList();

        public static List<SessionRecord> SessionsOf(string layout) => Sessions.Where(s => s.Layout == layout).ToList();

        /// <summary>Денні підсумки: швидкість і точність зважені на кількість символів.</summary>
        public static List<DayStat> Daily(string layout, int days)
        {
            var from = days > 0 ? DateTime.Today.AddDays(-days + 1) : DateTime.MinValue;
            return SessionsOf(layout)
                .Where(s => s.Ts >= from)
                .GroupBy(s => s.Ts.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    double chars = g.Sum(s => Math.Max(1, s.Chars));
                    return new DayStat
                    {
                        Day = g.Key,
                        Cpm = g.Sum(s => s.Cpm * Math.Max(1, s.Chars)) / chars,
                        Accuracy = g.Sum(s => s.Accuracy * Math.Max(1, s.Chars)) / chars,
                        Minutes = g.Sum(s => s.DurationSec) / 60,
                        Sessions = g.Count(),
                    };
                })
                .ToList();
        }

        // ---------- запис ----------

        /// <summary>Записати завершену вправу. Повертає true, якщо урок зараховано.</summary>
        public static bool Record(TypingSession s, Lesson l, int masteryFrom = 0)
        {
            bool passed = MeetsGoal(s, l);

            // «М'язова пам'ять» оновлюється в усіх режимах, не лише в динамічному.
            Mastery.Update(s, masteryFrom);

            if (!l.IsFree)
            {
                if (!Lessons.TryGetValue(l.Id, out var p)) Lessons[l.Id] = p = new LessonProgress();
                p.Attempts++;
                if (s.Accuracy >= AppConfig.Current.UnlockAccuracy && s.Cpm > p.Best) p.Best = Math.Round(s.Cpm);
                p.BestAccuracy = Math.Max(p.BestAccuracy, Math.Round(s.Accuracy, 1));
                if (passed) p.Passed = true;
            }

            if (!Keys.TryGetValue(s.Layout, out var keys)) Keys[s.Layout] = keys = new Dictionary<string, KeyStat>();
            if (!Bigrams.TryGetValue(s.Layout, out var bigrams)) Bigrams[s.Layout] = bigrams = new Dictionary<string, KeyStat>();

            char prev = '\0';
            foreach (var st in s.Strokes)
            {
                Count(keys, KeyName(st.Expected), st);

                bool letter = char.IsLetter(st.Expected) || st.Expected == '\'';
                if (prev != '\0' && letter)
                    Count(bigrams, char.ToLowerInvariant(prev).ToString() + char.ToLowerInvariant(st.Expected), st);
                if (st.Ok) prev = letter ? st.Expected : '\0';
            }

            double sec = s.ElapsedMs / 1000.0;
            if (sec >= MinSessionSec)
            {
                Sessions.Add(new SessionRecord
                {
                    Ts = s.StartedAt, Layout = s.Layout, Lesson = l.Id, DurationSec = Math.Round(sec, 1),
                    Chars = s.Pos, Errors = s.Errors, Cpm = Math.Round(s.Cpm), Accuracy = Math.Round(s.Accuracy, 1), Passed = passed,
                });
            }

            Save();
            Changed?.Invoke();
            return passed;
        }

        private static void Count(Dictionary<string, KeyStat> m, string name, KeyStroke st)
        {
            if (!m.TryGetValue(name, out var k)) m[name] = k = new KeyStat();
            if (!st.Ok) { k.Errors++; return; }
            k.Hits++;
            if (st.Ms > 0) { k.Timed++; k.TotalMs += st.Ms; }
        }

        private static string KeyName(char c)
        {
            if (KeyboardLayout.IsSpecial(c)) return KeyboardLayout.SpecialName(c);
            return char.ToLowerInvariant(c).ToString();
        }

        // ---------- файл ----------

        public static void Load()
        {
            DataStore.Resolve();
            Sessions.Clear(); Lessons.Clear(); Keys.Clear(); Bigrams.Clear();
            var path = FilePath;
            if (!File.Exists(path)) return;
            try
            {
                var root = Json.Parse(File.ReadAllText(path, DataStore.Utf8)) as IDictionary<string, object>;
                if (root == null) throw new FormatException();

                if (root.TryGetValue("sessions", out var ss) && ss is List<object> list)
                {
                    foreach (var o in list)
                    {
                        if (!(o is IDictionary<string, object> d)) continue;
                        DateTime.TryParse(Json.GetString(d, "ts", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts);
                        Sessions.Add(new SessionRecord
                        {
                            Ts = ts,
                            Layout = Json.GetString(d, "layout", "uk"),
                            Lesson = Json.GetString(d, "lesson", ""),
                            DurationSec = Json.GetNumber(d, "durationSec", 0),
                            Chars = (int)Json.GetNumber(d, "chars", 0),
                            Errors = (int)Json.GetNumber(d, "errors", 0),
                            Cpm = Json.GetNumber(d, "cpm", 0),
                            Accuracy = Json.GetNumber(d, "accuracy", 0),
                            Passed = Json.GetBool(d, "passed", false),
                        });
                    }
                }

                var lessons = Json.GetObject(root, "lessons");
                if (lessons != null)
                    foreach (var kv in lessons)
                        if (kv.Value is IDictionary<string, object> d)
                            Lessons[kv.Key] = new LessonProgress
                            {
                                Best = Json.GetNumber(d, "best", 0),
                                BestAccuracy = Json.GetNumber(d, "bestAccuracy", 0),
                                Passed = Json.GetBool(d, "passed", false),
                                Attempts = (int)Json.GetNumber(d, "attempts", 0),
                            };

                ReadStats(Json.GetObject(root, "keys"), Keys);
                ReadStats(Json.GetObject(root, "bigrams"), Bigrams);
            }
            catch (Exception)
            {
                try { File.Copy(path, DataStore.PathOf("stats.bak.json"), true); } catch { }
                Sessions.Clear(); Lessons.Clear(); Keys.Clear(); Bigrams.Clear();
                WasReset = true;
                Save(); // одразу записати чистий файл, щоб попередження не повторювалось
            }
        }

        private static void ReadStats(IDictionary<string, object> src, Dictionary<string, Dictionary<string, KeyStat>> dst)
        {
            if (src == null) return;
            foreach (var lay in src)
            {
                if (!(lay.Value is IDictionary<string, object> items)) continue;
                var m = dst[lay.Key] = new Dictionary<string, KeyStat>();
                foreach (var kv in items)
                    if (kv.Value is IDictionary<string, object> d)
                    {
                        var k = new KeyStat
                        {
                            Hits = (int)Json.GetNumber(d, "hits", 0),
                            Errors = (int)Json.GetNumber(d, "errors", 0),
                            TotalMs = (long)Json.GetNumber(d, "totalMs", 0),
                        };
                        k.Timed = (int)Json.GetNumber(d, "timed", k.Hits);
                        m[kv.Key] = k;
                    }
            }
        }

        private static Dictionary<string, object> WriteStats(Dictionary<string, Dictionary<string, KeyStat>> src)
        {
            var result = new Dictionary<string, object>();
            foreach (var lay in src)
            {
                var m = new Dictionary<string, object>();
                foreach (var kv in lay.Value.OrderBy(k => k.Key, StringComparer.Ordinal))
                    m[kv.Key] = new Dictionary<string, object>
                    {
                        ["hits"] = kv.Value.Hits,
                        ["errors"] = kv.Value.Errors,
                        ["timed"] = kv.Value.Timed,
                        ["totalMs"] = kv.Value.TotalMs,
                    };
                result[lay.Key] = m;
            }
            return result;
        }

        /// <summary>Почати статистику заново; стара копія — stats.bak.json.</summary>
        public static void Reset()
        {
            try { if (File.Exists(FilePath)) File.Copy(FilePath, DataStore.PathOf("stats.bak.json"), true); } catch { }
            Sessions.Clear(); Lessons.Clear(); Keys.Clear(); Bigrams.Clear();
            Save();
            Changed?.Invoke();
        }

        public static void Save()
        {
            var sessions = Sessions.Select(s => (object)new Dictionary<string, object>
            {
                ["ts"] = s.Ts.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                ["layout"] = s.Layout,
                ["lesson"] = s.Lesson,
                ["durationSec"] = s.DurationSec,
                ["chars"] = s.Chars,
                ["errors"] = s.Errors,
                ["cpm"] = s.Cpm,
                ["accuracy"] = s.Accuracy,
                ["passed"] = s.Passed,
            }).ToList();

            var lessons = new Dictionary<string, object>();
            foreach (var kv in Lessons.OrderBy(k => k.Key, StringComparer.Ordinal))
                lessons[kv.Key] = new Dictionary<string, object>
                {
                    ["best"] = kv.Value.Best,
                    ["bestAccuracy"] = kv.Value.BestAccuracy,
                    ["passed"] = kv.Value.Passed,
                    ["attempts"] = kv.Value.Attempts,
                };

            var root = new Dictionary<string, object>
            {
                ["version"] = 1,
                ["sessions"] = sessions,
                ["lessons"] = lessons,
                ["keys"] = WriteStats(Keys),
                ["bigrams"] = WriteStats(Bigrams),
            };
            try { DataStore.WriteAtomic(FilePath, Json.Write(root)); } catch { }
        }
    }
}
