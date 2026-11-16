using System;
using System.Collections.Generic;

namespace Keybrd.Core
{
    public enum Finger { None = -1, LPinky, LRing, LMiddle, LIndex, RIndex, RMiddle, RRing, RPinky, Thumb }

    /// <summary>Одна фізична клавіша. Id — скан-код (+0x100 для розширених), тож не залежить від розкладки Windows.</summary>
    public sealed class KeyDef
    {
        public int Id;
        public double W = 1, H = 1, YOff;
        public bool NoAdvance;      // наступна клавіша стає на те саме місце (стрілки ↑/↓ у ноутбуці)
        public Finger Finger = Finger.None;
        public string Label;        // текст службової клавіші
        public string Glyph;        // іконка замість тексту
        public string FnGlyph;      // іконка медіашару Fn (лише F1–F12)
        public bool HomeBump;       // мітка на А/О (F/J)
        public double X, Y;         // позиція в одиницях, обчислюється
        public bool IsChar => Label == null && Glyph == null;
    }

    public static class KeyboardLayout
    {
        public const int FnKeyId = -1;
        public const int LShift = 0x2A, RShift = 0x36, Space = 0x39, Enter = 0x1C, Backspace = 0x0E, Tab = 0x0F, Caps = 0x3A;
        public const int Ext = 0x100;

        private static int E(int scan) => scan | Ext;

        // ---------- символи на клавішах ----------

        private static readonly int[] DigitScans = { 0x29, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D };
        private static readonly int[] QScans = { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x2B };
        private static readonly int[] AScans = { 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28 };
        private static readonly int[] ZScans = { 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35 };

        private static readonly Finger[] DigitFingers = { Finger.LPinky, Finger.LPinky, Finger.LRing, Finger.LMiddle, Finger.LIndex, Finger.LIndex, Finger.RIndex, Finger.RIndex, Finger.RMiddle, Finger.RRing, Finger.RPinky, Finger.RPinky, Finger.RPinky };
        private static readonly Finger[] QFingers = { Finger.LPinky, Finger.LRing, Finger.LMiddle, Finger.LIndex, Finger.LIndex, Finger.RIndex, Finger.RIndex, Finger.RMiddle, Finger.RRing, Finger.RPinky, Finger.RPinky, Finger.RPinky, Finger.RPinky };
        private static readonly Finger[] AFingers = { Finger.LPinky, Finger.LRing, Finger.LMiddle, Finger.LIndex, Finger.LIndex, Finger.RIndex, Finger.RIndex, Finger.RMiddle, Finger.RRing, Finger.RPinky, Finger.RPinky };
        private static readonly Finger[] ZFingers = { Finger.LPinky, Finger.LRing, Finger.LMiddle, Finger.LIndex, Finger.LIndex, Finger.RIndex, Finger.RIndex, Finger.RMiddle, Finger.RRing, Finger.RPinky };

        // Українська (розширена) — стандартна розкладка Windows 10/11: ' на клавіші `, ґ на клавіші \.
        private static readonly Dictionary<int, string[]> Uk = Build(
            ("'1234567890-=", "₴!\"№;%:?*()_+"),
            ("йцукенгшщзхїґ", "ЙЦУКЕНГШЩЗХЇҐ"),
            ("фівапролджє", "ФІВАПРОЛДЖЄ"),
            ("ячсмитьбю.", "ЯЧСМИТЬБЮ,"));

        // Англійська (США)
        private static readonly Dictionary<int, string[]> En = Build(
            ("`1234567890-=", "~!@#$%^&*()_+"),
            ("qwertyuiop[]\\", "QWERTYUIOP{}|"),
            ("asdfghjkl;'", "ASDFGHJKL:\""),
            ("zxcvbnm,./", "ZXCVBNM<>?"));

        private static Dictionary<int, string[]> Build((string lo, string up) d, (string lo, string up) q, (string lo, string up) a, (string lo, string up) z)
        {
            var map = new Dictionary<int, string[]>();
            void Add(int[] scans, (string lo, string up) s)
            {
                for (int i = 0; i < scans.Length; i++)
                    map[scans[i]] = new[] { s.lo[i].ToString(), s.up[i].ToString() };
            }
            Add(DigitScans, d); Add(QScans, q); Add(AScans, a); Add(ZScans, z);
            map[Space] = new[] { " ", " " };
            return map;
        }

        /// <summary>[нижній, верхній] символ клавіші в розкладці або null.</summary>
        public static string[] Chars(string layout, int id)
            => (layout == "en" ? En : Uk).TryGetValue(id, out var c) ? c : null;

        /// <summary>Ряд клавіші: 0 — цифри, 1 — верхній, 2 — основний, 3 — нижній, -1 — інше.</summary>
        public static int RowOf(int id)
        {
            if (Array.IndexOf(AScans, id) >= 0) return 2;
            if (Array.IndexOf(QScans, id) >= 0) return 1;
            if (Array.IndexOf(ZScans, id) >= 0) return 3;
            if (Array.IndexOf(DigitScans, id) >= 0) return 0;
            return -1;
        }

