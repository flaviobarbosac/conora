using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
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

    public async Task SendAsync(string to, string subject, EmailContent content, CancellationToken ct = default)
    {
        if (await _suppressions.IsSuppressedAsync(to, ct))
        {
            _logger.LogWarning("E-mail bloqueado (supressão/opt-out). To={To} Subject={Subject}", to, subject);
            throw new InvalidOperationException($"E-mail bloqueado por supressão/opt-out: {to}");
        }

        var host = _configuration["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("E-mail não enviado (SMTP ausente). To={To} Subject={Subject}", to, subject);
            throw new InvalidOperationException("SMTP não configurado; e-mail não foi enviado.");
        }

        var port = int.TryParse(_configuration["Email:SmtpPort"], out var parsed) ? parsed : 587;
        var from = ResolveFrom(_configuration["Email:From"]);
        var configurationSet = _configuration["Email:ConfigurationSet"];

        // Fail fast when the SMTP host is down (common in local dev) so auth screens do not hang on "Criando…".
        var timeoutMs = int.TryParse(_configuration["Email:SmtpTimeoutMs"], out var timeout) ? timeout : 5_000;
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = string.Equals(_configuration["Email:UseSsl"], "true", StringComparison.OrdinalIgnoreCase),
            Credentials = string.IsNullOrWhiteSpace(_configuration["Email:User"])
                ? null
                : new NetworkCredential(_configuration["Email:User"], _configuration["Email:Password"]),
            Timeout = Math.Clamp(timeoutMs, 1_000, 60_000)
        };

        var renderer = CreateRenderer();
        using var message = new MailMessage
        {
            From = from,
            Sender = from,
            Subject = subject,
            Body = renderer.RenderText(content),
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        message.To.Add(to);
        message.ReplyToList.Add(from);
        message.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(renderer.RenderHtml(content), Encoding.UTF8, "text/html"));
        if (!string.IsNullOrWhiteSpace(configurationSet))
            message.Headers.Add("X-SES-CONFIGURATION-SET", configurationSet);

        foreach (var file in content.Attachments)
        {
            var stream = new MemoryStream(file.Content);
            message.Attachments.Add(new Attachment(stream, file.FileName, file.ContentType));
        }

        await client.SendMailAsync(message, ct);
    }

    private static MailAddress ResolveFrom(string? configured)
    {
        const string noreply = "noreply@conora.com.br";
        var address = string.IsNullOrWhiteSpace(configured) ? noreply : configured.Trim();
        if (address.EndsWith("@localhost", StringComparison.OrdinalIgnoreCase))
            address = noreply;

        return new MailAddress(address, "Conora");
    }

    private EmailLayoutRenderer CreateRenderer()
    {
        // Images must be absolute HTTPS URLs; default to the web app's public brand folder.
        var assetBase = _configuration["Email:AssetBaseUrl"];
        if (string.IsNullOrWhiteSpace(assetBase))
        {
            var web = (_configuration["App:PublicWebUrl"] ?? "https://conora.com.br").TrimEnd('/');
            assetBase = $"{web}/app/brand";
        }

        var brandName = _configuration["Email:BrandName"];
        return new EmailLayoutRenderer(assetBase, string.IsNullOrWhiteSpace(brandName) ? "Conora" : brandName);
    }
}
