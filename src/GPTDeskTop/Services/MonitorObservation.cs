namespace GPTDeskTop.Services;
public sealed record MonitorObservation(DateTimeOffset At, bool Generating, bool ComposerReady, bool HumanCheck, bool BreakReminder, bool ModelDelayed, int ResponseLength, string Activity, string? EvidenceFingerprint);
