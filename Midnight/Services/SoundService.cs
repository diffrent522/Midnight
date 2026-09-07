using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Midnight.Services
{
    public static class SoundService
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern bool PlaySound(byte[]? ptrToSound, IntPtr hmod, uint fdwSound);

        private const uint SND_ASYNC = 0x0001;
        private const uint SND_NODEFAULT = 0x0002;
        private const uint SND_MEMORY = 0x0004;

        private static readonly byte[]? _creamyClickWav;

        public static bool IsEnabled { get; set; } = true;

        static SoundService()
        {
            try
            {
                _creamyClickWav = GenerateCreamyClickWav();
            }
            catch
            {
                _creamyClickWav = null;
            }
        }

        public static void PlayClick()
        {
            if (!IsEnabled || _creamyClickWav == null) return;
            try
            {
                PlaySound(_creamyClickWav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch
            {
            }
        }

        private static byte[] GenerateCreamyClickWav()
        {
            int sampleRate = 44100;
            int sampleCount = (int)(sampleRate * 0.034);
            short[] pcm = new short[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / sampleRate;
                double progress = (double)i / sampleCount;

                double attack = t < 0.0016 ? (t / 0.0016) : Math.Exp(-(t - 0.0016) * 125.0);
                double window = Math.Cos(progress * Math.PI * 0.5);
                double envelope = attack * window;

                double phase = 2.0 * Math.PI * (230.0 * t + (410.0 / 175.0) * (1.0 - Math.Exp(-175.0 * t)));
                double body = Math.Sin(phase);

                double warmSub = 0.28 * Math.Sin(2.0 * Math.PI * 180.0 * t) * Math.Exp(-t * 90.0);
                double snapTransient = 0.22 * Math.Sin(2.0 * Math.PI * 1250.0 * t) * Math.Exp(-t * 420.0);

                double sample = (body * 0.75 + warmSub + snapTransient) * envelope;
                sample = Math.Clamp(sample, -1.0, 1.0);
                pcm[i] = (short)(sample * 30000);
            }

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 });
            writer.Write(36 + sampleCount * 2);
            writer.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 });
            writer.Write(new byte[] { 0x66, 0x6D, 0x74, 0x20 });
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((ushort)2);
            writer.Write((ushort)16);
            writer.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 });
            writer.Write(sampleCount * 2);

            for (int i = 0; i < sampleCount; i++)
            {
                writer.Write(pcm[i]);
            }

            return ms.ToArray();
        }
    }
}

