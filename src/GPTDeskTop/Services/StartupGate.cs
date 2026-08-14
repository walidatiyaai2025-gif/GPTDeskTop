namespace GPTDeskTop.Services;
public static class StartupGate
{
    public static bool CanRun(RuntimeWiringState wiring, RuntimeHealthSnapshot health) => wiring.Ready && health.Operational;
}
