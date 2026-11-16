using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Keybrd.Core
{
    public enum LessonKind { Keys, Words, Capitals, Sentences, Digits, Symbols, Fn, Speed, Free, Adaptive, Custom, Dynamic, Endless, Confusion, Daily, Placement, Exam, Dictation }

    public sealed class Lesson
    {
        public string Id;            // uk-2-3
        public string Layout;        // uk | en
        public int Stage, Number;
        public string StageTitle;
        public string Title;
        public string Description;
        public string NewKeys = "";  // нові символи цього уроку (для показу й акценту у вправі)
        public string Letters = "";  // усі дозволені малі літери (накопичувально)
        public string Extra = "";    // дозволені розділові знаки й цифри (накопичувально)
        public bool Upper;           // дозволені великі літери
        public LessonKind Kind;
        public int MinCpm;
        public int Length = 150;

        /// <summary>Вільна практика або «слабкі місця»: без мети й зарахування.</summary>
        public bool IsFree => Kind >= LessonKind.Free;
        public string Code => IsFree ? "—" : $"{Stage}.{Number}";
    }

    /// <summary>Курси для української та латиниці.</summary>
    public static class Course
    {
        private static readonly Dictionary<string, List<Lesson>> Cache = new Dictionary<string, List<Lesson>>();

        public static List<Lesson> For(string layout)
        {
            if (!Cache.TryGetValue(layout, out var list))
                Cache[layout] = list = layout == "en" ? BuildEn() : BuildUk();
            return list;
        }

        public static Lesson Free(string layout) => new Lesson
        {
            Id = layout + "-free", Layout = layout, Kind = LessonKind.Free, Stage = 0,
            StageTitle = "Вільна практика", Title = "Часті слова",
            Description = "Випадкові часті слова без обмежень. Підходить для розминки.",
            Letters = layout == "en" ? "abcdefghijklmnopqrstuvwxyz'" : "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя'",
            Upper = true, Extra = ".,", MinCpm = 0, Length = 150,
        };

        public static Lesson Weak(string layout)
        {
            var l = Free(layout);
            l.Id = layout + "-weak";
            l.Kind = LessonKind.Adaptive;
            l.Title = "Слабкі місця";
            l.Description = "Вправа складається з ваших найслабших клавіш і сполучень літер за статистикою: де найбільше помилок і де ви найдовше думаєте. Кожна нова вправа враховує свіжі результати.";
            l.Length = 180;
            return l;
        }

        public static Lesson Custom(string layout)
        {
            var l = Free(layout);
            l.Id = layout + "-custom";
            l.Kind = LessonKind.Custom;
            l.Title = "Свій текст";
            l.Description = "Вставте будь-який текст або відкрийте файл .txt — його буде набрано частинами приблизно по 200 символів, по черзі. Символи, яких немає на клавіатурі, пропускаються, а лапки й тире замінюються на звичайні.";
            l.Length = 200;
            return l;
        }

        public static Lesson Dynamic(string layout)
        {
            var l = Free(layout);
            l.Id = layout + "-dynamic";
            l.Kind = LessonKind.Dynamic;
            l.Title = "Динамічне навчання";
            l.Description = "Програма сама підбирає слова: починає з простих і стежить, як ви їх набираєте. " +
                            "Слово, яке кілька разів набрано без помилок і в рівному ритмі, вважається вивченим і більше не показується — " +
                            "замість нього приходять складніші. Сполучення літер, на яких ви помиляєтесь або зупиняєтесь, " +
                            "повертаються на донавчання в простіших словах і з повторами.";
            l.Length = 170;
            return l;
        }

        public static Lesson Endless(string layout)
        {
            var l = Free(layout);
            l.Id = layout + "-endless";
            l.Kind = LessonKind.Endless;
            l.Title = "Без зупинки";
            l.Description = "Те саме динамічне навчання, але текст не закінчується: друкуйте, скільки є часу й бажання. " +
                            "Програма на ходу стежить за ритмом і помилками, відкриває нові літери, додає нові склади і слова, " +
                            "а слабкі місця одразу повертає на донавчання. Щоб закінчити й побачити підсумок, натисніть Esc.";
            l.Length = 110; // довжина однієї порції тексту
            return l;
        }

        private static Lesson Practice(string layout, string suffix, LessonKind kind, string title, string description, int length)
        {
            var l = Free(layout);
            l.Id = layout + "-" + suffix;
            l.Kind = kind;
            l.Title = title;
            l.Description = description;
            l.Length = length;
            return l;
        }

        public static Lesson Confusion(string layout) => Practice(layout, "confusion", LessonKind.Confusion, "Плутанина",
            "Програма запам’ятовує, яку клавішу ви натискаєте замість потрібної. Ця вправа ставить такі літери поруч: " +
            "по черзі, у контрастних складах і в словах з кожною з них, щоб пальці навчилися їх розрізняти.", 190);

        public static Lesson Daily(string layout) => Practice(layout, "daily", LessonKind.Daily, "Щоденне заняття",
            "Готове заняття на день: розминка на вивчених словах, повторення того, чому настав час, нові слова і наприкінці — " +
            "швидкість на тому, що вже в пальцях. Триває, доки не виконано денну ціль (її можна змінити в налаштуваннях), і завершується саме. " +
            "Короткі заняття щодня дають більше, ніж довгі зрідка.", 110);

        public static Lesson Exam(string layout) => Practice(layout, "exam", LessonKind.Exam, "Контрольний тест",
            "Чесний замір: звичайні речення, які не підлаштовуються під вас, без клавіатури на екрані й без підказок. " +
            "Триває 1, 3 або 5 хвилин (змінюється в налаштуваннях) і зупиняється сам. Результати зберігаються окремо, " +
            "щоб порівнювати себе з собою місяць тому.", 120);

        public static Lesson Placement(string layout) => Practice(layout, "placement", LessonKind.Placement, "Вступний тест",
            "Для тих, хто вже трохи друкує. Один текст, де кожна літера трапляється щонайменше тричі. Після нього програма бачить, " +
            "які літери ви знаєте (без помилок і довгих пауз), відкриває їх у динамічному навчанні й зараховує відповідні уроки курсу. " +
            "Потрібні точність від 90% і швидкість від 60 зн/хв; інакше нічого не змінюється.", 420);

        public static Lesson Dictation(string layout) => Practice(layout, "dictation", LessonKind.Dictation, "Диктант",
            "Речення озвучуються, а ще не набраний текст приховано — друкуєте на слух, не читаючи. Tab повторює фразу. " +
            "Після трьох помилок на одному місці літера відкривається. Потрібен голос Windows для цієї мови; " +
            "якщо його немає, текст показується як звичайно.", 160);

        public static Lesson Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var layout = id.StartsWith("en") ? "en" : "uk";
            if (id.EndsWith("-free")) return Free(layout);
            if (id.EndsWith("-weak")) return Weak(layout);
            if (id.EndsWith("-custom")) return Custom(layout);
            if (id.EndsWith("-dynamic")) return Dynamic(layout);
            if (id.EndsWith("-endless")) return Endless(layout);
            if (id.EndsWith("-confusion")) return Confusion(layout);
            if (id.EndsWith("-daily")) return Daily(layout);
            if (id.EndsWith("-exam")) return Exam(layout);
            if (id.EndsWith("-placement")) return Placement(layout);
            if (id.EndsWith("-dictation")) return Dictation(layout);
            return For(layout).FirstOrDefault(l => l.Id == id);
        }

        public static Lesson Next(Lesson l)
        {
            if (l == null || l.IsFree) return null;
            var list = For(l.Layout);
            int i = list.IndexOf(l);
            return i >= 0 && i + 1 < list.Count ? list[i + 1] : null;
        }

        public static Lesson Previous(Lesson l)
        {
            if (l == null || l.IsFree) return null;
            var list = For(l.Layout);
            int i = list.IndexOf(l);
            return i > 0 ? list[i - 1] : null;
        }

        // ---------- побудова ----------

        private sealed class Builder
        {
            private readonly string layout;
            private readonly List<Lesson> list = new List<Lesson>();
            private string letters = "", extra = "";
            private bool upper;
            private int stage, number;
            private string stageTitle;

            public Builder(string layout) { this.layout = layout; }

            public void Stage(string title) { stage++; number = 0; stageTitle = title; }

            public void Add(LessonKind kind, string title, string newKeys, string desc, int minCpm,
                            string addLetters = "", string addExtra = "", bool setUpper = false, int length = 150)
            {
                letters += addLetters;
                extra += addExtra;
                if (setUpper) upper = true;
                number++;
                list.Add(new Lesson
                {
                    Id = $"{layout}-{stage}-{number}", Layout = layout, Stage = stage, Number = number,
                    StageTitle = stageTitle, Title = title, NewKeys = newKeys, Description = desc,
                    Letters = letters, Extra = extra, Upper = upper, Kind = kind, MinCpm = minCpm, Length = length,
                });
            }

            public List<Lesson> Done() => list;
        }

        private static List<Lesson> BuildUk()
        {
            var b = new Builder("uk");

            b.Stage("Основний ряд");
            // Основний ряд — по дві клавіші за урок, від вказівних пальців до мізинців.
            b.Add(LessonKind.Keys, "А і О", "ао",
                "Покладіть пальці на основний ряд: лівою рукою Ф І В А, правою О Л Д Ж. Вказівні пальці лежать на А та О — на цих клавішах є риски. Після кожного натискання палець повертається на своє місце.",
                30, addLetters: "ао", length: 90);
            b.Add(LessonKind.Keys, "В і Л", "вл", "Середні пальці: лівий на В, правий на Л. Тепер уже виходять справжні слова.", 35, addLetters: "вл");
            b.Add(LessonKind.Keys, "І і Д", "ід", "Безіменні пальці: лівий на І, правий на Д.", 40, addLetters: "ід");
            b.Add(LessonKind.Keys, "Ф і Ж", "фж", "Мізинці: лівий на Ф, правий на Ж. Основний ряд зібрано: ФІВА ОЛДЖ.", 40, addLetters: "фж");
            b.Add(LessonKind.Keys, "П і Р", "пр", "П натискає лівий вказівний, Р — правий вказівний. Після кожної літери пальці повертаються на А та О.", 45, addLetters: "пр");
            b.Add(LessonKind.Keys, "Є", "є", "Є натискає правий мізинець, тягнучись від Ж праворуч.", 45, addLetters: "є");
            b.Add(LessonKind.Words, "Слова основного ряду", "", "Справжні слова лише з літер основного ряду.", 50);

            b.Stage("Верхній ряд");
            b.Add(LessonKind.Keys, "Е і Н", "ен", "Вказівні пальці тягнуться вгору: лівий до Е, правий до Н.", 50, addLetters: "ен");
            b.Add(LessonKind.Keys, "К і Г", "кг", "К — лівий вказівний, Г — правий вказівний.", 55, addLetters: "кг");
            b.Add(LessonKind.Keys, "У і Ш", "уш", "У — лівий середній палець, Ш — правий середній.", 55, addLetters: "уш");
            b.Add(LessonKind.Keys, "Ц і Щ", "цщ", "Ц — лівий безіменний, Щ — правий безіменний.", 55, addLetters: "цщ");
            b.Add(LessonKind.Keys, "Й і З", "йз", "Й — лівий мізинець, З — правий мізинець.", 55, addLetters: "йз");
            b.Add(LessonKind.Keys, "Х і Ї", "хї", "Х та Ї — правий мізинець тягнеться далі праворуч.", 55, addLetters: "хї");
            b.Add(LessonKind.Words, "Слова: два ряди", "", "Слова з основного та верхнього рядів.", 65);

            b.Stage("Нижній ряд");
            b.Add(LessonKind.Keys, "М і Т", "мт", "Вказівні пальці тягнуться вниз: лівий до М, правий до Т.", 60, addLetters: "мт");
            b.Add(LessonKind.Keys, "И і Ь", "иь", "И — лівий вказівний, Ь — правий вказівний.", 60, addLetters: "иь");
            b.Add(LessonKind.Keys, "С і Б", "сб", "С — лівий середній, Б — правий середній.", 60, addLetters: "сб");
            b.Add(LessonKind.Keys, "Ч і Ю", "чю", "Ч — лівий безіменний, Ю — правий безіменний.", 60, addLetters: "чю");
            b.Add(LessonKind.Keys, "Я і крапка", "я.", "Я — лівий мізинець, крапка — правий мізинець.", 60, addLetters: "я", addExtra: ".");
            b.Add(LessonKind.Words, "Слова: усі літери", "", "Слова з усіх трьох рядів.", 75);

            b.Stage("Великі літери");
            b.Add(LessonKind.Capitals, "Shift", "", "Велика літера: Shift натискає мізинець протилежної руки. Для лівої руки — правий Shift, і навпаки.", 75, setUpper: true);
            b.Add(LessonKind.Sentences, "Речення з комою", ",", "Кома — Shift + клавіша крапки. Друкуйте справжні речення.", 80, addExtra: ",");

            b.Stage("Особливі літери");
            b.Add(LessonKind.Words, "Апостроф", "'", "Апостроф — клавіша ліворуч від 1, лівий мізинець.", 75, addLetters: "'");
            b.Add(LessonKind.Words, "Ґ", "ґ", "Ґ — клавіша над Enter, правий мізинець.", 75, addLetters: "ґ");

            b.Stage("Цифри і знаки");
            b.Add(LessonKind.Digits, "Цифри 1–5", "12345", "Цифри верхнього ряду лівою рукою: 1 мізинець, 2 безіменний, 3 середній, 4 і 5 вказівний.", 50, addExtra: "12345");
            b.Add(LessonKind.Digits, "Цифри 6–0", "67890", "Правою рукою: 6 і 7 вказівний, 8 середній, 9 безіменний, 0 мізинець.", 50, addExtra: "67890");
            b.Add(LessonKind.Symbols, "Розділові знаки", "!?;:-()", "Знаки на цифровому ряду з Shift та дефіс.", 50, addExtra: "!?;:-()");

            b.Stage("Ряд Fn");
            b.Add(LessonKind.Fn, "F1–F12 і Del", "", "Функціональні клавіші без погляду на клавіатуру. Ряд Fn увімкнеться автоматично.", 30);

            b.Stage("Слова і речення");
            b.Add(LessonKind.Words, "Часті слова", "", "Найуживаніші слова без обмежень.", 100);
            b.Add(LessonKind.Sentences, "Речення", "", "Справжні речення з великими літерами і розділовими знаками.", 100);

            b.Stage("Швидкість");
            foreach (var target in new[] { 150, 200, 250, 300 })
                b.Add(LessonKind.Speed, $"{target} зн/хв", "", $"Урок зараховується на швидкості від {target} знаків за хвилину.", target, length: 200);

            return b.Done();
        }

        private static List<Lesson> BuildEn()
        {
            var b = new Builder("en");

            b.Stage("Основний ряд");
            // Основний ряд — по дві клавіші за урок, від вказівних пальців до мізинців.
            b.Add(LessonKind.Keys, "F і J", "fj",
                "Пальці на основному ряду: лівою рукою A S D F, правою J K L ;. Вказівні лежать на F та J — на цих клавішах є риски. Після кожного натискання палець повертається на своє місце.",
                30, addLetters: "fj", length: 90);
            b.Add(LessonKind.Keys, "D і K", "dk", "Середні пальці: лівий на D, правий на K.", 30, addLetters: "dk", length: 90);
            b.Add(LessonKind.Keys, "S і L", "sl", "Безіменні пальці: лівий на S, правий на L.", 35, addLetters: "sl", length: 100);
            b.Add(LessonKind.Keys, "A і ;", "a;", "Мізинці: лівий на A, правий на крапці з комою. Тепер уже виходять справжні слова.", 40, addLetters: "a", addExtra: ";");
            b.Add(LessonKind.Keys, "G і H", "gh", "G — лівий вказівний, H — правий вказівний.", 45, addLetters: "gh");
            b.Add(LessonKind.Words, "Слова основного ряду", "", "Справжні слова з основного ряду.", 50);

            b.Stage("Верхній ряд");
            b.Add(LessonKind.Keys, "R і U", "ru", "Вказівні пальці тягнуться вгору: лівий до R, правий до U.", 50, addLetters: "ru");
            b.Add(LessonKind.Keys, "E і I", "ei", "E — лівий середній, I — правий середній.", 55, addLetters: "ei");
            b.Add(LessonKind.Keys, "T і Y", "ty", "T — лівий вказівний, Y — правий вказівний.", 55, addLetters: "ty");
            b.Add(LessonKind.Keys, "W і O", "wo", "W — лівий безіменний, O — правий безіменний.", 55, addLetters: "wo");
            b.Add(LessonKind.Keys, "Q і P", "qp", "Q — лівий мізинець, P — правий мізинець.", 55, addLetters: "qp");
            b.Add(LessonKind.Words, "Слова: два ряди", "", "Слова з основного та верхнього рядів.", 65);

            b.Stage("Нижній ряд");
            b.Add(LessonKind.Keys, "V і M", "vm", "Вказівні вниз: лівий до V, правий до M.", 60, addLetters: "vm");
            b.Add(LessonKind.Keys, "B і N", "bn", "B — лівий вказівний, N — правий вказівний.", 60, addLetters: "bn");
            b.Add(LessonKind.Keys, "C і кома", "c,", "C — лівий середній, кома — правий середній.", 60, addLetters: "c", addExtra: ",");
            b.Add(LessonKind.Keys, "X і крапка", "x.", "X — лівий безіменний, крапка — правий безіменний.", 60, addLetters: "x", addExtra: ".");
            b.Add(LessonKind.Keys, "Z", "z", "Z — лівий мізинець.", 60, addLetters: "z");
            b.Add(LessonKind.Words, "Слова: усі літери", "", "Слова з усіх трьох рядів.", 75);

            b.Stage("Великі літери");
            b.Add(LessonKind.Capitals, "Shift", "", "Велика літера: Shift натискає мізинець протилежної руки.", 75, setUpper: true);
            b.Add(LessonKind.Sentences, "Речення", "", "Справжні речення з великою літерою та крапкою.", 80);

            b.Stage("Особливі знаки");
            b.Add(LessonKind.Words, "Апостроф", "'", "Апостроф — клавіша праворуч від ;, правий мізинець.", 75, addLetters: "'");

            b.Stage("Цифри і знаки");
            b.Add(LessonKind.Digits, "Цифри 1–5", "12345", "Лівою рукою: 1 мізинець, 2 безіменний, 3 середній, 4 і 5 вказівний.", 50, addExtra: "12345");
            b.Add(LessonKind.Digits, "Цифри 6–0", "67890", "Правою рукою: 6 і 7 вказівний, 8 середній, 9 безіменний, 0 мізинець.", 50, addExtra: "67890");
            b.Add(LessonKind.Symbols, "Розділові знаки", "!?:-()\"", "Знаки з Shift на цифровому ряду, дефіс, лапки та двокрапка.", 50, addExtra: "!?:-()\"");

            b.Stage("Ряд Fn");
            b.Add(LessonKind.Fn, "F1–F12 і Del", "", "Функціональні клавіші без погляду на клавіатуру. Ряд Fn увімкнеться автоматично.", 30);

            b.Stage("Слова і речення");
            b.Add(LessonKind.Words, "Часті слова", "", "Найуживаніші слова без обмежень.", 100);
            b.Add(LessonKind.Sentences, "Речення", "", "Справжні речення з розділовими знаками.", 100);

            b.Stage("Швидкість");
            foreach (var target in new[] { 150, 200, 250, 300 })
                b.Add(LessonKind.Speed, $"{target} зн/хв", "", $"Урок зараховується на швидкості від {target} знаків за хвилину.", target, length: 200);

            return b.Done();
        }
    }
}
