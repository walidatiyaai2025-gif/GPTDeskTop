namespace GPTDeskTop.Services;
public sealed record ExternalWaitResult(string TicketId, ExternalWaitStatus Status, string Detail, DateTimeOffset CheckedAt)
{
    public bool IsTerminal => Status is ExternalWaitStatus.Satisfied or ExternalWaitStatus.TimedOut or ExternalWaitStatus.Failed or ExternalWaitStatus.Cancelled;
}
