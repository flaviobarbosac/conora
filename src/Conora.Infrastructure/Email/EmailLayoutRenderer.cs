using System.Net;
using System.Text;
using Conora.Domain.Ports;

namespace Conora.Infrastructure.Email;

/// <summary>
/// Renders <see cref="EmailContent"/> into the Conora "Noite" layout (dark brand header, white body,
/// dark footer). Table-based markup with inline styles for e-mail client compatibility.
/// </summary>
public sealed class EmailLayoutRenderer
{
    private const string FontStack = "'Be Vietnam Pro', Arial, Helvetica, sans-serif";

    private readonly string _assetBaseUrl;
    private readonly string _brandName;

    public EmailLayoutRenderer(string assetBaseUrl, string brandName)
    {
        _assetBaseUrl = assetBaseUrl.TrimEnd('/');
        _brandName = brandName;
    }

    public string RenderHtml(EmailContent content)
    {
        var sb = new StringBuilder(8192);
        var year = DateTime.UtcNow.Year;

        sb.Append($"""
            <!DOCTYPE html>
            <html lang="pt-BR" xmlns="http://www.w3.org/1999/xhtml">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="x-apple-disable-message-reformatting">
            <meta name="color-scheme" content="light">
            <title>{E(content.Title)}</title>
            <link href="https://fonts.googleapis.com/css2?family=Be+Vietnam+Pro:wght@400;600;700&display=swap" rel="stylesheet">
            """);
        sb.Append("""
            <style>
              body { margin:0; padding:0; }
              @media only screen and (max-width:620px) {
                .container { width:100% !important; }
                .px { padding-left:24px !important; padding-right:24px !important; }
                .h1 { font-size:26px !important; line-height:34px !important; }
              }
            </style>
            </head>
            <body style="margin:0; padding:0; background-color:#e9ecef;">
            """);

        if (!string.IsNullOrWhiteSpace(content.Preheader))
            sb.Append($"""<div style="display:none; max-height:0; overflow:hidden; mso-hide:all;">{E(content.Preheader)}&#8199;&#847;&#8199;&#847;&#8199;&#847;</div>""");

        sb.Append($"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#e9ecef;">
            <tr><td align="center" style="padding:32px 12px;">
            <table role="presentation" class="container" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px; max-width:600px; background-color:#ffffff; border-radius:16px; overflow:hidden;">
            <tr><td bgcolor="#0e1826" class="px" style="background-color:#0e1826; padding:36px 48px 44px 48px; font-family:{FontStack};">
            <img src="{E(_assetBaseUrl)}/conora-escuro.png" width="168" alt="{E(_brandName)}" style="display:block; width:168px; height:auto; border:0; margin:0 0 40px 0;">
            <h1 class="h1" style="margin:0 0 12px 0; font-size:30px; line-height:38px; font-weight:700; color:#ffffff;">{E(content.Title)}</h1>
            """);

        if (!string.IsNullOrWhiteSpace(content.Subtitle))
            sb.Append($"""<p style="margin:0; font-size:17px; line-height:27px; color:#a9b6c7;">{E(content.Subtitle)}</p>""");

        sb.Append("""
            </td></tr>
            <tr><td style="height:4px; line-height:4px; font-size:0; background-color:#6aacff;">&nbsp;</td></tr>
            """);

        sb.Append($"""<tr><td class="px" style="padding:40px 48px 8px 48px; font-family:{FontStack};">""");
        foreach (var paragraph in content.Paragraphs)
            sb.Append($"""<p style="margin:0 0 20px 0; font-size:16px; line-height:26px; color:#4a5568;">{E(paragraph)}</p>""");

        if (content.Steps.Count > 0)
        {
            sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">""");
            for (var i = 0; i < content.Steps.Count; i++)
            {
                var step = content.Steps[i];
                sb.Append($"""
                    <tr>
                    <td width="44" valign="top" style="padding:0 0 20px 0;"><div style="width:32px; height:32px; line-height:32px; border-radius:16px; background-color:#e3eefc; color:#375984; font-size:14px; font-weight:700; text-align:center;">{i + 1}</div></td>
                    <td valign="top" style="padding:4px 0 20px 0; font-size:15px; line-height:23px; color:#4a5568;"><strong style="color:#172638;">{E(step.Title)}</strong><br>{E(step.Description)}</td>
                    </tr>
                    """);
            }
            sb.Append("</table>");
        }
        sb.Append("</td></tr>");

