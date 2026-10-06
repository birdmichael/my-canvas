using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace JonsboCanvas
{
    internal sealed class MusicSnapshot
    {
        public bool Available;
        public string SongId = "";
        public string Title = "等待网易云播放";
        public string Artist = "NETEASE CLOUD MUSIC";
        public string Album = "";
        public string CurrentLyric = "打开网易云音乐并开始播放";
        public string PreviousLyric = "";
        public string NextLyric = "";
        public string CurrentTranslation = "";
        public double Progress;
        public double ElapsedSeconds;
        public double DurationSeconds;
        public double LyricProgress;
        public double LyricLineSeconds;
        // Word timing of the current line when the song has word-level (yrc)
        // lyrics, otherwise null. The words concatenate to CurrentLyric.
        public LyricWord[] CurrentWords;
        // Every lyric line of the song and the index of the current one (-1
        // before the first line).
        public string[] AllLyrics = new string[0];
        public int LyricIndex = -1;
        public Bitmap Cover;
        public bool Realtime;
        public bool Playing;
    }

    internal sealed class LyricWord
    {
        public string Text;
        // Seconds from the start of the line.
        public double Start;
        public double Duration;
    }

    internal sealed class NeteaseMediaCollector : IDisposable
    {
        private sealed class QueueRoot
        {
            public List<QueueItem> list { get; set; }
            public List<QueueItem> queue { get; set; }
        }

        private sealed class QueueItem
        {
            public string id { get; set; }
            public Track track { get; set; }
        }

        private sealed class Track
        {
            public string id { get; set; }
            public string name { get; set; }
            public int duration { get; set; }
            public List<Artist> artists { get; set; }
            public Album album { get; set; }
        }

        private sealed class Artist { public string name { get; set; } }
        private sealed class Album
        {
            public string name { get; set; }
            public string albumName { get; set; }
            public string picUrl { get; set; }
            public string cover { get; set; }
        }

        private sealed class LyricResponse
        {
            public LyricPayload lrc { get; set; }
            public LyricPayload tlyric { get; set; }
            public LyricPayload yrc { get; set; }
        }
        private sealed class LyricPayload { public string lyric { get; set; } }
        private sealed class RichLyricLine
        {
            public double t { get; set; }
            public List<RichLyricChunk> c { get; set; }
        }
        private sealed class RichLyricChunk { public string tx { get; set; } }
        private sealed class SongDetailResponse { public List<Track> songs { get; set; } }
        private sealed class LyricLine
        {
            public double Seconds;
            public string Text;
            public string Translation = "";
            public LyricWord[] Words;
        }

        private readonly string _root;
        private readonly string _webDataDirectory;
        private readonly JavaScriptSerializer _serializer;
        private string _songId = "";
        private Track _track;
        private Bitmap _cover;
        private List<LyricLine> _lyrics = new List<LyricLine>();
        private string[] _lyricTexts = new string[0];
        private string _lyricsSongId = "";
        private DateTime _nextLyricsRetryUtc;
        private readonly NeteaseCdpBridge _bridge;
        private string _failedSongId = "";
        private DateTime _nextSongRetryUtc;

        public bool RealtimeConnected { get { return _bridge.Connected; } }

        public void ReconnectRealtime()
        {
            _bridge.Reconnect();
        }

        public NeteaseMediaCollector(int debugPort)
        {
            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "netease", "CloudMusic");
            _webDataDirectory = Path.Combine(_root, "webdata", "file");
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            _bridge = new NeteaseCdpBridge(debugPort);
        }

        public MusicSnapshot Collect()
        {
            MusicSnapshot result = new MusicSnapshot();
            if (!CloudMusicRunning())
                return result;

            NeteasePlaybackSnapshot playback = _bridge.GetSnapshot();
            // Taskbar-Lyrics and LibSongInfo both use the native player event
            // stream as the only source of truth. Never mix in the newest cache
            // file while CDP is connected: that file can belong to another song
            // and was the cause of title/progress/lyrics jumping between tracks.
            string currentId = playback.Connected ? playback.SongId : "";
            if (string.IsNullOrWhiteSpace(currentId))
                return result;

            if (!string.Equals(currentId, _songId, StringComparison.Ordinal))
                LoadSong(currentId);
            if (_track == null)
                return result;

            bool realtime = playback.HasPosition && playback.SongId == _songId;
            if (!realtime)
                return result;
            double elapsed = playback.PositionSeconds;
            double duration = playback.DurationSeconds > 0 ? playback.DurationSeconds :
                Math.Max(1, _track.duration / 1000.0);
            string previousLyric;
            string currentLyric;
            string nextLyric;
            double lyricProgress;
            double lyricLineSeconds;
            string translation;
            int lyricIndex;
            EnsureLyrics(currentId);
            GetLyrics(elapsed, out previousLyric, out currentLyric, out nextLyric, out translation,
                out lyricProgress, out lyricLineSeconds, out lyricIndex);

            result.Available = true;
            result.SongId = _songId;
            result.Title = OrValue(_track.name, "网易云音乐");
            result.Artist = JoinArtists(_track.artists);
            result.Album = _track.album == null ? "" : OrValue(_track.album.albumName, _track.album.name);
            result.PreviousLyric = previousLyric;
            result.CurrentLyric = currentLyric;
            result.NextLyric = nextLyric;
            result.CurrentTranslation = translation;
            result.Progress = Math.Max(0, Math.Min(100, elapsed * 100.0 / duration));
            result.ElapsedSeconds = elapsed;
            result.DurationSeconds = duration;
            result.LyricProgress = lyricProgress;
            result.LyricLineSeconds = lyricLineSeconds;
            result.LyricIndex = lyricIndex;
            result.AllLyrics = _lyricTexts;
            result.CurrentWords = lyricIndex >= 0 ? _lyrics[lyricIndex].Words : null;
            result.Cover = _cover;
            result.Realtime = realtime;
            result.Playing = playback.Playing;
            return result;
        }

        private void LoadSong(string songId)
        {
            if (songId == _failedSongId && DateTime.UtcNow < _nextSongRetryUtc)
                return;
            Track track = FindTrack(songId);
            if (track == null)
            {
                _failedSongId = songId;
                _nextSongRetryUtc = DateTime.UtcNow.AddSeconds(10);
                return;
            }

            _songId = songId;
            _failedSongId = "";
            _track = track;
            _lyricsSongId = songId;
            _lyrics = DownloadLyrics(songId);
            _lyricTexts = _lyrics.ConvertAll(delegate(LyricLine line) { return line.Text; }).ToArray();
            _nextLyricsRetryUtc = _lyrics.Count == 0
                ? DateTime.UtcNow.AddSeconds(10) : DateTime.MaxValue;
            Bitmap nextCover = DownloadCover(track);
            if (nextCover != null)
            {
                Bitmap old = _cover;
                _cover = nextCover;
                if (old != null)
                    old.Dispose();
            }
            Log.Write("Netease now playing: " + track.name + " [" + songId + "]");
        }

        private Track FindTrack(string songId)
        {
            Track found = FindTrackInFile(Path.Combine(_webDataDirectory, "playingList"), songId);
            found = found ?? FindTrackInFile(Path.Combine(_webDataDirectory, "fmPlay"), songId);
            return found ?? DownloadTrack(songId);
        }

        private Track DownloadTrack(string songId)
        {
            try
            {
                string url = "https://music.163.com/api/song/detail?ids=[" +
                    Uri.EscapeDataString(songId) + "]";
                SongDetailResponse response = _serializer.Deserialize<SongDetailResponse>(DownloadText(url));
                return response == null || response.songs == null || response.songs.Count == 0
                    ? null : response.songs[0];
            }
            catch (Exception exception)
            {
                Log.Write("Netease song detail download failed: " + exception.Message);
                return null;
            }
        }

        private Track FindTrackInFile(string path, string songId)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                QueueRoot root = _serializer.Deserialize<QueueRoot>(File.ReadAllText(path, Encoding.UTF8));
                List<QueueItem> items = root == null ? null : (root.list ?? root.queue);
                if (items == null)
                    return null;
                foreach (QueueItem item in items)
                {
                    Track track = item == null ? null : item.track;
                    if (track == null && item != null && item.id == songId)
                        continue;
                    string id = track == null ? item.id : track.id;
                    if (id == songId)
                        return track;
                }
            }
            catch (Exception exception)
            {
                Log.Write("Netease playlist parse failed: " + exception.Message);
            }
            return null;
        }

        private List<LyricLine> DownloadLyrics(string songId)
        {
            List<LyricLine> lines = new List<LyricLine>();
            try
            {
                // Match the MIT Taskbar-Lyrics implementation. Its line timeline
                // is driven by the exact seconds delivered by onPlayProgress.
                // tv=-1 also returns the translated lyric (tlyric) when the song has one;
                // yv=1 the word-timed lyric (yrc) when it has one.
                string url = "https://music.163.com/api/song/lyric/v1?tv=-1&lv=0&rv=0&kv=0" +
                    "&yv=1&ytv=0&yrv=0&cp=false&id=" + Uri.EscapeDataString(songId);
                string json = DownloadText(url);
                LyricResponse response = _serializer.Deserialize<LyricResponse>(json);
                string lrc = response == null || response.lrc == null ? "" : response.lrc.lyric;
                ParseLrc(lrc, lines);
                lines.Sort(delegate(LyricLine left, LyricLine right)
                {
                    return left.Seconds.CompareTo(right.Seconds);
                });
                int translated = AttachTranslations(lines,
                    response == null || response.tlyric == null ? "" : response.tlyric.lyric);
                List<LyricLine> worded = ParseYrc(response == null || response.yrc == null ? "" : response.yrc.lyric);
                bool useWords = worded.Count > 0 && worded.Count * 2 >= lines.Count;
                if (useWords)
                {
                    TakeTranslations(worded, lines);
                    lines = worded;
                }
                Log.Write("Netease lyrics loaded: " + lines.Count + " lines, " + translated +
                    " translated [" + songId + "] " + (useWords ? "YRC" : "LRC"));
            }
            catch (Exception exception)
            {
                Log.Write("Netease lyric download failed: " + exception.Message);
            }
            return lines;
        }

        private void ParseLrc(string lyric, List<LyricLine> lines)
        {
            Regex timestampPattern = new Regex(@"\[(\d{1,3}):(\d{2})(?:[\.:](\d{1,3}))?\]");
            Match offsetMatch = Regex.Match(lyric ?? "", @"\[offset:([+-]?\d+)\]",
                RegexOptions.IgnoreCase);
            double offsetSeconds = offsetMatch.Success
                ? int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0
                : 0;
            foreach (string raw in (lyric ?? "").Split(
                new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        RichLyricLine rich = _serializer.Deserialize<RichLyricLine>(trimmed);
                        if (rich != null && rich.c != null)
                        {
                            StringBuilder text = new StringBuilder();
                            foreach (RichLyricChunk chunk in rich.c)
                                if (chunk != null && chunk.tx != null)
                                    text.Append(chunk.tx);
                            AddLyricLine(lines, rich.t / 1000.0 + offsetSeconds, text.ToString());
                        }
                    }
                    catch (Exception exception)
                    {
                        Log.Write("Netease rich lyric line ignored: " + exception.Message);
                    }
                    continue;
                }

                MatchCollection timestamps = timestampPattern.Matches(raw);
                if (timestamps.Count == 0)
                    continue;
                Match lastTimestamp = timestamps[timestamps.Count - 1];
                string lineText = raw.Substring(lastTimestamp.Index + lastTimestamp.Length);
                foreach (Match timestamp in timestamps)
                {
                    int minutes = int.Parse(timestamp.Groups[1].Value, CultureInfo.InvariantCulture);
                    int seconds = int.Parse(timestamp.Groups[2].Value, CultureInfo.InvariantCulture);
                    string fraction = timestamp.Groups[3].Value;
                    double milliseconds = string.IsNullOrEmpty(fraction) ? 0 :
                        int.Parse(fraction.PadRight(3, '0').Substring(0, 3), CultureInfo.InvariantCulture);
                    AddLyricLine(lines, minutes * 60 + seconds + milliseconds / 1000.0 +
                        offsetSeconds, lineText);
                }
            }
        }

        // Word-timed lyric: "[lineStartMs,lineMs](wordStartMs,wordMs,0)word(...)word".
        // Word times are absolute; they are stored relative to the line start.
        private static readonly Regex YrcLine = new Regex(@"^\[(\d+),(\d+)\](.*)$");
        private static readonly Regex YrcWord = new Regex(@"\((\d+),(\d+),-?\d+\)");

        private static List<LyricLine> ParseYrc(string yrc)
        {
            List<LyricLine> lines = new List<LyricLine>();
            foreach (string raw in (yrc ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match line = YrcLine.Match(raw.Trim());
                if (!line.Success)
                    continue;
                double lineStart = long.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0;
                string body = line.Groups[3].Value;
                MatchCollection tags = YrcWord.Matches(body);
                List<LyricWord> words = new List<LyricWord>();
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < tags.Count; i++)
                {
                    int from = tags[i].Index + tags[i].Length;
                    int to = i + 1 < tags.Count ? tags[i + 1].Index : body.Length;
                    string word = body.Substring(from, to - from);
                    if (word.Length == 0)
                        continue;
                    words.Add(new LyricWord
                    {
                        Text = word,
                        Start = long.Parse(tags[i].Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0 - lineStart,
                        Duration = long.Parse(tags[i].Groups[2].Value, CultureInfo.InvariantCulture) / 1000.0,
                    });
                    text.Append(word);
                }
                int before = lines.Count;
                AddLyricLine(lines, lineStart, text.ToString());
                if (lines.Count > before)
                {
                    // AddLyricLine trims the text; trim the words to match.
                    if (words.Count > 0)
                    {
                        words[0].Text = words[0].Text.TrimStart();
                        words[words.Count - 1].Text = words[words.Count - 1].Text.TrimEnd();
                    }
                    lines[lines.Count - 1].Words = words.ToArray();
                }
            }
            lines.Sort(delegate(LyricLine left, LyricLine right) { return left.Seconds.CompareTo(right.Seconds); });
            return lines;
        }

        // Word-timed lines start a little off the LRC timestamps the
        // translations follow, so each takes the nearest LRC line's translation.
        private static void TakeTranslations(List<LyricLine> worded, List<LyricLine> lrc)
        {
            foreach (LyricLine line in worded)
            {
                LyricLine nearest = null;
                foreach (LyricLine candidate in lrc)
                    if (nearest == null || Math.Abs(candidate.Seconds - line.Seconds) < Math.Abs(nearest.Seconds - line.Seconds))
                        nearest = candidate;
                if (nearest != null && Math.Abs(nearest.Seconds - line.Seconds) <= 1.0)
                    line.Translation = nearest.Translation;
            }
        }

        // Translated lines share the original lines' timestamps.
        private int AttachTranslations(List<LyricLine> lines, string translatedLrc)
        {
            if (string.IsNullOrWhiteSpace(translatedLrc))
                return 0;
            List<LyricLine> translations = new List<LyricLine>();
            ParseLrc(translatedLrc, translations);
            int attached = 0;
            foreach (LyricLine translation in translations)
            {
                foreach (LyricLine line in lines)
                {
                    if (Math.Abs(line.Seconds - translation.Seconds) > 0.05)
                        continue;
                    if (!string.Equals(line.Text, translation.Text, StringComparison.Ordinal))
                    {
                        line.Translation = translation.Text;
                        attached++;
                    }
                    break;
                }
            }
            return attached;
        }

        private static void AddLyricLine(List<LyricLine> lines, double seconds, string rawText)
        {
            string text = (rawText ?? "").Trim();
            if (text.Length == 0 || text.StartsWith("作词") || text.StartsWith("作詞") ||
                text.StartsWith("作曲") || text.StartsWith("编曲") || text.StartsWith("混音") ||
                text.StartsWith("制作人"))
                return;
            lines.Add(new LyricLine { Seconds = Math.Max(0, seconds), Text = text });
        }

        private void EnsureLyrics(string songId)
        {
            if (_lyrics.Count > 0 || songId != _lyricsSongId || DateTime.UtcNow < _nextLyricsRetryUtc)
                return;
            _lyrics = DownloadLyrics(songId);
            _lyricTexts = _lyrics.ConvertAll(delegate(LyricLine line) { return line.Text; }).ToArray();
            _nextLyricsRetryUtc = _lyrics.Count == 0
                ? DateTime.UtcNow.AddSeconds(15) : DateTime.MaxValue;
        }

        private Bitmap DownloadCover(Track track)
        {
            try
            {
                string url = track.album == null ? "" : OrValue(track.album.picUrl, track.album.cover);
                if (string.IsNullOrWhiteSpace(url))
                    return null;
                byte[] data = DownloadBytes(url.Replace("http://", "https://") +
                    "?imageView&thumbnail=500y500&type=jpg");
                using (MemoryStream stream = new MemoryStream(data))
                using (Image image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
            catch (Exception exception)
            {
                Log.Write("Netease cover download failed: " + exception.Message);
                return LoadNewestCachedCover();
            }
        }

        private Bitmap LoadNewestCachedCover()
        {
            try
            {
                string directory = Path.Combine(_root, "Statics");
                FileInfo newest = null;
                foreach (string path in Directory.EnumerateFiles(directory, "*.dat"))
                {
                    if (Path.GetFileName(path).Equals("index.dat", StringComparison.OrdinalIgnoreCase))
                        continue;
                    FileInfo file = new FileInfo(path);
                    if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                        newest = file;
                }
                if (newest == null)
                    return null;
                using (Image image = Image.FromFile(newest.FullName))
                    return new Bitmap(image);
            }
            catch { return null; }
        }

        private static string DownloadText(string url)
        {
            return Encoding.UTF8.GetString(DownloadBytes(url));
        }

        private static byte[] DownloadBytes(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Mozilla/5.0 JonsboCanvas/1.0";
            request.Referer = "https://music.163.com/";
            request.Timeout = 4000;
            request.ReadWriteTimeout = 4000;
            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (MemoryStream output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private void GetLyrics(double elapsed, out string previous, out string current,
            out string next, out string translation, out double progress, out double lineSeconds, out int lineIndex)
        {
            previous = "";
            current = "♪  " + OrValue(_track == null ? "" : _track.name, "正在播放");
            next = "";
            translation = "";
            progress = 0;
            lineSeconds = 0;
            lineIndex = -1;
            if (_lyrics.Count == 0)
                return;

            // Same selection rule as Taskbar-Lyrics: find the first lyric at
            // or after the native progress value, then display the line before
            // it. Before the first lyric, keep the song title instead of
            // showing a future line early.
            int index = -1;
            for (int i = 0; i < _lyrics.Count; i++)
            {
                if (_lyrics[i].Seconds <= elapsed + 0.005)
                    index = i;
                else
                    break;
            }
            if (index < 0)
            {
                next = _lyrics[0].Text;
                return;
            }
            lineIndex = index;
            current = _lyrics[index].Text;
            translation = _lyrics[index].Translation;
            if (index > 0)
                previous = _lyrics[index - 1].Text;
            if (index + 1 < _lyrics.Count)
                next = _lyrics[index + 1].Text;
            double start = _lyrics[index].Seconds;
            double end = index + 1 < _lyrics.Count ? _lyrics[index + 1].Seconds : start + 6.0;
            if (end <= start)
                end = start + 1.0;
            progress = Math.Max(0, Math.Min(1, (elapsed - start) / (end - start)));
            lineSeconds = end - start;
        }

        private static string JoinArtists(List<Artist> artists)
        {
            if (artists == null || artists.Count == 0)
                return "NETEASE CLOUD MUSIC";
            List<string> names = new List<string>();
            foreach (Artist artist in artists)
                if (artist != null && !string.IsNullOrWhiteSpace(artist.name))
                    names.Add(artist.name);
            return names.Count == 0 ? "NETEASE CLOUD MUSIC" : string.Join(" / ", names.ToArray());
        }

        private static bool CloudMusicRunning()
        {
            try { return Process.GetProcessesByName("cloudmusic").Length > 0; }
            catch { return false; }
        }

        private static string OrValue(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? (fallback ?? "") : value;
        }

        public void Dispose()
        {
            _bridge.Dispose();
            if (_cover != null)
                _cover.Dispose();
            _cover = null;
        }
    }
}
