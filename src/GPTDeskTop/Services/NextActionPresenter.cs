using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class NextActionPresenter
{
    public static string Build(ProjectState state) => string.IsNullOrWhiteSpace(state.NextAction) ? "No next action recorded." : $"Next: {state.NextAction.Trim()}";
}
