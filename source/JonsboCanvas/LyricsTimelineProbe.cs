using System;
using System.Collections;
using System.Reflection;

namespace JonsboCanvas
{
    internal static class LyricsTimelineProbe
    {
        public static int Main()
        {
            using (NeteaseMediaCollector collector = new NeteaseMediaCollector(38476))
            {
                Type type = typeof(NeteaseMediaCollector);
                MethodInfo parse = type.GetMethod("ParseLrc",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo timeline = type.GetMethod("GetLyrics",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo lyrics = type.GetField("_lyrics",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Type lyricLineType = type.GetNestedType("LyricLine", BindingFlags.NonPublic);
                IList parsed = (IList)Activator.CreateInstance(
                    typeof(System.Collections.Generic.List<>).MakeGenericType(lyricLineType));
                string lrc = "[offset:+100]\n" +
                    "[00:01.000][00:03.000]重复歌词\n" +
                    "[00:05.000]下一句歌词\n";
                parse.Invoke(collector, new object[] { lrc, parsed });
                lyrics.SetValue(collector, parsed);
                object[] arguments = { 1.100, null, null, null, 0d };
                timeline.Invoke(collector, arguments);

                Console.WriteLine("LINES=" + parsed.Count);
                Console.WriteLine("PREVIOUS=" + arguments[1]);
                Console.WriteLine("CURRENT=" + arguments[2]);
                Console.WriteLine("NEXT=" + arguments[3]);
                Console.WriteLine("PROGRESS=" + Convert.ToDouble(arguments[4]).ToString("0.000"));

                object[] laterArguments = { 5.100, null, null, null, 0d };
                timeline.Invoke(collector, laterArguments);
                typeof(EmbeddedRuntime).GetField("<RuntimeDirectory>k__BackingField",
                    BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Environment.CurrentDirectory);
                typeof(EmbeddedRuntime).GetField("<DataDirectory>k__BackingField",
                    BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Environment.CurrentDirectory);
                int changedPixels;
                using (DashboardRenderer renderer = new DashboardRenderer(new AppConfig()))
                using (System.Drawing.Bitmap first = renderer.RenderMusic(CreateSnapshot(arguments, 1.100)))
                using (System.Drawing.Bitmap later = renderer.RenderMusic(CreateSnapshot(laterArguments, 5.100)))
                    changedPixels = CountChangedPixels(first, later);
                Console.WriteLine("LATER_CURRENT=" + laterArguments[2]);
                Console.WriteLine("CHANGED_PIXELS=" + changedPixels);

                FieldInfo bridgeField = type.GetField("_bridge",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                NeteaseCdpBridge bridge = (NeteaseCdpBridge)bridgeField.GetValue(collector);
                MethodInfo handlePlayback = typeof(NeteaseCdpBridge).GetMethod(
                    "HandlePlaybackPayload", BindingFlags.Instance | BindingFlags.NonPublic);
                handlePlayback.Invoke(bridge, new object[] {
                    "{\"eventName\":\"load\",\"args\":[\"probe_song\",{\"duration\":228000}]}" });
                handlePlayback.Invoke(bridge, new object[] {
                    "{\"eventName\":\"progress\",\"args\":[\"probe_song\",4.25,0]}" });
                NeteasePlaybackSnapshot playing = bridge.GetSnapshot();
                handlePlayback.Invoke(bridge, new object[] {
                    "{\"eventName\":\"state\",\"args\":[\"probe_song\",\"player|pause|\"]}" });
                NeteasePlaybackSnapshot paused = bridge.GetSnapshot();
                Console.WriteLine("DURATION=" + playing.DurationSeconds.ToString("0.000"));
                Console.WriteLine("PAUSE_STATE=" + paused.Playing);

                return parsed.Count == 3 &&
                    Convert.ToString(arguments[2]) == "重复歌词" &&
                    Convert.ToString(arguments[3]) == "重复歌词" &&
                    Convert.ToString(laterArguments[2]) == "下一句歌词" &&
                    Math.Abs(playing.DurationSeconds - 228) < 0.001 &&
                    playing.Playing && !paused.Playing &&
                    changedPixels > 1000 ? 0 : 1;
            }
        }

        private static MusicSnapshot CreateSnapshot(object[] values, double elapsed)
        {
            return new MusicSnapshot
            {
                Available = true,
                SongId = "362937",
                Title = "烟火",
                Artist = "严艺丹",
                PreviousLyric = Convert.ToString(values[1]),
                CurrentLyric = Convert.ToString(values[2]),
                NextLyric = Convert.ToString(values[3]),
                LyricProgress = Convert.ToDouble(values[4]),
                ElapsedSeconds = elapsed,
                DurationSeconds = 260,
                Progress = elapsed * 100 / 260,
                Realtime = true,
                Playing = true
            };
        }

        private static int CountChangedPixels(System.Drawing.Bitmap left, System.Drawing.Bitmap right)
        {
            int changed = 0;
            for (int y = 0; y < left.Height; y += 2)
                for (int x = 0; x < left.Width; x += 2)
                    if (left.GetPixel(x, y).ToArgb() != right.GetPixel(x, y).ToArgb())
                        changed++;
            return changed;
        }
    }
}
