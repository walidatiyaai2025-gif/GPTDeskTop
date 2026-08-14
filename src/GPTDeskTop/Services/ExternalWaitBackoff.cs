namespace GPTDeskTop.Services;
public static class ExternalWaitBackoff
{
    public static TimeSpan NextDelay(int checkNumber, TimeSpan initial, TimeSpan maximum)
    {
        if (checkNumber <= 0) return initial;
        var factor = Math.Pow(2, Math.Min(checkNumber, 10));
        var next = TimeSpan.FromMilliseconds(initial.TotalMilliseconds * factor);
        return next <= maximum ? next : maximum;
    }
}
