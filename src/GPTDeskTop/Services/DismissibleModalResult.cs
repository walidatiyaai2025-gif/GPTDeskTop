namespace GPTDeskTop.Services;

public enum DismissibleModalResult
{
    NotPresent,
    Dismissed,
    StillVisible,
    HumanVerificationDetected,
    UnknownModalIgnored
}