        /// <summary>Місце клавіші на клавіатурі: ряд і горизонтальна позиція з урахуванням зсуву рядів.</summary>
        public static bool Position(int id, out int row, out double x)
        {
            row = RowOf(id); x = 0;
            if (id == Space) { row = 4; x = 6; return true; }
            if (row < 0) return false;
            var scans = row == 0 ? DigitScans : row == 1 ? QScans : row == 2 ? AScans : ZScans;
            double[] offset = { 0, 1.5, 1.75, 2.25 };
            x = Array.IndexOf(scans, id) + offset[row];
            return true;
        }

        /// <summary>Чи стоять дві клавіші поруч (у ряду або по діагоналі).</summary>
        public static bool Adjacent(int a, int b)
        {
            if (a == b || !Position(a, out int ra, out double xa) || !Position(b, out int rb, out double xb)) return false;
            if (ra == 4 || rb == 4) return false;
            return Math.Abs(ra - rb) <= 1 && Math.Abs(xa - xb) <= 1.1;
        }

        /// <summary>Дзеркальні клавіші: той самий ряд і той самий палець іншої руки.</summary>
        public static bool Mirror(int a, int b)
        {
            if (a == b || RowOf(a) < 0 || RowOf(a) != RowOf(b)) return false;
            var fa = FingerOf(a); var fb = FingerOf(b);
            if (fa < Finger.LPinky || fa > Finger.RPinky || fb < Finger.LPinky || fb > Finger.RPinky) return false;
            return (int)fa + (int)fb == 7;
        }

        // ---------- функціональні клавіші як «символи» вправи ----------

        /// <summary>Esc, F1–F12, Del позначаються у тексті вправи символами з приватної області Unicode.</summary>
        public const char SpecialBase = '';
        private static readonly int[] SpecialIds = { 0x01, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58, 0x53 | Ext };
        private static readonly string[] SpecialNames = { "Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Del" };

        public static bool IsSpecial(char c) => c >= SpecialBase && c < SpecialBase + SpecialIds.Length;
        public static string SpecialName(char c) => IsSpecial(c) ? SpecialNames[c - SpecialBase] : c.ToString();

        /// <summary>Як показати символ людині: пробіл — «пробіл», F5 — «F5».</summary>
        public static string DisplayName(char c) => c == ' ' ? "пробіл" : IsSpecial(c) ? SpecialName(c) : $"«{c}»";

        /// <summary>
        /// Який символ дає фізична клавіша в розкладці тренажера з урахуванням Shift і Caps Lock.
        /// null — клавіша не символьна (Tab, Backspace тощо).
        /// </summary>
        public static string CharFor(string layout, int id, bool shift, bool caps)
        {
            int si = Array.IndexOf(SpecialIds, id);
            if (si >= 0) return ((char)(SpecialBase + si)).ToString();
            var c = Chars(layout, id);
            if (c == null) return null;
            bool upper = shift;
            if (caps && char.IsLetter(c[0][0])) upper = !upper;
            return upper ? c[1] : c[0];
        }

        public static string Other(string layout) => layout == "en" ? "uk" : "en";
        public static string ShortName(string layout) => layout == "en" ? "LAT" : "УКР";

        /// <summary>Яку клавішу натиснути для символу і чи потрібен Shift.</summary>
        public static bool Find(string layout, char ch, out int id, out bool shift)
        {
            if (IsSpecial(ch)) { id = SpecialIds[ch - SpecialBase]; shift = false; return true; }
            var s = ch.ToString();
            foreach (var kv in layout == "en" ? En : Uk)
            {
                if (kv.Value[0] == s) { id = kv.Key; shift = false; return true; }
                if (kv.Value[1] == s && kv.Key != Space) { id = kv.Key; shift = true; return true; }
            }
            // Типографський апостроф приймаємо як звичайний.
            if (ch == 'ʼ' || ch == '’') return Find(layout, '\'', out id, out shift);
            id = 0; shift = false;
            return false;
        }

        /// <summary>Shift на протилежній руці від клавіші — так вчить сліпий друк.</summary>
        public static int ShiftFor(int id)
        {
            var f = FingerOf(id);
            return f >= Finger.LPinky && f <= Finger.LIndex ? RShift : LShift;
        }

        // ---------- пальці ----------

        private static readonly Dictionary<int, Finger> Fingers = BuildFingers();

        private static Dictionary<int, Finger> BuildFingers()
        {
            var m = new Dictionary<int, Finger>();
            void Add(int[] s, Finger[] f) { for (int i = 0; i < s.Length; i++) m[s[i]] = f[i]; }
            Add(DigitScans, DigitFingers); Add(QScans, QFingers); Add(AScans, AFingers); Add(ZScans, ZFingers);
            foreach (var k in new[] { Tab, Caps, LShift, 0x1D, 0x01, 0x3B }) m[k] = Finger.LPinky;
            foreach (var k in new[] { Backspace, Enter, RShift, E(0x1D), 0x44, 0x57, 0x58, E(0x53) }) m[k] = Finger.RPinky;
            foreach (var k in new[] { Space, 0x38, E(0x38) }) m[k] = Finger.Thumb;
            m[0x3C] = Finger.LRing; m[0x3D] = Finger.LMiddle; m[0x3E] = Finger.LIndex; m[0x3F] = Finger.LIndex;
            m[0x40] = Finger.RIndex; m[0x41] = Finger.RIndex; m[0x42] = Finger.RMiddle; m[0x43] = Finger.RRing;
            return m;
        }

