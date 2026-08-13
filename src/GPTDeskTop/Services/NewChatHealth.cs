namespace GPTDeskTop.Services;
public sealed record NewChatHealth(bool PageReady, bool ComposerReady, bool HumanCheckAbsent, bool FatalErrorAbsent)
{
    public bool Healthy => PageReady && ComposerReady && HumanCheckAbsent && FatalErrorAbsent;
}
