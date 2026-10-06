namespace JonsboCanvas_WinUI;

internal readonly record struct MusicLightFrame(byte Brightness, bool Beat, double Flash);

// Audio energy -> light level. Sampling stays independent of HID latency.
// The light never drops below a floor (a dim flicker at low brightness looks
// like a fault, not a rhythm). The body follows the loudness of the full band
// quickly; kicks - the bass band jumping well above its recent average - fire
// a flash to full brightness that decays over a beat.
internal sealed class MusicLightEnvelope
{
    internal const double Floor = 0.3;
    private const double BodyCeiling = 0.72;
    private const double FlashSeconds = 0.2;
    private const double MinBeatGap = 0.28;

    private double _reference = 0.05;
    private double _body;
    private double _bassFast;
    private double _bassSlow;
    private double _bassReference = 0.02;
    private bool _armed = true;
    private double _flash;
    private double _sinceBeat = 1;

    public MusicLightFrame Sample(float bass, float full, int maximumPercent, double elapsedSeconds)
    {
        double dt = Math.Clamp(elapsedSeconds, 0.001, 0.25);
        double b = float.IsFinite(bass) ? Math.Clamp(bass, 0, 1) : 0;
        double f = float.IsFinite(full) ? Math.Clamp(full, 0, 1) : 0;
        double K(double tau) => 1 - Math.Exp(-dt / tau);

        // Loudness follower: rises with choruses, decays over a few seconds.
        _reference += (f - _reference) * K(f > _reference ? 0.3 : 3);
        _reference = Math.Max(0.01, _reference);
        double loudness = Math.Clamp(f / (_reference * 1.15), 0, 1);
        _body += (loudness - _body) * K(loudness > _body ? 0.03 : 0.16);

        _bassFast += (b - _bassFast) * K(0.012);
        _bassSlow += (b - _bassSlow) * K(0.3);
        _bassReference += (_bassFast - _bassReference) * K(_bassFast > _bassReference ? 0.2 : 3);
        _bassReference = Math.Max(0.004, _bassReference);
        // One beat per rise: re-arm once the bass has fallen back.
        if (_bassFast < _bassSlow * 1.12) _armed = true;
        _sinceBeat += dt;
        bool beat = _armed && _sinceBeat >= MinBeatGap && _bassFast > 0.0008
            && _bassFast > _bassSlow * 1.45 && _bassFast > _bassReference * 0.45;
        if (beat)
        {
            _flash = 1;
            _sinceBeat = 0;
            _armed = false;
        }
        else _flash *= Math.Exp(-dt / FlashSeconds);

        double level = Floor + (BodyCeiling - Floor) * _body;
        level += (1 - level) * _flash;
        int maximum = Math.Clamp(maximumPercent, 0, 100) * 255 / 100;
        return new MusicLightFrame((byte)Math.Clamp(Math.Round(level * maximum), 0, maximum), beat, _flash);
    }
}
