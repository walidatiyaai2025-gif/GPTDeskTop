namespace GPTDeskTop.Runtime;

public enum ChatGptRuntimeState
{
    Ready, Generating, ResponseComplete, PageLoading, EditorNotReady, ComposerDisabled,
    SendInProgress, SendUncertain, MessageDeliveryTimeout, SomethingWentWrong, NetworkError,
    Offline, ChatGptServiceUnavailable, TooManyRequests, UsageLimit, PlanLimit, LoginRequired,
    SessionExpired, SecurityChallenge, ContextLimit, ConversationNotFound, ModelUnavailable,
    ModelSwitching, TabClosed, CdpDisconnected, WrongChat, ChatChanged, AccountBlocked,
    UnknownBlockingUi
}

public enum RuntimeStateScope { Monitor, Global }
public enum RuntimeSendPolicy { Allow, Wait, Block }
public enum RuntimeRecoveryPolicy { None, Observe, ReloadSameConversation, Reauthenticate, HumanAction }

public sealed record ChatGptRuntimeEvidence(
    bool PageLoading = false, bool EditorReady = true, bool ComposerEnabled = true,
    bool IsGenerating = false, bool ResponseCompleted = false, bool SendInProgress = false,
    bool SendUncertain = false, bool CurrentTurnDeliveryTimedOut = false,
    string CurrentBlockingText = "", bool BrowserOffline = false, bool TabClosed = false,
    bool CdpConnected = true, bool WrongChat = false, bool ChatChanged = false,
    bool UnknownBlockingUi = false);

public sealed record ChatGptRuntimeDecision(
    ChatGptRuntimeState State, RuntimeStateScope Scope, RuntimeSendPolicy SendPolicy,
    RuntimeRecoveryPolicy RecoveryPolicy, string RetryPolicy, string ResumeCondition, string Evidence);

public static class ChatGptRuntimeStateEngine
{
    public static ChatGptRuntimeDecision PolicyForState(ChatGptRuntimeState state)
    {
        var global = state is ChatGptRuntimeState.Offline or ChatGptRuntimeState.ChatGptServiceUnavailable
            or ChatGptRuntimeState.TooManyRequests or ChatGptRuntimeState.UsageLimit or ChatGptRuntimeState.PlanLimit
            or ChatGptRuntimeState.LoginRequired or ChatGptRuntimeState.SessionExpired or ChatGptRuntimeState.SecurityChallenge
            or ChatGptRuntimeState.AccountBlocked or ChatGptRuntimeState.NetworkError;
        var allow = state is ChatGptRuntimeState.Ready or ChatGptRuntimeState.ResponseComplete;
        var wait = state is ChatGptRuntimeState.Generating or ChatGptRuntimeState.PageLoading
            or ChatGptRuntimeState.EditorNotReady or ChatGptRuntimeState.ComposerDisabled
            or ChatGptRuntimeState.SendInProgress or ChatGptRuntimeState.ModelSwitching;
        var human = state is ChatGptRuntimeState.LoginRequired or ChatGptRuntimeState.SessionExpired
            or ChatGptRuntimeState.SecurityChallenge or ChatGptRuntimeState.WrongChat or ChatGptRuntimeState.ChatChanged
            or ChatGptRuntimeState.ConversationNotFound or ChatGptRuntimeState.AccountBlocked or ChatGptRuntimeState.UnknownBlockingUi;
        var reload = state is ChatGptRuntimeState.MessageDeliveryTimeout or ChatGptRuntimeState.SomethingWentWrong
            or ChatGptRuntimeState.ContextLimit or ChatGptRuntimeState.TabClosed or ChatGptRuntimeState.CdpDisconnected;
        return new(state, global ? RuntimeStateScope.Global : RuntimeStateScope.Monitor,
            allow ? RuntimeSendPolicy.Allow : wait ? RuntimeSendPolicy.Wait : RuntimeSendPolicy.Block,
            allow ? RuntimeRecoveryPolicy.None : human ? RuntimeRecoveryPolicy.HumanAction : reload ? RuntimeRecoveryPolicy.ReloadSameConversation : RuntimeRecoveryPolicy.Observe,
            global ? "global circuit breaker/backoff" : "bounded monitor retry policy",
            allow ? "already safe" : "fresh structured evidence satisfies state policy",
            "canonical policy matrix");
    }

