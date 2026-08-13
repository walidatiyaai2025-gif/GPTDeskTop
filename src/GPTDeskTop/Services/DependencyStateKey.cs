namespace GPTDeskTop.Services;
public static class DependencyStateKey
{
    public static string Build(string repository, string identifier, GitHubCheckState state) => $"{repository}|{identifier}|{state}";
}
