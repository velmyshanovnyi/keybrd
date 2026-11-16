using System;
using System.IO;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Де лежать config.json і stats.json: поруч з exe, а якщо туди писати не можна —
    /// у %APPDATA%\Keybrd. Плюс атомарний запис файлів.
    /// </summary>
    public static class DataStore
    {
        public static string ExeDir { get; } = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string Dir { get; private set; }
        public static bool UsingFallback { get; private set; }

        public static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static void Resolve()
        {
            if (Dir != null) return;
            if (CanWrite(ExeDir))
            {
                Dir = ExeDir;
                return;
            }
            Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keybrd");
            Directory.CreateDirectory(Dir);
            UsingFallback = true;
        }

        public static string PathOf(string fileName) => Path.Combine(Dir, fileName);

        private static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".keybrd_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Пише у тимчасовий файл, потім замінює ним цільовий — файл не пошкодиться при збої.</summary>
        public static void WriteAtomic(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8);
            if (File.Exists(path))
                File.Replace(tmp, path, null, true);
            else
                File.Move(tmp, path);
        }
    }
}
