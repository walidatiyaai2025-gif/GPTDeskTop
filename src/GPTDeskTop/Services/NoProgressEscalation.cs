namespace GPTDeskTop.Services;
public enum NoProgressAction { Observe, Warn, Recover, RotateChat, Block }
public static class NoProgressEscalation
{
    public static NoProgressAction Decide(int cycles, int recoveriesUsed, int recoveryLimit)
    {
        if (cycles < 2) return NoProgressAction.Observe;
        if (cycles < 3) return NoProgressAction.Warn;
        if (recoveriesUsed < recoveryLimit) return NoProgressAction.Recover;
        if (cycles < 5) return NoProgressAction.RotateChat;
        return NoProgressAction.Block;
    }
}
