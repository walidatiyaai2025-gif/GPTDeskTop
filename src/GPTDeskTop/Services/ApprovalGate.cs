namespace GPTDeskTop.Services;
public static class ApprovalGate
{
    private static readonly string[] SensitiveOperations = ["force-push", "production-release", "delete-production-data", "destructive-migration", "change-secrets"];
    public static bool RequiresApproval(string operation) => SensitiveOperations.Contains(operation.Trim(), StringComparer.OrdinalIgnoreCase);
}
