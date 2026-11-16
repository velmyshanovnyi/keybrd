using System;
using System.Diagnostics;
using System.IO;

namespace Keybrd.Core
{
    /// <summary>
    /// Журнал для тестів і діагностики (debug.log поруч з exe). Вмикається параметром
    /// "debugLog": true у config.json; у звичайній роботі нічого не пише.
    /// Рядок: мілісекунди від запуску, подія, дані — через табуляцію.
    /// </summary>
    public static class DebugLog
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static bool Enabled => AppConfig.Current.DebugLog;

        public static void Write(string kind, string data)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(DataStore.PathOf("debug.log"),
                    $"{Clock.ElapsedMilliseconds}\t{kind}\t{data}\r\n", DataStore.Utf8);
            }
            catch { }
        }
    }
}
