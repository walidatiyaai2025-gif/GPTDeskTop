namespace GPTDeskTop.Services;
public sealed record RuntimeHealthSnapshot(RuntimeIntegrationState State, DateTimeOffset CheckedAt, string Detail)
{
    public bool Operational => State is RuntimeIntegrationState.Connected or RuntimeIntegrationState.Degraded or RuntimeIntegrationState.Recovering;
}
