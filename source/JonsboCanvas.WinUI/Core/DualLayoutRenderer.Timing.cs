using JonsboCanvas;

namespace JonsboCanvas_WinUI;

// When each word of the current line is sung, in seconds from the line start.
internal readonly record struct LyricSpan(float Start, float End)
{
    public float Fill(float elapsed) => End <= Start
        ? (elapsed >= Start ? 1 : 0)
        : Math.Clamp((elapsed - Start) / (End - Start), 0, 1);

    public static LyricSpan Union(LyricSpan first, LyricSpan last) => new(first.Start, last.End);
}

internal sealed partial class DualLayoutRenderer
{
    // Without a line length the progress fraction is the clock, over the
    // one-second window TokenSpans then spreads the line across.
    private static float LineElapsed(MusicSnapshot? music) => music == null ? 0
        : (float)(Math.Clamp(music.LyricProgress, 0, 1) * (music.LyricLineSeconds > 0 ? music.LyricLineSeconds : 1));

    // Timing for each token of the current line: from the word-timed lyric
    // when the song has one, otherwise spread over an estimate of the sung
    // length. LRC lines run until the next line starts, which includes any
    // instrumental gap, so the estimate is capped below the line's length.
    internal static LyricSpan[] TokenSpans(IReadOnlyList<string> tokens, MusicSnapshot? music, bool cjk)
    {
        int[] counts = tokens.Select(InkCount).ToArray();
        LyricWord[]? words = music?.CurrentWords;
        if (words is { Length: > 0 })
        {
            List<LyricSpan> chars = new();
            foreach (LyricWord word in words)
            {
                int count = InkCount(word.Text ?? "");
                for (int i = 0; i < count; i++)
                    chars.Add(new LyricSpan((float)(word.Start + word.Duration * i / count),
                        (float)(word.Start + word.Duration * (i + 1) / count)));
            }
            if (chars.Count == counts.Sum())
            {
                LyricSpan[] timed = new LyricSpan[tokens.Count];
                int k = 0;
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (counts[i] == 0)
                    {
                        float at = k > 0 ? chars[k - 1].End : 0;
                        timed[i] = new LyricSpan(at, at);
                        continue;
                    }
                    timed[i] = LyricSpan.Union(chars[k], chars[k + counts[i] - 1]);
                    k += counts[i];
                }
                return timed;
            }
        }

        double line = music?.LyricLineSeconds ?? 0;
        float sung = line <= 0 ? 1 : (float)Math.Max(0.3, Math.Min(line * 0.92, 0.4 + tokens.Count * (cjk ? 0.3 : 0.42)));
        float total = Math.Max(1, counts.Sum());
        LyricSpan[] spans = new LyricSpan[tokens.Count];
        float done = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            float start = done / total * sung;
            done += counts[i];
            spans[i] = new LyricSpan(start, done / total * sung);
        }
        return spans;
    }

    private static int InkCount(string text) => text.Count(c => !char.IsWhiteSpace(c));
}
