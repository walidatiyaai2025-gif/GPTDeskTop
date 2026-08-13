namespace GPTDeskTop.Services;
public static class ExternalWaitResumeGate
{
    public static bool CanResume(ExternalWaitStatus status, bool resumeConditionSatisfied, bool projectStateValid) => status == ExternalWaitStatus.Satisfied && resumeConditionSatisfied && projectStateValid;
}
