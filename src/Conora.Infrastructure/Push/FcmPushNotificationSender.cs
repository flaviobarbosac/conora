using System.Collections.Concurrent;
using Conora.Domain.Ports;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conora.Infrastructure.Push;

/// <summary>Sends device pushes through Firebase Cloud Messaging HTTP v1 when credentials are set.</summary>
public sealed class FcmPushNotificationSender : IPushNotificationSender
{
    private static readonly object AppLock = new();
    private static bool _appReady;

    private readonly FcmOptions _options;
    private readonly ILogger<FcmPushNotificationSender> _logger;

    public FcmPushNotificationSender(IOptions<FcmOptions> options, ILogger<FcmPushNotificationSender> logger)
    {
        _options = options.Value;
        _logger = logger;
        EnsureApp();
    }

    public bool IsConfigured => _options.IsReady;

    public async Task<PushDeliveryResult> SendAsync(
        IReadOnlyList<string> tokens,
        PushMessage message,
        CancellationToken ct)
    {
        var unique = tokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unique.Count == 0)
            return new PushDeliveryResult(0, 0, 0, []);

        if (!IsConfigured || !EnsureApp())
        {
            _logger.LogInformation(
                "FCM not configured; skipped push to {Count} token(s): {Title}",
                unique.Count,
                message.Title);
            return new PushDeliveryResult(unique.Count, 0, unique.Count, []);
        }

        var succeeded = 0;
        var failed = 0;
        var invalid = new ConcurrentBag<string>();

        // FCM multicast supports up to 500 tokens per call.
        foreach (var batch in unique.Chunk(500))
        {
            var multicast = new MulticastMessage
            {
                Tokens = batch.ToList(),
                Notification = new Notification
                {
                    Title = message.Title,
                    Body = message.Body
                },
                Data = message.Data is null
                    ? null
                    : new Dictionary<string, string>(message.Data)
            };

            BatchResponse response;
            try
            {
                response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(multicast, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "FCM multicast failed for {Count} tokens", batch.Length);
                failed += batch.Length;
                continue;
            }

            succeeded += response.SuccessCount;
            failed += response.FailureCount;
            for (var i = 0; i < response.Responses.Count; i++)
            {
                var item = response.Responses[i];
                if (item.IsSuccess)
                    continue;
                if (IsInvalidToken(item.Exception))
                    invalid.Add(batch[i]);
                else
                    _logger.LogWarning(
                        "FCM send failed for a token: {Code} {Message}",
                        item.Exception?.MessagingErrorCode,
                        item.Exception?.Message);
            }
        }

        return new PushDeliveryResult(unique.Count, succeeded, failed, invalid.ToArray());
    }

    private bool EnsureApp()
    {
        if (!IsConfigured)
            return false;
        if (_appReady && FirebaseApp.DefaultInstance is not null)
            return true;

        lock (AppLock)
        {
            if (_appReady && FirebaseApp.DefaultInstance is not null)
                return true;

            try
            {
                GoogleCredential credential;
                if (!string.IsNullOrWhiteSpace(_options.CredentialsJson))
                    credential = GoogleCredential.FromJson(_options.CredentialsJson);
                else
                    credential = GoogleCredential.FromFile(_options.CredentialsPath);

                if (FirebaseApp.DefaultInstance is null)
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = credential,
                        ProjectId = _options.ProjectId
                    });
                }

                _appReady = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FirebaseApp for FCM");
                _appReady = false;
                return false;
            }
        }
    }

    private static bool IsInvalidToken(FirebaseMessagingException? ex)
    {
        if (ex is null)
            return false;
        return ex.MessagingErrorCode is MessagingErrorCode.Unregistered
            or MessagingErrorCode.InvalidArgument
            || string.Equals(ex.ErrorCode.ToString(), "NotFound", StringComparison.OrdinalIgnoreCase);
    }
}
