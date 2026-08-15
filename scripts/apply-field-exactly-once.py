from pathlib import Path

path = Path('src/GPTDeskTop/Services/ChatGptMonitorService.cs')
text = path.read_text(encoding='utf-8')

if 'using GPTDeskTop.Runtime;' not in text:
    text = text.replace('using GPTDeskTop.Models;\n', 'using GPTDeskTop.Models;\nusing GPTDeskTop.Runtime;\n', 1)

field_anchor = '    private readonly ModelRoutingService _modelRouting = new();\n'
field_line = '    private readonly OutboundDeliveryCoordinator _outboundDelivery = new();\n'
if field_line not in text:
    if field_anchor not in text:
        raise SystemExit('FIELD patch failed: ModelRoutingService anchor not found')
    text = text.replace(field_anchor, field_anchor + field_line, 1)

start = text.find('    private async Task<bool> SendWhenReadyAsync(long monitorId, ChromeTab tab, string message, bool allowRecoveryReload, CancellationToken cancellationToken)')
if start < 0:
    raise SystemExit('FIELD patch failed: SendWhenReadyAsync not found')
end = text.find('    private async Task ApplyModelRouteAsync(', start)
if end < 0:
    raise SystemExit('FIELD patch failed: ApplyModelRouteAsync boundary not found')

replacement = '''    private async Task<bool> SendWhenReadyAsync(long monitorId, ChromeTab tab, string message, bool allowRecoveryReload, CancellationToken cancellationToken)\n    {\n        // Exactly-once field policy: a missing receipt is an uncertain delivery, not permission\n        // to click Send again. The previous implementation repeatedly invoked the physical\n        // composer send for up to 95 seconds, which could duplicate a message when ChatGPT\n        // accepted it but the receipt detector lagged. One logical outbound operation now\n        // performs at most one physical composer mutation.\n        try\n        {\n            var accepted = await _outboundDelivery.SendOnceAsync(\n                monitorId,\n                tab.Id,\n                message,\n                () => _chrome.SendChatMessageVerifiedAsync(tab, message, cancellationToken),\n                detail => Activity?.Invoke(monitorId, detail),\n                cancellationToken);\n\n            if (accepted)\n            {\n                Activity?.Invoke(monitorId, "Verified message accepted. Exactly-once guard closed the delivery operation.");\n                return true;\n            }\n\n            Activity?.Invoke(monitorId,\n                "Composer delivery was not confirmed. Exactly-once guard suppressed blind resend; monitoring will reconcile from observed ChatGPT state.");\n            return false;\n        }\n        catch (Exception ex) when (IsTransientChromeException(ex))\n        {\n            Activity?.Invoke(monitorId,\n                $"Physical composer send became uncertain ({ex.GetType().Name}). Exactly-once guard suppressed automatic resend.");\n            return false;\n        }\n    }\n\n'''
text = text[:start] + replacement + text[end:]
path.write_text(text, encoding='utf-8')
print('FIELD exactly-once production integration applied')