        if (!string.IsNullOrWhiteSpace(content.ActionLabel) && !string.IsNullOrWhiteSpace(content.ActionUrl))
        {
            sb.Append($"""
                <tr><td class="px" align="left" style="padding:12px 48px 32px 48px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                <td align="center" bgcolor="#6aacff" style="border-radius:999px;">
                <a href="{E(content.ActionUrl)}" target="_blank" style="display:inline-block; padding:14px 34px; font-family:{FontStack}; font-size:16px; font-weight:700; color:#0e1826; text-decoration:none; border-radius:999px;">{E(content.ActionLabel)} &rarr;</a>
                </td></tr></table>
                </td></tr>
                """);
        }

        if (!string.IsNullOrWhiteSpace(content.Note) || !string.IsNullOrWhiteSpace(content.ActionUrl))
        {
            sb.Append($"""
                <tr><td class="px" style="padding:0 48px 40px 48px; font-family:{FontStack};">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#e3eefc; border-radius:10px;"><tr>
                <td style="padding:16px 20px; font-size:14px; line-height:22px; color:#375984;">
                """);
            if (!string.IsNullOrWhiteSpace(content.Note))
                sb.Append($"{E(content.Note)}<br>");
            if (!string.IsNullOrWhiteSpace(content.ActionUrl))
                sb.Append($"""Se o botão não funcionar, copie e cole este link no navegador:<br><a href="{E(content.ActionUrl)}" style="color:#375984; word-break:break-all;">{E(content.ActionUrl)}</a>""");
            sb.Append("</td></tr></table></td></tr>");
        }
        else
        {
            sb.Append("""<tr><td style="height:20px; line-height:20px; font-size:0;">&nbsp;</td></tr>""");
        }

        sb.Append($"""
            <tr><td bgcolor="#172638" class="px" style="background-color:#172638; padding:28px 48px; font-family:{FontStack};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
            <td valign="middle" width="40"><img src="{E(_assetBaseUrl)}/conora-simbolo.png" width="28" alt="" style="display:block; width:28px; height:auto; border:0;"></td>
            <td valign="middle" style="font-size:13px; line-height:20px; color:#a9b6c7;">E-mail automático do {E(_brandName)} — não é preciso responder.<br>&copy; {year} {E(_brandName)} &middot; uma solução <strong style="color:#eef0f2; letter-spacing:1px;">ONRA</strong></td>
            </tr></table>
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    public string RenderText(EmailContent content)
    {
        var sb = new StringBuilder();
        sb.AppendLine(content.Title);
        if (!string.IsNullOrWhiteSpace(content.Subtitle))
            sb.AppendLine(content.Subtitle);
        sb.AppendLine();

        foreach (var paragraph in content.Paragraphs)
        {
            sb.AppendLine(paragraph);
            sb.AppendLine();
        }

        for (var i = 0; i < content.Steps.Count; i++)
            sb.AppendLine($"{i + 1}. {content.Steps[i].Title} — {content.Steps[i].Description}");
        if (content.Steps.Count > 0)
            sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(content.ActionUrl))
        {
            sb.AppendLine($"{content.ActionLabel ?? "Acessar"}: {content.ActionUrl}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(content.Note))
        {
            sb.AppendLine(content.Note);
            sb.AppendLine();
        }

        sb.AppendLine($"— {_brandName} · uma solução ONRA");
        return sb.ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
