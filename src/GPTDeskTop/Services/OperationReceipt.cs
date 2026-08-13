namespace GPTDeskTop.Services;
public sealed record OperationReceipt(string OperationId, string ProjectId, string OperationType, string PayloadFingerprint, DateTimeOffset CreatedAt, string Status)
{
    public static OperationReceipt Pending(string operationId, string projectId, string operationType, string payloadFingerprint) =>
        new(operationId, projectId, operationType, payloadFingerprint, DateTimeOffset.UtcNow, "PENDING");
    public OperationReceipt Complete() => this with { Status = "COMPLETED" };
}
