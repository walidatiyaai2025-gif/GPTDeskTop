namespace GPTDeskTop.Services;
public enum StallClockExclusion { None, WaitingForHuman, ModelDelayed, WaitingExternal, AwaitingApproval, UserPaused }
public static class StallClockExclusionPolicy
{
    public static bool Exclude(StallClockExclusion reason) => reason != StallClockExclusion.None;
}
