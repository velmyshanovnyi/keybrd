using System;
using System.Collections.Generic;
using System.IO;

namespace Keybrd.Core
{
    /// <summary>Налаштування програми, що зберігаються в config.json поруч з exe.</summary>
    public sealed class AppConfig
    {
        public const string FileName = "config.json";
        public const int CurrentVersion = 1;

        public static AppConfig Current { get; private set; } = new AppConfig();

        /// <summary>Будь-яке налаштування змінилося через Update.</summary>
        public static event Action Changed;

        /// <summary>Змінити налаштування, зберегти файл і повідомити всі частини програми.</summary>
        public static void Update(Action<AppConfig> change)
        {
            change(Current);
            Current.Save();
            Changed?.Invoke();
        }

        /// <summary>Файл був пошкоджений і його скинуто (копія — config.bak.json).</summary>
        public static bool WasReset { get; private set; }

        public static string DataDir => DataStore.Dir;
        public static string FilePath => DataStore.PathOf(FileName);

        // --- Вигляд ---
        public string Theme = "system";          // light | dark | system
        public string Accent = "blue";           // blue | teal | green | purple | orange | pink | system
        // Mica (Windows 11 22H2+) вимкнено: з власним заголовком DWM малює під ним системні
        // кнопки й колір заголовка. Лишаємо як прихований параметр.
        public bool UseMica = false;
        public string KeyboardStyle = "standard"; // standard | laptop
        public bool ShowFnRow = true;
        public bool ShowFingerColors = true;
        public bool ShowAltLetters = false;
        public bool FocusMode = false;
        public bool AutoHideKeyboard = false;    // клавіатура згасає, коли немає помилок
        public bool Eggs = false;                // «Пасхалки»: ілюзії та фрактали на місці схованої клавіатури
        public int DebugEgg = -1;                // прихований: завжди показувати пасхалку з цим номером (тест)
        public bool Voice = false;               // озвучувати текст вправи
        public int VoiceRate = 0;                // швидкість мовлення −5…5
        public int BreakMinutes = 10;            // перерва в уроці «Без зупинки»; 0 — без перерв
        public bool DebugLog = false;            // журнал debug.log для тестів
        public int DebugDayOffset = 0;           // прихований: зсув «сьогодні» на N днів (тест повторень)
        public bool SingleLine = false;          // біжучий рядок: один рядок тексту, поточна літера по центру
        public double TextFontSize = 28;

        // --- Навчання ---
        public string Layout = "uk";             // uk | en
        public string SpeedUnit = "cpm";         // cpm | wpm
        public double UnlockAccuracy = 95;
        public bool UnlockAll = false;        // відкрити всі уроки одразу
        public bool Sound = false;               // звук при помилці
        public bool CheckUpdates = true;         // при старті перевіряти нову версію на GitHub
        public bool DebugNoWarn = false;         // прихований: не показувати попередження про розкладку Windows (знімки для README)
        public string DebugUpdateUrl = "";       // прихований: інша адреса файлу версії (тест)
        public bool StrictShift = false;         // Shift тієї ж руки — помилка
        public bool FixMode = false;             // помилки виправляються Backspace
        public int TargetCpm = 150;              // цільовий темп для метронома і бігунка
        public bool Metronome = false;
        public bool Pacer = false;
        public int DailyGoal = 15;               // денна ціль, хв
        public int ExamMinutes = 1;              // тривалість контрольного тесту
        public string LessonUk = "uk-1-1";
        public string LessonEn = "en-1-1";

        // --- Свій текст ---
        public string CustomText = "";
        public int CustomPos;

        // --- Вікно ---
        public double WinX = double.NaN, WinY = double.NaN, WinW = 1100, WinH = 720;
        public bool WinMaximized;
        public bool Pinned;                      // вікно поверх усіх інших

        // --- Типовий текст для «Свого тексту»: файл default_text.txt поруч з exe ---
        public static string DefaultTextFile(string layout) => $"default_text_{layout}.txt";

