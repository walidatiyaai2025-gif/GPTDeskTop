namespace GPTDeskTop.Services;
public static class StartupGate
{
    public static bool CanRun(RuntimeWiringState wiring, OrchestratorRuntimeHealthSnapshot health) => wiring.Ready && health.Operational;
}
