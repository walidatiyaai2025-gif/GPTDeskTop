namespace GPTDeskTop.Services;
public sealed class RetryBudget
{
    public int Limit { get; init; } = 2;
    public int Used { get; private set; }
    public bool TryUse()
    {
        if (Used >= Limit) return false;
        Used++;
        return true;
    }
    public void Reset() => Used = 0;
}
