using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Conservative model routing policy for ChatGPT Web automation.
/// This service never attempts to bypass usage limits. It only decides which configured
/// model label should be preferred for a new conversation and when to fall back.
/// </summary>
public sealed class ModelRoutingService
{
    public ModelRoutingDecision Choose(SavedMonitor monitor, bool recovery, bool contextRotation)
    {
        var preferred = string.IsNullOrWhiteSpace(monitor.PreferredModel) ? "Auto" : monitor.PreferredModel.Trim();
        var alternate = string.IsNullOrWhiteSpace(monitor.FallbackModel) ? preferred : monitor.FallbackModel.Trim();

        // Keep normal continuation on the preferred model. Rotation/recovery can use the
        // configured alternate model, but only if it is explicitly different.
        if ((contextRotation || recovery) && !string.Equals(preferred, alternate, StringComparison.OrdinalIgnoreCase))
            return new ModelRoutingDecision(alternate, preferred, "Recovery/rotation fallback");

        return new ModelRoutingDecision(preferred, alternate, "Normal monitor route");
    }
}

public sealed record ModelRoutingDecision(string PreferredModel, string FallbackModel, string Reason);
