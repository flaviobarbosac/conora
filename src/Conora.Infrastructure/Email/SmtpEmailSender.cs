using System.Net;
using System.Net.Mail;
using Conora.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Conora.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly IEmailSuppressionStore _suppressions;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IConfiguration configuration,
        IEmailSuppressionStore suppressions,
        ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _suppressions = suppressions;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (await _suppressions.IsSuppressedAsync(to, ct))
        {
            _logger.LogWarning("E-mail bloqueado (supressão SES). To={To} Subject={Subject}", to, subject);
            return;
        }

        var host = _configuration["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation("E-mail não enviado (SMTP ausente). To={To} Subject={Subject}", to, subject);
            return;
        }

        var port = int.TryParse(_configuration["Email:SmtpPort"], out var parsed) ? parsed : 587;
        var from = _configuration["Email:From"] ?? "noreply@localhost";
        var configurationSet = _configuration["Email:ConfigurationSet"];

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = string.Equals(_configuration["Email:UseSsl"], "true", StringComparison.OrdinalIgnoreCase),
            Credentials = string.IsNullOrWhiteSpace(_configuration["Email:User"])
                ? null
                : new NetworkCredential(_configuration["Email:User"], _configuration["Email:Password"])
        };

        using var message = new MailMessage(from, to, subject, body);
        if (!string.IsNullOrWhiteSpace(configurationSet))
            message.Headers.Add("X-SES-CONFIGURATION-SET", configurationSet);

        await client.SendMailAsync(message, ct);
    }
}
