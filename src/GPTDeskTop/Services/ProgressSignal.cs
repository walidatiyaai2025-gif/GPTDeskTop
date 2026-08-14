namespace GPTDeskTop.Services;
public sealed record ProgressSignal(bool TextChanged, bool ActivityChanged, bool RepoChanged, bool ReplyCompleted)
{
    public bool HasProgress => TextChanged || ActivityChanged || RepoChanged || ReplyCompleted;
}
