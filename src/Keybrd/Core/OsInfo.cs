using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Keybrd.Core
{
    public static class OsInfo
    {
        /// <summary>Номер збірки Windows: 19045 — Windows 10 22H2, 22000+ — Windows 11.</summary>
        public static int Build { get; } = ReadBuild();

        private static int ReadBuild()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out var b)) return b;
            }
            catch { }
            return Environment.OSVersion.Version.Build;
        }

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        /// <summary>Чи збігається поточна розкладка Windows (для потоку вікна) з розкладкою вправи.</summary>
        public static bool WindowsLayoutMatches(string layout)
        {
            int lang = (int)(GetKeyboardLayout(0).ToInt64() & 0xFFFF);
            int primary = lang & 0x3FF;
            return layout == "en" ? primary == 0x09 : primary == 0x22;
        }

        public const int SM_CXSIZEFRAME = 32;
        public const int SM_CYSIZEFRAME = 33;
        public const int SM_CXPADDEDBORDER = 92;
    }
}
