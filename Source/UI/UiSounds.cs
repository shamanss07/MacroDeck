using System;
using System.IO;
using System.Media;

namespace MacroDeck
{
    internal static class UiSounds
    {
        private static readonly MemoryStream recordStartStream = new MemoryStream(CreateWave(new int[] { 1040, 1320 }, 75, 115));
        private static readonly MemoryStream recordStopStream = new MemoryStream(CreateWave(new int[] { 700, 540 }, 80, 120));
        private static readonly MemoryStream playbackStartStream = new MemoryStream(CreateWave(new int[] { 820, 1080 }, 100, 140));
        private static readonly MemoryStream playbackStopStream = new MemoryStream(CreateWave(new int[] { 520, 390 }, 110, 150));
        private static readonly SoundPlayer recordStartPlayer = new SoundPlayer(recordStartStream);
        private static readonly SoundPlayer recordStopPlayer = new SoundPlayer(recordStopStream);
        private static readonly SoundPlayer playbackStartPlayer = new SoundPlayer(playbackStartStream);
        private static readonly SoundPlayer playbackStopPlayer = new SoundPlayer(playbackStopStream);

        public static void RecordingStart() { Play(recordStartStream, recordStartPlayer); }
        public static void RecordingStop() { Play(recordStopStream, recordStopPlayer); }
        public static void PlaybackStart() { Play(playbackStartStream, playbackStartPlayer); }
        public static void PlaybackStop() { Play(playbackStopStream, playbackStopPlayer); }

        private static void Play(MemoryStream stream, SoundPlayer player)
        {
            try { stream.Position = 0; player.Play(); } catch { }
        }

        private static byte[] CreateWave(int[] tones, int firstMs, int secondMs)
        {
            const int rate = 22050; int silence = (int)(rate * 0.035); int[] lengths = { rate * firstMs / 1000, silence, rate * secondMs / 1000 };
            int samples = lengths[0] + lengths[1] + lengths[2]; byte[] data = new byte[44 + samples * 2];
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream(data)))
            {
                writer.Write(new char[] { 'R','I','F','F' }); writer.Write(36 + samples * 2); writer.Write(new char[] { 'W','A','V','E' });
                writer.Write(new char[] { 'f','m','t',' ' }); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(new char[] { 'd','a','t','a' }); writer.Write(samples * 2);
                int[] frequencies = { tones[0], 0, tones[1] }; int segment = 0; int within = 0;
                for (int i = 0; i < samples; i++)
                {
                    while (segment < 2 && within >= lengths[segment]) { within = 0; segment++; }
                    short value = frequencies[segment] == 0 ? (short)0 : (short)(Math.Sin(2.0 * Math.PI * frequencies[segment] * within++ / rate) * 5200.0);
                    if (frequencies[segment] == 0) within++;
                    writer.Write(value);
                }
            }
            return data;
        }
    }
}
