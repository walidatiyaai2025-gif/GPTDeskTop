namespace GPTDeskTop.Services;

public static class ProjectBootstrapPromptBuilder
{
    public static string Build(NewProjectMonitorDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var repository = Require(draft.Repository, nameof(draft.Repository));
        var branch = Require(draft.Branch, nameof(draft.Branch));
        var instruction = Require(draft.ProjectInstruction, nameof(draft.ProjectInstruction));

        return $"""
You are continuing work as an implementation agent on this GitHub project.

Repository: https://github.com/{repository}
Working branch: {branch}

Project instruction from the operator:
{instruction}

Execution contract:
- Inspect the repository and existing GitHub issues/PRs before changing code.
- Continue from existing work; do not duplicate completed tasks.
- Work on the named branch and keep changes traceable in GitHub.
- Record implementation evidence in commits/PRs/issues as appropriate.
- Run the relevant build/tests for every code change and repair regressions before declaring completion.
- Never expose, print, copy, or request stored GitHub credentials or tokens.
- When work is blocked by a real human-only action, state the exact blocker and the smallest required action.

Start by determining the current repository state and execute the next concrete implementation step for the instruction above.
""";
    }

    private static string Require(string? value, string name)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) throw new ArgumentException($"{name} cannot be empty.", name);
        return trimmed;
    }
}
