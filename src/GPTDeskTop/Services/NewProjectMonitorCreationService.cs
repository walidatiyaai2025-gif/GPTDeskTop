using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record NewProjectMonitorCreationResult(
    string ProjectId,
    ProjectState State,
    NewChatMonitorWorkflowResult Workflow);

public sealed class NewProjectMonitorCreationService
{
    private readonly NewChatMonitorWorkflowService _workflow;
    private readonly ProjectStateStore _projectStore;

    public NewProjectMonitorCreationService(
        ChromeDevToolsService chrome,
        ChatGptMonitorService monitor,
        LocalDatabase database,
        ProjectStateStore? projectStore = null)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(database);
        _workflow = new NewChatMonitorWorkflowService(chrome, monitor, database);
        _projectStore = projectStore ?? new ProjectStateStore();
    }

    public async Task<NewProjectMonitorCreationResult> ExecuteAsync(
        NewProjectMonitorDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var bootstrapPrompt = ProjectBootstrapPromptBuilder.Build(draft);
        var workflow = await _workflow.ExecuteAsync(
            bootstrapPrompt,
            draft.MonitorReply,
            cancellationToken).ConfigureAwait(false);

        var stableConversationUrl = ChatGptConversationIdentity.Normalize(workflow.ConversationTab.Url);
        var projectName = draft.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? draft.Repository.Trim();
        var projectId = BuildProjectId(projectName, workflow.Monitor.Id);
        var now = DateTimeOffset.UtcNow;

        var state = new ProjectState
        {
            ProjectId = projectId,
            RepoUrl = $"https://github.com/{draft.Repository.Trim()}",
            ProjectName = projectName,
            MainGoal = draft.ProjectInstruction.Trim(),
            CurrentPhase = "Bootstrap complete",
            Status = "RUNNING",
            CurrentBranch = draft.Branch.Trim(),
            CurrentChatId = stableConversationUrl,
            CurrentMonitorId = workflow.Monitor.Id,
            ChatGeneration = 1,
            HealthScore = 100,
            RetryCount = 0,
            LastVerifiedAt = now,
            UpdatedAt = now,
            NextAction = "Monitor the verified ChatGPT project conversation and continue repository implementation."
        };
        state.ImportantDecisions.Add($"New Project Monitor created from saved GitHub profile for {draft.Repository.Trim()} on branch {draft.Branch.Trim()}.");
        state.ImportantDecisions.Add($"Stable ChatGPT conversation bound to saved monitor #{workflow.Monitor.Id}: {stableConversationUrl}");

        await _projectStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return new NewProjectMonitorCreationResult(projectId, state, workflow);
    }

    private static string BuildProjectId(string projectName, long monitorId)
    {
        var safeName = new string(projectName
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray())
            .Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "project";
        return $"{safeName}-monitor-{monitorId}";
    }
}
