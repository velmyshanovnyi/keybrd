using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keybrd.Core
{
    /// <summary>
    /// Складає прості речення з готових ролей: хто (S), що робить з чимось (V), що саме (O),
    /// що робить сам (VI), де (P), коли або як (T), а також множина (SP, VP, VIP).
    /// Форми слів підібрано так, щоб узгодження не ламалося: дієслова в теперішньому часі
    /// третьої особи підходять до будь-якого підмета в однині, додатки вже стоять у знахідному відмінку.
    ///
    /// Слова, які зараз на вивченні («цілі»), вставляються в речення: у свою роль, якщо вона відома,
    /// або в рамку на кшталт «брат пише слово …». Сенс виходить умовний, але текст читається як мова,
    /// а не як список слів.
    /// </summary>
    public static class SentenceBuilder
    {
        public const char ShiftKey = '⇧'; // «клавіша» великої літери в списку відкритих символів

        private sealed class Lexicon
        {
            public Dictionary<string, string[]> Roles;
            public string[] Templates;      // повні речення
            public string[] Phrases;        // без розділових знаків — для етапу, коли відкрито ще не всі літери
            public Dictionary<string, string> RoleOf = new Dictionary<string, string>();
        }

        private static readonly Random Rnd = new Random();
        private static readonly Dictionary<string, Lexicon> Cache = new Dictionary<string, Lexicon>();

        private static Lexicon For(string layout)
        {
            if (Cache.TryGetValue(layout, out var lex)) return lex;
            lex = layout == "en" ? English() : Ukrainian();
            foreach (var role in lex.Roles)
                foreach (var w in role.Value)
                    if (w.IndexOf(' ') < 0 && !lex.RoleOf.ContainsKey(w)) lex.RoleOf[w] = role.Key;
            return Cache[layout] = lex;
        }

        private static string[] W(string list) => list.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();

        private static Lexicon Ukrainian() => new Lexicon
        {
            Roles = new Dictionary<string, string[]>
            {
                ["S"] = W("він;вона;воно;брат;сестра;мама;тато;друг;учень;дід;бабуся;кіт;пес;вітер;дощ;ранок;вечір;учитель;лікар;сусід;хлопець;дівчина;дитина;сонце;місяць;день;ніч;ліс;сад;дім;син;дочка;гість;водій;кухар;автор;герой;птах;кінь;вовк;їжак;лелека"),
                ["SP"] = W("вони;діти;люди;друзі;учні;птахи;хмари;зорі;сусіди;батьки;гості;хлопці;брати;сестри;коні"),
                ["V"] = W("читає;пише;бачить;знає;любить;шукає;чекає;несе;бере;дає;варить;малює;слухає;пам'ятає;купує;тримає;відкриває;знаходить;вчить;розуміє;згадує;показує;збирає"),
                ["VP"] = W("читають;пишуть;бачать;знають;люблять;шукають;чекають;несуть;беруть;дають;малюють;слухають;купують;тримають;збирають;розуміють"),
                ["VI"] = W("іде;біжить;спить;сидить;стоїть;живе;працює;співає;мовчить;росте;світить;шумить;летить;плаває;відпочиває;радіє;сміється;думає;чекає"),
                ["VIP"] = W("ідуть;біжать;сплять;сидять;стоять;живуть;працюють;співають;мовчать;ростуть;летять;плавають;радіють;думають"),
                ["O"] = W("книгу;воду;школу;дорогу;пісню;казку;каву;газету;руку;хату;річку;лист;хліб;чай;дім;сад;ліс;стіл;текст;урок;вірш;день;ранок;вечір;мову;правду;роботу;зошит;олівець;квітку;траву;музику;історію;відповідь"),
                ["P"] = W("тут;там;вдома;у лісі;в саду;на полі;біля річки;у місті;в селі;на горі;під деревом;у школі;на вулиці;біля моря;у парку;у дворі;на березі;біля вікна"),
                ["T"] = W("ще;вже;теж;також;тільки;дуже;майже;ніколи;потім;спочатку;зараз;тоді;сьогодні;завтра;вчора;зранку;ввечері;вночі;завжди;часто;іноді;знову;тепер;швидко;тихо;повільно;добре;разом;довго;рано;пізно"),
            },
            Templates = new[]
            {
                "{S} {V} {O}.",
                "{S} {VI} {P}.",
                "{T} {S} {V} {O}.",
                "{T} {S} {VI} {P}.",
                "{SP} {VP} {O}.",
                "{SP} {VIP} {P}.",
                "{T} {SP} {VIP} {P}.",
                "{S} {V} {O} і {VI} {P}.",
                "{SP} {VP} {O} і {VIP} {P}.",
                "{S} {V} {O}, а {S} {VI} {P}.",
                "{S} {VI} {P}, бо {S} {V} {O}.",
                "{S} {V} {O}, {O} і {O}.",
                "{S} {VI} {P}, а {SP} {VIP} {P}.",
                "{T} {S} {V} {O}, але {SP} {VIP}.",
                "{S} - це {S}.",
                "{O} {V} {S}, а не {S}.",
                "чи {V} {S} {O}?",
                "де {VI} {S}?",
                "як {T} {VI} {S}!",
                "{S} {V} {O}, але {S} {VI} {P}.",
                "{S} {VI} {P}, коли {SP} {VIP}.",
                "{S} {V} {O} та {O}.",
                "{S} {V} {O} або {O}.",
                "{S} знає, що {S} {VI} {P}.",
                "якщо {S} {VI}, {SP} {VIP} {P}.",
                "{SP} {VIP}, а {S} {V} {O}.",
                "хто {V} {O}?",
                "що {V} {S}?",
                "коли {SP} {VIP} {P}?",
                "{T} {SP} {VP} {O}!",
                "{S} {VI} {P}!",
                "{S} - не {S}.",
                "{P} - {S}, а {P} - {S}.",
                // Рамки для слів без відомої ролі (службові слова тощо)
                "{S} пише слово {X}.",
                "{S} знає слово {X}.",
                "{SP} знають слова {X} і {X}.",
                "{S} читає слова {X}, {X} і {X}.",
                "спочатку {X}, потім {X}.",
                "тут написано {X}.",
                "{S} шукає слово {X} у тексті.",
                "у реченні є слова {X} і {X}.",
                "{T} {S} друкує {X}, {X} і {X}.",
                "після слова {X} іде слово {X}.",
                "друкуй {X}, а потім {X}.",
                "слово {X} друкуй {T}.",
                "де слово {X}?",
                "чи є тут слово {X}?",
                "яке коротке слово {X}!",
                "знову {X}!",
                "{X} - це слово.",
                "{X} - не {X}.",
            },
            Phrases = new[] { "{S} {V} {O}", "{S} {VI} {P}", "{T} {S} {VI}", "{SP} {VIP} {P}", "{SP} {VP} {O}", "{T} {S} {V} {O}" },
        };

        private static Lexicon English() => new Lexicon
        {
            Roles = new Dictionary<string, string[]>
            {
                ["S"] = W("the cat;the dog;my friend;the teacher;a child;the wind;the sun;the girl;the boy;my brother;her sister;the farmer;the doctor;our guest;the driver;the artist;a bird;the river;the old man;the baby"),
                ["SP"] = W("the children;people;my friends;the birds;the students;our guests;the boys;the girls;the farmers;the clouds"),
                ["V"] = W("reads;writes;sees;knows;likes;finds;takes;gives;makes;keeps;holds;opens;brings;needs;wants;remembers;carries;watches"),
                ["VP"] = W("read;write;see;know;like;find;take;give;make;keep;hold;open;bring;need;want;remember;carry;watch"),
                ["VI"] = W("runs;sleeps;sits;stands;lives;works;sings;waits;grows;walks;plays;smiles;thinks;rests;swims"),
                ["VIP"] = W("run;sleep;sit;stand;live;work;sing;wait;grow;walk;play;smile;think;rest;swim"),
                ["O"] = W("a book;the letter;some water;the road;a song;the story;fresh bread;hot tea;the house;the garden;a picture;the answer;the key;a paper;the door;the window;a flower;the music;an idea;the truth"),
                ["P"] = W("here;there;at home;in the forest;near the river;in the city;at school;in the park;by the sea;on the hill;under the tree;in the garden;on the road;near the window"),
                ["T"] = W("today;now;often;always;again;sometimes;every day;at night;in the morning;slowly;quietly;together;early;late"),
            },
            Templates = new[]
            {
                "{S} {V} {O}.",
                "{S} {VI} {P}.",
                "{T} {S} {V} {O}.",
                "{T} {S} {VI} {P}.",
                "{SP} {VP} {O}.",
                "{SP} {VIP} {P}.",
                "{T} {SP} {VIP} {P}.",
                "{S} {V} {O} and {VI} {P}.",
                "{SP} {VP} {O} and {VIP} {P}.",
                "{S} {V} {O}, and {S} {VI} {P}.",
                "{S} {VI} {P}, but {SP} {VIP} {P}.",
                "{S} {V} {O}, {O} and {O}.",
                "{T} {S} {V} {O}, but {SP} {VIP}.",
                "{S} - {O}.",
                "who {V} {O}?",
                "where is {S}?",
                "how {T} {S} {VI}!",
                "{S} {VI} {P}, when {SP} {VIP}.",
                "{S} {V} {O} or {O}.",
                "{S} knows that {SP} {VIP} {P}.",
                "{SP} {VIP}, and {S} {V} {O}.",
                "who {VI} {P}?",
                "{T} {SP} {VP} {O}!",
                "{S} {VI} {P}!",
                "{P} - {S}, and {P} - {SP}.",
                // Frames for words without a known role
                "first {X}, then {X}.",
                "it says {X} here.",
                "{S} finds the word {X} in the text.",
                "the line has the words {X} and {X}.",
                "{T} {S} types {X}, {X} and {X}.",
                "the word {X} comes after {X}.",
                "type {X}, and then {X}.",
                "where is the word {X}?",
                "is the word {X} here?",
                "what a short word {X}!",
                "{X} again!",
                "{X} - a word.",
                "{X} - not {X}.",
                "{S} writes the word {X}.",
                "{S} knows the word {X}.",
                "{SP} know the words {X} and {X}.",
                "{S} reads the words {X}, {X} and {X}.",
            },
            Phrases = new[] { "{S} {V} {O}", "{S} {VI} {P}", "{T} {S} {VI}", "{SP} {VIP} {P}", "{SP} {VP} {O}", "{T} {S} {V} {O}" },
        };

        /// <summary>Чи відома роль цього слова (тобто чи можна поставити його в речення як підмет, дієслово тощо).</summary>
        public static bool HasRole(string layout, string word) => For(layout).RoleOf.ContainsKey(word);

        /// <summary>
        /// Одне речення (або фраза без розділових знаків, якщо крапку ще не відкрито).
        /// </summary>
        /// <param name="alpha">Відкриті літери.</param>
        /// <param name="symbols">Усі відкриті символи: літери, розділові знаки, ShiftKey.</param>
        /// <param name="fresh">Символи, які зараз закріплюються: речення з ними трапляються частіше.</param>
        /// <param name="targets">Слова на вивченні; використані вилучаються зі списку.</param>
        /// <returns>null, якщо з відкритих літер речення скласти не вдається.</returns>
        /// <param name="allowFrame">Чи можна цього разу взяти рамку «… слово X»; двох рамок поспіль не даємо.</param>
        /// <param name="isKnown">Чи слово вже вивчене: у вільні місця речення охочіше ставимо ще не вивчені.</param>
        /// <param name="usedFrame">Чи вийшла рамка (щоб наступне речення було звичайним).</param>
        public static string Build(string layout, string alpha, string symbols, string fresh, List<string> targets,
                                   bool allowFrame, Func<string, bool> isKnown, out bool usedFrame)
        {
            usedFrame = false;
            var lex = For(layout);
            bool full = symbols.IndexOf('.') >= 0;
            var pool = (full ? lex.Templates : lex.Phrases).Where(t => PunctOk(t, symbols, full)).ToList();
            if (pool.Count == 0) return null;

            bool Fits(string w) => w.All(c => c == ' ' || alpha.IndexOf(c) >= 0);
            bool HasFresh(string t) => t.Any(c => c != '.' && c != ' ' && !char.IsLetter(c) && c != '{' && c != '}' && fresh.IndexOf(c) >= 0);
            bool freshPunct = pool.Any(HasFresh);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                // Перша ціль визначає шаблон: її роль або рамка «слово …».
                string lead = targets.Count > 0 ? targets[0] : null;
                string leadRole = lead != null && lex.RoleOf.TryGetValue(lead, out var r0) ? r0 : null;
                bool hasFrameWord = targets.Any(t => !lex.RoleOf.ContainsKey(t));

                List<string> candidates;
                if (freshPunct && Rnd.NextDouble() < 0.4)
                {
                    // Знак, який зараз закріплюється, має траплятись регулярно — беремо речення з ним.
                    candidates = pool.Where(t => HasFresh(t) && (!t.Contains("{X}") || (allowFrame && hasFrameWord))).ToList();
                }
                else if (leadRole != null) candidates = pool.Where(t => t.Contains("{" + leadRole + "}") && !t.Contains("{X}")).ToList();
                else if (lead != null && allowFrame) candidates = pool.Where(t => t.Contains("{X}")).ToList();
                else candidates = pool.Where(t => !t.Contains("{X}")).ToList();   // звичайне речення; ціль зачекає
                if (candidates.Count == 0) candidates = pool.Where(t => !t.Contains("{X}")).ToList();
                if (candidates.Count == 0) return null;

                string template = candidates[Rnd.Next(candidates.Count)];

                var used = new HashSet<string>();
                var taken = new List<string>();
                var sb = new StringBuilder();
                bool ok = true;
                int i = 0;
                while (i < template.Length && ok)
                {
                    if (template[i] != '{') { sb.Append(template[i++]); continue; }
                    int close = template.IndexOf('}', i);
                    string role = template.Substring(i + 1, close - i - 1);
                    i = close + 1;

                    string word = null;
                    if (role == "X")
                    {
                        // Рамка для слів без відомої ролі; якщо цілі скінчились — будь-який підмет.
                        int idx = targets.FindIndex(t => !used.Contains(t) && !lex.RoleOf.ContainsKey(t));
                        if (idx < 0) idx = targets.FindIndex(t => !used.Contains(t));
                        if (idx >= 0) { word = targets[idx]; taken.Add(word); }
                    }
                    else
                    {
                        // Ціль із такою роллю (серед кількох найближчих) має перевагу над словником.
                        int idx = targets.FindIndex(0, Math.Min(targets.Count, 8),
                            t => !used.Contains(t) && lex.RoleOf.TryGetValue(t, out var rr) && rr == role);
                        if (idx >= 0) { word = targets[idx]; taken.Add(word); }
                    }
                    if (word == null)
                    {
                        string from = role == "X" ? "S" : role;
                        var options = lex.Roles[from].Where(w => !used.Contains(w) && Fits(w)).ToList();
                        if (options.Count == 0) { ok = false; break; }
                        // Ще не вивчені слова словника — у 70% випадків: речення теж має вчити нове.
                        var unknown = options.Where(w => w.IndexOf(' ') < 0 && !isKnown(w)).ToList();
                        word = unknown.Count > 0 && Rnd.NextDouble() < 0.7 ? unknown[Rnd.Next(unknown.Count)] : options[Rnd.Next(options.Count)];
                    }
                    used.Add(word);
                    sb.Append(word);
                }
                if (!ok) continue;

                foreach (var t in taken) targets.Remove(t);
                usedFrame = template.Contains("{X}");
                string s = sb.ToString();
                if (full && symbols.IndexOf(ShiftKey) >= 0) s = char.ToUpper(s[0]) + s.Substring(1);
                return s;
            }
            return null;
        }

        /// <summary>Шаблон можна використати, якщо всі його розділові знаки вже відкрито.</summary>
        private static bool PunctOk(string template, string symbols, bool full)
        {
            foreach (var c in template)
            {
                if (char.IsLetter(c) || c == ' ' || c == '{' || c == '}' || c == '\'') continue;
                if (c == '.' && full) continue;
                if (symbols.IndexOf(c) < 0) return false;
            }
            return true;
        }
    }
}
