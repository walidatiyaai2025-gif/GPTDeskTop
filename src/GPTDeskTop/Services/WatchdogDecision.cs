namespace GPTDeskTop.Services;
public enum WatchdogAction { None, Warn, MarkSuspected, StopAndRecover, RotateChat, WaitForHuman }
public sealed record WatchdogDecision(WatchdogAction Action, string Reason);
