using GPTDeskTop.Models;
namespace GPTDeskTop.Services;
public static class TaskProgressLine
{
    public static string Build(ProjectState state)
    {
        var p = ProjectProgressService.Calculate(state);
        return $"Completed {p.Completed}/{p.Total} | Remaining {p.Remaining} | In progress {p.InProgress} | Blocked {p.Blocked}";
    }
}
