using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Keybrd.Core
{
    public enum TypeResult { Ignored, Correct, Wrong, Finished }

    /// <summary>Одне натискання символьної клавіші: що очікувалось, чи вірно, скільки мс від попереднього.</summary>
    public struct KeyStroke
    {
        public char Expected;
        public bool Ok;
        public int Ms;
        /// <summary>Що натиснули насправді і яка літера йшла в тексті наступною (для аналізу плутанини).</summary>
        public char Typed, Next;
    }

    /// <summary>
    /// Одна вправа. Режим «стоп на помилці»: курсор не рухається, доки не натиснуто правильну клавішу.
    /// Паузи довші за MaxGapMs не рахуються в час, щоб відволікання не псувало швидкість.
    /// </summary>
    public sealed class TypingSession
    {
        public const int MaxGapMs = 3000;

        public string Text { get; private set; }
        public string Layout { get; }
        public int Pos { get; private set; }
        public int Keystrokes { get; private set; }
        public int Errors { get; private set; }
        public bool Started { get; private set; }
        public bool Finished => stopped || Pos >= Text.Length;
        private bool stopped;

        /// <summary>Дописати текст (урок без зупинки).</summary>
        public void Append(string more)
        {
            if (string.IsNullOrEmpty(more) || stopped) return;
            Text += more;
            Array.Resize(ref HadError, Text.Length);
        }

        /// <summary>Завершити вправу достроково (урок без зупинки закінчує сама людина).</summary>
        public void Stop()
        {
            stopped = true;
            sw.Stop();
        }
        public DateTime StartedAt { get; private set; }

        public bool[] HadError;
        public readonly List<KeyStroke> Strokes = new List<KeyStroke>();

        private readonly Stopwatch sw = new Stopwatch();
        private long lastMs;
        private double activeMs;

        public TypingSession(string text, string layout)
        {
            Text = text ?? "";
            Layout = layout;
            HadError = new bool[Text.Length];
        }

        public char? Current => Finished ? (char?)null : Text[Pos];

        public TypeResult Type(string typed)
        {
            if (Finished || string.IsNullOrEmpty(typed)) return TypeResult.Ignored;

            long now;
            int dt;
            if (!Started)
            {
                Started = true;
                StartedAt = DateTime.Now;
                sw.Start();
                now = 0;
                dt = 0;
            }
            else
            {
                now = sw.ElapsedMilliseconds;
                dt = (int)Math.Min(now - lastMs, MaxGapMs);
                activeMs += dt;
            }
            lastMs = now;
            Keystrokes++;

            char expected = Text[Pos];
            bool ok = Same(typed[0], expected);
            Strokes.Add(new KeyStroke
            {
                Expected = expected, Ok = ok, Ms = dt, Typed = typed[0],
                Next = Pos + 1 < Text.Length ? Text[Pos + 1] : (char)0,
            });

            if (!ok)
            {
                Errors++;
                HadError[Pos] = true;
                return TypeResult.Wrong;
            }

            Pos++;
            if (Finished) sw.Stop();
            return Finished ? TypeResult.Finished : TypeResult.Correct;
        }

        private static bool Same(char a, char b)
        {
            if (a == b) return true;
            return IsApostrophe(a) && IsApostrophe(b);
        }

        private static bool IsApostrophe(char c) => c == '\'' || c == 'ʼ' || c == '’';

        // ---------- метрики ----------

        /// <summary>Час набору в мс (живий, поки вправа триває).</summary>
        public double ElapsedMs
        {
            get
            {
                if (!Started) return 0;
                if (Finished) return activeMs;
                return activeMs + Math.Min(sw.ElapsedMilliseconds - lastMs, MaxGapMs);
            }
        }

        /// <summary>Знаків за хвилину (правильно набраних).</summary>
        public double Cpm
        {
            get
            {
                double min = ElapsedMs / 60000.0;
                return min < 1.0 / 60 ? 0 : Pos / min;
            }
        }

        public double Accuracy => Keystrokes == 0 ? 100 : 100.0 * (Keystrokes - Errors) / Keystrokes;

        /// <summary>Символи з найбільшою кількістю помилок у цій вправі.</summary>
        public IEnumerable<char> WorstChars(int count)
            => Strokes.Where(s => !s.Ok)
                      .GroupBy(s => s.Expected)
                      .OrderByDescending(g => g.Count())
                      .Take(count)
                      .Select(g => g.Key);

        public static string FormatTime(double ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        public static string FormatSpeed(double cpm, string unit)
            => unit == "wpm" ? Math.Round(cpm / 5).ToString() : Math.Round(cpm).ToString();

        public static string SpeedLabel(string unit) => unit == "wpm" ? "сл/хв" : "зн/хв";
    }
}
