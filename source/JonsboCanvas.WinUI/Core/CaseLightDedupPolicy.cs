namespace JonsboCanvas_WinUI;

internal readonly record struct CaseLight(byte R, byte G, byte B);

// Hardware-mode case lighting is persistent state. Skip an unchanged frame,
// while music pulses keep their existing high-rate write path.
internal static class CaseLightDedupPolicy
{
    internal static bool ShouldApply(
        CaseLight requested,
        CaseLight? applied,
        CaseLight? pending) =>
        applied != requested || pending != null;
}
