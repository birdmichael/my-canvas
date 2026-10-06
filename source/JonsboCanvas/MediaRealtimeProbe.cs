using System;
using System.Threading;

namespace JonsboCanvas
{
    internal static class MediaRealtimeProbe
    {
        public static int Main()
        {
            using (NeteaseMediaCollector media = new NeteaseMediaCollector(38476))
            {
                for (int index = 0; index < 12; index++)
                {
                    Thread.Sleep(500);
                    MusicSnapshot value = media.Collect();
                    Console.WriteLine("AVAILABLE={0} REALTIME={1} PLAYING={2} ID={3} ELAPSED={4:F2} PROGRESS={5:F2} TITLE={6} LYRIC={7}",
                        value.Available, value.Realtime, value.Playing, value.SongId,
                        value.ElapsedSeconds, value.Progress, value.Title, value.CurrentLyric);
                }
            }
            return 0;
        }
    }
}