    public static ChatGptRuntimeDecision Classify(ChatGptRuntimeEvidence e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.WrongChat) return Decision(ChatGptRuntimeState.WrongChat, RuntimeRecoveryPolicy.HumanAction, "conversation identity must match");
        if (e.ChatChanged) return Decision(ChatGptRuntimeState.ChatChanged, RuntimeRecoveryPolicy.HumanAction, "owned chat identity must be restored");
        if (e.TabClosed) return Decision(ChatGptRuntimeState.TabClosed, RuntimeRecoveryPolicy.ReloadSameConversation, "owned tab must be reacquired");
        if (!e.CdpConnected) return Decision(ChatGptRuntimeState.CdpDisconnected, RuntimeRecoveryPolicy.ReloadSameConversation, "CDP connection must recover");
        if (e.BrowserOffline) return Decision(ChatGptRuntimeState.Offline, RuntimeRecoveryPolicy.Observe, "network connectivity must recover", RuntimeStateScope.Global);
        if (e.UnknownBlockingUi) return Decision(ChatGptRuntimeState.UnknownBlockingUi, RuntimeRecoveryPolicy.HumanAction, "blocking UI must be classified or dismissed");
        var text = Normalize(e.CurrentBlockingText);
        foreach (var marker in Markers)
            if (marker.Terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                return Decision(marker.State, marker.Recovery, marker.Resume, marker.Scope);
        if (e.PageLoading) return Decision(ChatGptRuntimeState.PageLoading, RuntimeRecoveryPolicy.Observe, "page load must settle", send: RuntimeSendPolicy.Wait);
        if (e.SendInProgress) return Decision(ChatGptRuntimeState.SendInProgress, RuntimeRecoveryPolicy.Observe, "verified send must settle", send: RuntimeSendPolicy.Wait);
        if (e.SendUncertain) return Decision(ChatGptRuntimeState.SendUncertain, RuntimeRecoveryPolicy.Observe, "delivery must reconcile before retry");
        if (e.CurrentTurnDeliveryTimedOut) return Decision(ChatGptRuntimeState.MessageDeliveryTimeout, RuntimeRecoveryPolicy.ReloadSameConversation, "bounded same-monitor recovery must complete");
        if (e.IsGenerating) return Decision(ChatGptRuntimeState.Generating, RuntimeRecoveryPolicy.Observe, "generation must complete", send: RuntimeSendPolicy.Wait);
        if (!e.EditorReady) return Decision(ChatGptRuntimeState.EditorNotReady, RuntimeRecoveryPolicy.Observe, "editor must become ready", send: RuntimeSendPolicy.Wait);
        if (!e.ComposerEnabled) return Decision(ChatGptRuntimeState.ComposerDisabled, RuntimeRecoveryPolicy.Observe, "composer must become enabled", send: RuntimeSendPolicy.Wait);
        if (e.ResponseCompleted) return new(ChatGptRuntimeState.ResponseComplete, RuntimeStateScope.Monitor, RuntimeSendPolicy.Allow, RuntimeRecoveryPolicy.None, "none", "verified next action", "current structured state");
        return new(ChatGptRuntimeState.Ready, RuntimeStateScope.Monitor, RuntimeSendPolicy.Allow, RuntimeRecoveryPolicy.None, "none", "already ready", "current structured state");
    }

    private static ChatGptRuntimeDecision Decision(ChatGptRuntimeState state, RuntimeRecoveryPolicy recovery, string resume,
        RuntimeStateScope scope = RuntimeStateScope.Monitor, RuntimeSendPolicy send = RuntimeSendPolicy.Block)
        => new(state, scope, send, recovery, scope == RuntimeStateScope.Global ? "global circuit breaker" : "bounded monitor policy", resume, "current visible/structured evidence only");

    private static string Normalize(string text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly (ChatGptRuntimeState State, RuntimeStateScope Scope, RuntimeRecoveryPolicy Recovery, string Resume, string[] Terms)[] Markers =
    [
        (ChatGptRuntimeState.TooManyRequests, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Observe, "one global probe confirms clear", ["too many requests", "making requests too quickly", "temporarily limited access", "please wait a few minutes before trying again"]),
        (ChatGptRuntimeState.LoginRequired, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Reauthenticate, "authenticated session", ["log in to chatgpt", "sign in to chatgpt"]),
        (ChatGptRuntimeState.SessionExpired, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Reauthenticate, "session renewed", ["session has expired"]),
        (ChatGptRuntimeState.SecurityChallenge, RuntimeStateScope.Global, RuntimeRecoveryPolicy.HumanAction, "challenge completed", ["verify you are human", "security check"]),
        (ChatGptRuntimeState.UsageLimit, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Observe, "usage limit reset", ["usage limit"]),
        (ChatGptRuntimeState.PlanLimit, RuntimeStateScope.Global, RuntimeRecoveryPolicy.HumanAction, "plan permits usage", ["upgrade your plan", "plan limit"]),
        (ChatGptRuntimeState.ChatGptServiceUnavailable, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Observe, "service becomes available", ["service unavailable", "chatgpt is currently unavailable"]),
        (ChatGptRuntimeState.NetworkError, RuntimeStateScope.Global, RuntimeRecoveryPolicy.Observe, "network request succeeds", ["network error"]),
        (ChatGptRuntimeState.AccountBlocked, RuntimeStateScope.Global, RuntimeRecoveryPolicy.HumanAction, "account access restored", ["account has been deactivated", "account blocked"]),
        (ChatGptRuntimeState.ContextLimit, RuntimeStateScope.Monitor, RuntimeRecoveryPolicy.ReloadSameConversation, "verified rollover", ["maximum context length", "conversation is too long"]),
        (ChatGptRuntimeState.ConversationNotFound, RuntimeStateScope.Monitor, RuntimeRecoveryPolicy.HumanAction, "owned conversation found", ["conversation not found"]),
        (ChatGptRuntimeState.ModelUnavailable, RuntimeStateScope.Monitor, RuntimeRecoveryPolicy.Observe, "configured model available", ["model is unavailable"]),
        (ChatGptRuntimeState.ModelSwitching, RuntimeStateScope.Monitor, RuntimeRecoveryPolicy.Observe, "model switch settles", ["switching models"]),
        (ChatGptRuntimeState.SomethingWentWrong, RuntimeStateScope.Monitor, RuntimeRecoveryPolicy.ReloadSameConversation, "current error clears", ["something went wrong"])
    ];
}
