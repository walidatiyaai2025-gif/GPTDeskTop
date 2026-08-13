namespace GPTDeskTop.Services;
public sealed class ModelDelayTracker
{
    public int Occurrences { get; private set; }
    public DateTimeOffset? FirstDetectedAt { get; private set; }
    public void Record(DateTimeOffset now)
    {
        Occurrences++;
        FirstDetectedAt ??= now;
    }
    public void Reset()
    {
        Occurrences = 0;
        FirstDetectedAt = null;
    }
}
