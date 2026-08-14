namespace GPTDeskTop.Services;

public sealed record ModalDismissVerification(bool ModalGone, bool ComposerAvailable, bool ComposerEditable)
{
    public bool CanResume => ModalGone && ComposerAvailable && ComposerEditable;
}
