using System.Text.Json;
using Conora.Domain.Ports;
using Microsoft.Extensions.Logging;

namespace Conora.Services;

public sealed class SesFeedbackService
{
    private readonly IEmailSuppressionStore _suppressions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SesFeedbackService> _logger;

    public SesFeedbackService(
        IEmailSuppressionStore suppressions,
        IHttpClientFactory httpClientFactory,
        ILogger<SesFeedbackService> logger)
    {
        _suppressions = suppressions;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task HandleSnsPayloadAsync(string body, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var type = root.TryGetProperty("Type", out var typeEl) ? typeEl.GetString() : null;

        if (string.Equals(type, "SubscriptionConfirmation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "UnsubscribeConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("SubscribeURL", out var urlEl)
                && urlEl.GetString() is { Length: > 0 } subscribeUrl
                && subscribeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var client = _httpClientFactory.CreateClient("ses-sns");
                using var response = await client.GetAsync(subscribeUrl, ct);
                _logger.LogInformation("SNS subscription confirmation HTTP {Status}", (int)response.StatusCode);
            }

            return;
        }

        if (!string.Equals(type, "Notification", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("SNS message ignored. Type={Type}", type);
            return;
        }

        var messageJson = root.TryGetProperty("Message", out var msgEl) ? msgEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(messageJson))
            return;

        await ApplySesNotificationAsync(messageJson, ct);
    }

    private async Task ApplySesNotificationAsync(string messageJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;

        var notificationType = root.TryGetProperty("notificationType", out var nt) ? nt.GetString()
            : root.TryGetProperty("eventType", out var et) ? et.GetString()
            : null;

        var reason = notificationType?.ToUpperInvariant() switch
        {
            "BOUNCE" => "Bounce",
            "COMPLAINT" => "Complaint",
            _ => null
        };

        if (reason is null)
        {
            _logger.LogInformation("SES event ignored. Type={Type}", notificationType);
            return;
        }

        var messageId = root.TryGetProperty("mail", out var mail)
            && mail.TryGetProperty("messageId", out var mid)
            ? mid.GetString()
            : null;

        foreach (var email in ExtractRecipients(root, reason))
        {
            await _suppressions.UpsertAsync(email, reason, messageId, ct);
            _logger.LogWarning("E-mail suppressed from SES {Reason}: {Email}", reason, email);
        }
    }

    private static IEnumerable<string> ExtractRecipients(JsonElement root, string reason)
    {
        if (reason == "Bounce"
            && root.TryGetProperty("bounce", out var bounce)
            && bounce.TryGetProperty("bouncedRecipients", out var bounced)
            && bounced.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in bounced.EnumerateArray())
            {
                if (item.TryGetProperty("emailAddress", out var addr) && addr.GetString() is { Length: > 0 } email)
                    yield return email;
            }

            yield break;
        }

        if (reason == "Complaint"
            && root.TryGetProperty("complaint", out var complaint)
            && complaint.TryGetProperty("complainedRecipients", out var complained)
            && complained.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in complained.EnumerateArray())
            {
                if (item.TryGetProperty("emailAddress", out var addr) && addr.GetString() is { Length: > 0 } email)
                    yield return email;
            }

            yield break;
        }

        if (root.TryGetProperty("mail", out var mail)
            && mail.TryGetProperty("destination", out var dest)
            && dest.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dest.EnumerateArray())
            {
                if (item.GetString() is { Length: > 0 } email)
                    yield return email;
            }
        }
    }
}
