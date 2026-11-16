using System;
using System.IO;
using System.Media;

namespace Keybrd.Core
{
    /// <summary>Короткий тихий звук помилки, згенерований у пам'яті (без файлів поруч з exe).</summary>
    public static class Sounds
    {
        private static SoundPlayer error;

        public static void Error()
        {
            try
            {
                if (error == null)
                {
                    error = new SoundPlayer(Tone(220, 70, 0.25));
                    error.Load();
                }
                error.Play();
            }
            catch { /* немає аудіопристрою — мовчимо */ }
        }

        private static MemoryStream Tone(double freq, int ms, double volume)
        {
            const int rate = 22050;
            int samples = rate * ms / 1000;
            var mem = new MemoryStream();
            var w = new BinaryWriter(mem);
            w.Write(0x46464952); w.Write(36 + samples * 2); w.Write(0x45564157);           // RIFF, WAVE
            w.Write(0x20746D66); w.Write(16); w.Write((short)1); w.Write((short)1);          // fmt, PCM, моно
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(0x61746164); w.Write(samples * 2);                                       // data
            for (int i = 0; i < samples; i++)
            {
                double env = Math.Min(1, Math.Min(i, samples - i) / (rate * 0.005));          // м'які краї без клацання
                w.Write((short)(Math.Sin(2 * Math.PI * freq * i / rate) * short.MaxValue * volume * env));
            }
            w.Flush();
            mem.Position = 0;
            return mem;
        }
    }
}
