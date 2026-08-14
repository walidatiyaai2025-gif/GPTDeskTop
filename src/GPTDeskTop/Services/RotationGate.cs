namespace GPTDeskTop.Services;
public static class RotationGate
{
    public static bool CanRotate(bool checkpointReady, bool stateHealthy, bool waitingForHuman, bool approvalRequired) => checkpointReady && stateHealthy && !waitingForHuman && !approvalRequired;
}