        /// <summary>
        /// Типовий текст для розкладки: файл default_text_uk.txt / default_text_en.txt поруч з exe,
        /// а якщо його немає — текст, вбудований у програму.
        /// </summary>
        public static string DefaultText(string layout)
        {
            try
            {
                var path = DataStore.PathOf(DefaultTextFile(layout));
                if (File.Exists(path))
                {
                    var own = File.ReadAllText(path, DataStore.Utf8).Trim();
                    if (own.Length > 0) return own;
                }
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream($"Keybrd.Data.default_{layout}.txt"))
                {
                    if (s == null) return "";
                    using (var r = new StreamReader(s, DataStore.Utf8)) return r.ReadToEnd().Replace("\r\n", "\n").Trim();
                }
            }
            catch { return ""; }
        }

        public static bool SaveDefaultText(string text, string layout)
        {
            try { File.WriteAllText(DataStore.PathOf(DefaultTextFile(layout)), text ?? "", DataStore.Utf8); return true; }
            catch { return false; }
        }

        /// <summary>Свій текст, якщо його можна набрати в цій розкладці; інакше — типовий для неї.</summary>
        public string EffectiveCustomText(string layout)
        {
            if (!string.IsNullOrWhiteSpace(CustomText) && ExerciseGenerator.NormalizeCustom(CustomText, layout).Length >= 10)
                return CustomText;
            return DefaultText(layout);
        }

        public string LessonFor(string layout) => layout == "en" ? LessonEn : LessonUk;

        public void SetLesson(string id)
        {
            if (id.StartsWith("en")) LessonEn = id;
            else LessonUk = id;
        }

        public static void Load()
        {
            DataStore.Resolve();
            var cfg = new AppConfig();
            var path = FilePath;

            if (File.Exists(path))
            {
                try
                {
                    var root = Json.Parse(File.ReadAllText(path, DataStore.Utf8)) as IDictionary<string, object>;
                    if (root == null) throw new FormatException("JSON: корінь не є об'єктом");
                    cfg.ReadFrom(root);
                }
                catch (Exception)
                {
                    try { File.Copy(path, DataStore.PathOf("config.bak.json"), true); } catch { }
                    cfg = new AppConfig();
                    WasReset = true;
                }
            }

            Current = cfg;
            cfg.Save(); // створює файл, якщо його ще немає, і дописує нові поля
        }

        public void Save()
        {
            try
            {
                DataStore.WriteAtomic(FilePath, Json.Write(ToDict()));
            }
            catch (Exception)
            {
                // Не вдалося записати (наприклад, флешку витягнули) — програма працює далі.
            }
        }

        private void ReadFrom(IDictionary<string, object> d)
        {
            Theme = Json.GetString(d, "theme", Theme, "light", "dark", "system");
            Accent = Json.GetString(d, "accent", Accent, "blue", "teal", "green", "purple", "orange", "pink", "system");
            UseMica = Json.GetBool(d, "useMica", UseMica);
            KeyboardStyle = Json.GetString(d, "keyboardStyle", KeyboardStyle, "standard", "laptop");
            ShowFnRow = Json.GetBool(d, "showFnRow", ShowFnRow);
            ShowFingerColors = Json.GetBool(d, "showFingerColors", ShowFingerColors);
            ShowAltLetters = Json.GetBool(d, "showAltLetters", ShowAltLetters);
            FocusMode = Json.GetBool(d, "focusMode", FocusMode);
            SingleLine = Json.GetBool(d, "singleLine", SingleLine);
            DebugLog = Json.GetBool(d, "debugLog", DebugLog);
            DebugDayOffset = (int)Json.GetNumber(d, "debugDayOffset", 0, -10000, 10000);
            AutoHideKeyboard = Json.GetBool(d, "autoHideKeyboard", AutoHideKeyboard);
            Eggs = Json.GetBool(d, "eggs", Eggs);
            DebugEgg = (int)Json.GetNumber(d, "debugEgg", -1, -1, 100);
            Voice = Json.GetBool(d, "voice", Voice);
            VoiceRate = (int)Json.GetNumber(d, "voiceRate", VoiceRate, -5, 5);
            BreakMinutes = (int)Json.GetNumber(d, "breakMinutes", BreakMinutes, 0, 120);
            TextFontSize = Json.GetNumber(d, "textFontSize", TextFontSize, 14, 64);

            Layout = Json.GetString(d, "layout", Layout, "uk", "en");
            SpeedUnit = Json.GetString(d, "speedUnit", SpeedUnit, "cpm", "wpm");
            UnlockAccuracy = Json.GetNumber(d, "unlockAccuracy", UnlockAccuracy, 50, 100);
            UnlockAll = Json.GetBool(d, "unlockAll", UnlockAll);
            Sound = Json.GetBool(d, "sound", Sound);
            CheckUpdates = Json.GetBool(d, "checkUpdates", CheckUpdates);
            DebugUpdateUrl = Json.GetString(d, "debugUpdateUrl", "");
            DebugNoWarn = Json.GetBool(d, "debugNoWarn", false);
            StrictShift = Json.GetBool(d, "strictShift", StrictShift);
            FixMode = Json.GetBool(d, "fixMode", FixMode);
            TargetCpm = (int)Json.GetNumber(d, "targetCpm", TargetCpm, 60, 600);
            Metronome = Json.GetBool(d, "metronome", Metronome);
            Pacer = Json.GetBool(d, "pacer", Pacer);
            DailyGoal = (int)Json.GetNumber(d, "dailyGoal", DailyGoal, 1, 120);
            ExamMinutes = (int)Json.GetNumber(d, "examMinutes", ExamMinutes, 1, 30);
            CustomText = Json.GetString(d, "customText", CustomText);
            CustomPos = (int)Json.GetNumber(d, "customPos", CustomPos, 0, int.MaxValue);

            var lessons = Json.GetObject(d, "currentLesson");
            LessonUk = Json.GetString(lessons, "uk", LessonUk);
            LessonEn = Json.GetString(lessons, "en", LessonEn);

            var w = Json.GetObject(d, "window");
            WinX = Json.GetNumber(w, "x", WinX);
            WinY = Json.GetNumber(w, "y", WinY);
            WinW = Json.GetNumber(w, "w", WinW, 400, 10000);
            WinH = Json.GetNumber(w, "h", WinH, 300, 10000);
            WinMaximized = Json.GetBool(w, "maximized", WinMaximized);
            Pinned = Json.GetBool(w, "pinned", Pinned);
        }

