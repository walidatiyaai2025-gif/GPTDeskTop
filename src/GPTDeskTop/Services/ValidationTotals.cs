namespace GPTDeskTop.Services;
public sealed record ValidationTotals(int Total, int Passed, int Failed, DateTimeOffset CompletedAt)
{
    public bool Successful => Total > 0 && Failed == 0 && Passed == Total;
}
