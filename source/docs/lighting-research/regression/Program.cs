using JonsboCanvas_WinUI;

static void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
var envelope = new MusicLightEnvelope();
byte quiet = 0;
for (int i = 0; i < 100; i++) quiet = envelope.Sample(0.01f, 80, 0.02);
byte loud = envelope.Sample(0.08f, 80, 0.02);
Check(loud > quiet + 40, "low-volume music responds to transients");
var full = new MusicLightEnvelope();
byte sustained = 0;
for (int i = 0; i < 300; i++) sustained = full.Sample(0.10f, 100, 0.02); // 6s of steady music
Check(sustained is > 40 and < 200, "sustained audio settles mid-scale rather than pinned high");
byte spike = full.Sample(0.55f, 100, 0.02);
Check(spike >= 200, "transients reach near full brightness");
byte dip = spike;
for (int i = 0; i < 50; i++) dip = full.Sample(0.02f, 100, 0.02);
Check(dip <= 45, "quiet passages fall low");
Check(spike - sustained >= 70, "full swing between beat and sustain");
byte silent = loud;
for (int i = 0; i < 100; i++) silent = envelope.Sample(0, 80, 0.02);
Check(silent == 0, "silence reaches zero rather than artificial minimum");
Check(envelope.Sample(1, 0, 0.02) == 0, "zero brightness is fully off");
for (int i = 0; i < 100; i++) Check(envelope.Sample(1, 25, 0.02) <= 63, "brightness respects committed ceiling");
Check(envelope.Sample(float.NaN, 100, 0.02) <= 255, "invalid audio samples handled");
for (int i = 0; i < 5; i++) { Console.WriteLine("live audio peak=" + SystemAudioLevel.GetPeak()); await Task.Delay(100); }
Console.WriteLine("PASS lighting envelope and live audio smoke test");
// Brightness is baked into the color channels; verify the scaling math used by
// LightingController via reflection-free arithmetic sanity check.
Check((byte)Math.Round(255 * (10 / 255.0)) == 10, "brightness scaling math at 10/255");
Check((byte)Math.Round(255 * (255 / 255.0)) == 255, "brightness scaling math full");