        private Dictionary<string, object> ToDict()
        {
            var window = new Dictionary<string, object>
            {
                ["x"] = double.IsNaN(WinX) ? null : (object)Math.Round(WinX),
                ["y"] = double.IsNaN(WinY) ? null : (object)Math.Round(WinY),
                ["w"] = Math.Round(WinW),
                ["h"] = Math.Round(WinH),
                ["maximized"] = WinMaximized,
                ["pinned"] = Pinned,
            };

            var result = new Dictionary<string, object>
            {
                ["version"] = CurrentVersion,
                ["theme"] = Theme,
                ["accent"] = Accent,
                ["useMica"] = UseMica,
                ["layout"] = Layout,
                ["keyboardStyle"] = KeyboardStyle,
                ["showFnRow"] = ShowFnRow,
                ["showFingerColors"] = ShowFingerColors,
                ["showAltLetters"] = ShowAltLetters,
                ["focusMode"] = FocusMode,
                ["singleLine"] = SingleLine,
                ["autoHideKeyboard"] = AutoHideKeyboard,
                ["eggs"] = Eggs,
                ["voice"] = Voice,
                ["voiceRate"] = VoiceRate,
                ["breakMinutes"] = BreakMinutes,
                ["speedUnit"] = SpeedUnit,
                ["unlockAccuracy"] = UnlockAccuracy,
                ["unlockAll"] = UnlockAll,
                ["textFontSize"] = TextFontSize,
                ["sound"] = Sound,
                ["checkUpdates"] = CheckUpdates,
                ["strictShift"] = StrictShift,
                ["fixMode"] = FixMode,
                ["targetCpm"] = TargetCpm,
                ["metronome"] = Metronome,
                ["pacer"] = Pacer,
                ["dailyGoal"] = DailyGoal,
                ["examMinutes"] = ExamMinutes,
                ["currentLesson"] = new Dictionary<string, object> { ["uk"] = LessonUk, ["en"] = LessonEn },
                ["customText"] = CustomText,
                ["customPos"] = CustomPos,
                ["window"] = window,
            };
            // Приховані параметри для тестів пишемо, лише коли їх увімкнено.
            if (DebugLog) result["debugLog"] = true;
            if (DebugDayOffset != 0) result["debugDayOffset"] = DebugDayOffset;
            return result;
        }
    }
}
