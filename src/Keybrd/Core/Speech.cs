using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Speech.Synthesis;

namespace Keybrd.Core
{
    /// <summary>
    /// Озвучення тексту вправи голосами, встановленими у Windows (працює без інтернету).
    /// Крім класичних голосів SAPI, пробуємо підключити сучасні голоси Windows (OneCore) —
    /// вони лежать в іншій гілці реєстру, тому додаються через внутрішні типи System.Speech.
    /// Якщо голосу для мови немає, озвучення просто не працює, а налаштування про це кажуть.
    /// </summary>
    public static class Speech
    {
        private static SpeechSynthesizer synth;
        private static bool failed;
        private static string currentVoice;

        private static SpeechSynthesizer Synth
        {
            get
            {
                if (synth != null || failed) return synth;
                try
                {
                    synth = new SpeechSynthesizer();
                    synth.SetOutputToDefaultAudioDevice();
                    AddOneCoreVoices(synth);
                }
                catch { failed = true; synth = null; }
                return synth;
            }
        }

        private static string Culture(string layout) => layout == "en" ? "en" : "uk";

        private static InstalledVoice Find(string layout)
        {
            var s = Synth;
            if (s == null) return null;
            try
            {
                string lang = Culture(layout);
                return s.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == lang);
            }
            catch { return null; }
        }

        /// <summary>Назва голосу для мови або null, якщо такого голосу у Windows немає.</summary>
        public static string VoiceName(string layout) => Find(layout)?.VoiceInfo.Name;

        public static bool Available(string layout) => Find(layout) != null;

        /// <summary>Почати говорити текст; попереднє мовлення переривається.</summary>
        public static void Say(string text, string layout)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var voice = Find(layout);
            if (voice == null) return;
            try
            {
                var s = Synth;
                s.SpeakAsyncCancelAll();
                if (currentVoice != voice.VoiceInfo.Name)
                {
                    s.SelectVoice(voice.VoiceInfo.Name);
                    currentVoice = voice.VoiceInfo.Name;
                }
                s.Rate = AppConfig.Current.VoiceRate;
                s.SpeakAsync(text);
            }
            catch { }
        }

        public static void Stop()
        {
            try { synth?.SpeakAsyncCancelAll(); } catch { }
        }

        /// <summary>Додати голоси з гілки Speech_OneCore. Будь-яка невдача тихо ігнорується.</summary>
        private static void AddOneCoreVoices(SpeechSynthesizer s)
        {
            try
            {
                const string path = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
                var asm = typeof(SpeechSynthesizer).Assembly;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                var voiceSynth = typeof(SpeechSynthesizer).GetProperty("VoiceSynthesizer", flags)?.GetValue(s);
                if (voiceSynth == null) return;
                var installed = voiceSynth.GetType().GetField("_installedVoices", flags)?.GetValue(voiceSynth) as IList;
                if (installed == null) return;

                var categoryType = asm.GetType("System.Speech.Internal.ObjectTokens.ObjectTokenCategory");
                var create = categoryType?.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                using (var category = create?.Invoke(null, new object[] { path }) as IDisposable)
                {
                    if (category == null) return;
                    var tokens = categoryType.GetMethod("FindMatchingTokens", flags)?.Invoke(category, new object[] { null, null }) as IEnumerable;
                    if (tokens == null) return;

                    var voiceInfoCtor = typeof(VoiceInfo).GetConstructors(flags).FirstOrDefault(c => c.GetParameters().Length == 1);
                    var installedCtor = typeof(InstalledVoice).GetConstructors(flags).FirstOrDefault(c => c.GetParameters().Length == 2);
                    if (voiceInfoCtor == null || installedCtor == null) return;

                    foreach (var token in tokens)
                    {
                        if (token == null) continue;
                        try
                        {
                            var info = (VoiceInfo)voiceInfoCtor.Invoke(new[] { token });
                            bool exists = installed.Cast<InstalledVoice>().Any(v => v.VoiceInfo.Name == info.Name);
                            if (!exists) installed.Add(installedCtor.Invoke(new object[] { voiceSynth, info }));
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