        public static Finger FingerOf(int id) => Fingers.TryGetValue(id, out var f) ? f : Finger.None;

        /// <summary>Індекс кольору: однакові пальці обох рук мають один колір, вказівні — різні.</summary>
        public static int ColorIndex(Finger f)
        {
            switch (f)
            {
                case Finger.LPinky: case Finger.RPinky: return 0;
                case Finger.LRing: case Finger.RRing: return 1;
                case Finger.LMiddle: case Finger.RMiddle: return 2;
                case Finger.LIndex: return 3;
                case Finger.RIndex: return 4;
                case Finger.Thumb: return 5;
                default: return -1;
            }
        }

        public static string FingerName(Finger f)
        {
            switch (f)
            {
                case Finger.LPinky: return "ліва рука, мізинець";
                case Finger.LRing: return "ліва рука, безіменний палець";
                case Finger.LMiddle: return "ліва рука, середній палець";
                case Finger.LIndex: return "ліва рука, вказівний палець";
                case Finger.RIndex: return "права рука, вказівний палець";
                case Finger.RMiddle: return "права рука, середній палець";
                case Finger.RRing: return "права рука, безіменний палець";
                case Finger.RPinky: return "права рука, мізинець";
                case Finger.Thumb: return "великий палець";
                default: return "";
            }
        }

        // ---------- геометрія ----------

        private static KeyDef K(int id, double w = 1, string label = null, string glyph = null)
            => new KeyDef { Id = id, W = w, Label = label, Glyph = glyph, Finger = FingerOf(id) };

        public static List<KeyDef> FnRow()
        {
            string[] media = { "", "", "", "", "", "", "", "", "", "", "", "" };
            var row = new List<KeyDef> { K(0x01, label: "Esc") };
            int[] f = { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58 };
            for (int i = 0; i < 12; i++)
            {
                var k = K(f[i], label: "F" + (i + 1));
                k.FnGlyph = media[i];
                row.Add(k);
            }
            row.Add(K(E(0x53), label: "Del"));
            foreach (var k in row) { k.W = 15.0 / row.Count; k.H = 0.6; }
            return row;
        }

        public static List<List<KeyDef>> MainRows(bool laptop)
        {
            var rows = new List<List<KeyDef>>();

            var r1 = new List<KeyDef>();
            foreach (var s in DigitScans) r1.Add(K(s));
            r1.Add(K(Backspace, 2, glyph: ""));
            rows.Add(r1);

            var r2 = new List<KeyDef> { K(Tab, 1.5, "Tab") };
            for (int i = 0; i < 12; i++) r2.Add(K(QScans[i]));
            r2.Add(K(0x2B, 1.5));
            rows.Add(r2);

            var r3 = new List<KeyDef> { K(Caps, 1.75, "Caps") };
            foreach (var s in AScans) r3.Add(K(s));
            r3.Add(K(Enter, 2.25, "Enter"));
            r3[4].HomeBump = true;  // А / F
            r3[7].HomeBump = true;  // О / J
            rows.Add(r3);

            var r4 = new List<KeyDef> { K(LShift, 2.25, "Shift") };
            foreach (var s in ZScans) r4.Add(K(s));
            r4.Add(K(RShift, 2.75, "Shift"));
            rows.Add(r4);

            List<KeyDef> r5;
            if (!laptop)
            {
                r5 = new List<KeyDef>
                {
                    K(0x1D, 1.25, "Ctrl"), K(E(0x5B), 1.25, "Win"), K(0x38, 1.25, "Alt"),
                    K(Space, 6.25), K(E(0x38), 1.25, "Alt"), K(FnKeyId, 1.25, "Fn"),
                    K(E(0x5D), 1.25, glyph: ""), K(E(0x1D), 1.25, "Ctrl"),
                };
            }
            else
            {
                var up = K(E(0x48), glyph: ""); up.H = 0.5; up.NoAdvance = true;
                var down = K(E(0x50), glyph: ""); down.H = 0.5; down.YOff = 0.5;
                r5 = new List<KeyDef>
                {
                    K(0x1D, 1, "Ctrl"), K(FnKeyId, 1, "Fn"), K(E(0x5B), 1, "Win"), K(0x38, 1, "Alt"),
                    K(Space, 6), K(E(0x38), 1, "Alt"), K(E(0x1D), 1, "Ctrl"),
                    K(E(0x4B), glyph: ""), up, down, K(E(0x4D), glyph: ""),
                };
            }
            rows.Add(r5);
            return rows;
        }

        /// <summary>Клавіша Enter на цифровому блоці — це той самий Enter для тренажера.</summary>
        public static int Normalize(int id) => id == E(0x1C) ? Enter : id;
    }
}
