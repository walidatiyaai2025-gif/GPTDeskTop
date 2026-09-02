namespace GPTDeskTop.Models;

public sealed record ChromeProfileInfo(
    string Key,
    string DisplayName,
    string Email,
    string SourceDirectory,
    string ManagedUserDataDirectory)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Email)
        ? DisplayName
        : $"{DisplayName} ({Email})";

    public override string ToString() => DisplayLabel;
}
