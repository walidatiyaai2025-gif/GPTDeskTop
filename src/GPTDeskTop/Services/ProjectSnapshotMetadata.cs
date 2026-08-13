namespace GPTDeskTop.Services;

public sealed record ProjectSnapshotMetadata(
    string ProjectId,
    int StateVersion,
    DateTimeOffset CapturedAt,
    string Status,
    int ChatGeneration,
    string LastCommit,
    int TotalTasks,
    int CompletedTasks,
    int BlockedTasks);
