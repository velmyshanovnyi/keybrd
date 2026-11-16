using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Мінімальний JSON без залежностей. Об'єкти — Dictionary&lt;string, object&gt;,
    /// масиви — List&lt;object&gt;, числа — double. Читає коментарі // і /* */.
    /// Пише з відступами й без екранування кирилиці, щоб файл було зручно читати.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("зайві символи після кінця");
            return v;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        // ---------- читання ----------

        private sealed class Parser
        {
            private readonly string s;
            private int i;
            public Parser(string s) { this.s = s ?? ""; }
            public bool End => i >= s.Length;

            public FormatException Error(string msg) => new FormatException($"JSON: {msg} (позиція {i})");

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿') { i++; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = end < 0 ? s.Length : end + 2;
                        continue;
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                if (End) throw Error("неочікуваний кінець");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"неочікуваний символ '{c}'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"очікувалось {word}");
                i += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; SkipWs();
                if (!End && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error("очікувався ключ");
                    var key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("очікувалось ':'");
                    i++; SkipWs();
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("незакритий об'єкт");
                    if (s[i] == ',') { i++; SkipWs(); if (!End && s[i] == '}') { i++; return d; } continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("очікувалось ',' або '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                i++; SkipWs();
                if (!End && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("незакритий масив");
                    if (s[i] == ',') { i++; SkipWs(); if (!End && s[i] == ']') { i++; return list; } continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Error("очікувалось ',' або ']'");
                }
            }

            private string ReadString()
            {
                i++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("незакритий рядок");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("незакритий рядок");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("погане \\u");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error($"погана послідовність \\{e}");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw Error("погане число");
                return v;
            }
        }

        // ---------- запис ----------

        private static void Indent(StringBuilder sb, int level) => sb.Append(' ', level * 2);

        private static void WriteValue(StringBuilder sb, object v, int level)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string str: WriteString(sb, str); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: WriteValue(sb, (double)f, level); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> dict: WriteObject(sb, dict, level); return;
                case IEnumerable seq: WriteArray(sb, seq, level); return;
                default: WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture)); return;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> d, int level)
        {
            if (d.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\r\n");
            int k = 0;
            foreach (var kv in d)
            {
                Indent(sb, level + 1);
                WriteString(sb, kv.Key);
                sb.Append(": ");
                WriteValue(sb, kv.Value, level + 1);
                if (++k < d.Count) sb.Append(',');
                sb.Append("\r\n");
            }
            Indent(sb, level);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable seq, int level)
        {
            var items = new List<object>();
            foreach (var x in seq) items.Add(x);
            if (items.Count == 0) { sb.Append("[]"); return; }

            // Короткі масиви простих значень — в один рядок.
            bool simple = items.Count <= 16 && items.TrueForAll(x => !(x is IDictionary<string, object>) && (x is string || !(x is IEnumerable)));
            if (simple)
            {
                sb.Append('[');
                for (int j = 0; j < items.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    WriteValue(sb, items[j], level);
                }
                sb.Append(']');
                return;
            }

            sb.Append("[\r\n");
            for (int j = 0; j < items.Count; j++)
            {
                Indent(sb, level + 1);
                WriteValue(sb, items[j], level + 1);
                if (j < items.Count - 1) sb.Append(',');
                sb.Append("\r\n");
            }
            Indent(sb, level);
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- зручні читачі ----------

        public static string GetString(IDictionary<string, object> d, string key, string def, params string[] allowed)
        {
            if (d != null && d.TryGetValue(key, out var v) && v is string s)
            {
                if (allowed == null || allowed.Length == 0) return s;
                foreach (var a in allowed) if (a == s) return s;
            }
            return def;
        }

        public static bool GetBool(IDictionary<string, object> d, string key, bool def)
            => d != null && d.TryGetValue(key, out var v) && v is bool b ? b : def;

        public static double GetNumber(IDictionary<string, object> d, string key, double def, double min = double.MinValue, double max = double.MaxValue)
        {
            if (d != null && d.TryGetValue(key, out var v) && v is double x && !double.IsNaN(x))
                return Math.Max(min, Math.Min(max, x));
            return def;
        }

        public static IDictionary<string, object> GetObject(IDictionary<string, object> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v as IDictionary<string, object> : null;
    }
}
