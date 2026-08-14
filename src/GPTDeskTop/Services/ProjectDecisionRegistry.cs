using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

public sealed record ProjectDecision(string DecisionId, string Decision, string Status, string Scope, DateTimeOffset CreatedAt, string? Supersedes = null, string? Source = null);

public sealed class ProjectDecisionRegistry
{
    private readonly ProjectStateStore _store;
    public ProjectDecisionRegistry(ProjectStateStore store) => _store = store;

    public async Task AddAsync(string projectId, ProjectDecision decision, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(projectId, ct) ?? throw new KeyNotFoundException(projectId);
        var serialized = $"{decision.DecisionId}|{decision.Status}|{decision.Scope}|{decision.Decision}|supersedes={decision.Supersedes ?? ""}|source={decision.Source ?? ""}|created={decision.CreatedAt:O}";
        state.ImportantDecisions.RemoveAll(x => x.StartsWith(decision.DecisionId + "|", StringComparison.OrdinalIgnoreCase));
        state.ImportantDecisions.Add(serialized);
        await _store.SaveAsync(state, ct);
    }
}
