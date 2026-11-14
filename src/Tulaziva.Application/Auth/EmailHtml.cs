using System.Net;
using System.Text;

namespace Tulaziva.Application.Auth;

/// <summary>
/// Turns the plain text of an email into a simple HTML version: the same paragraphs, and a button for the link. Email clients that cannot
/// show HTML (or people who read the text part) get the original text, so both parts always say the same.
/// </summary>
internal static class EmailHtml
{
    private const string Blue = "#3D7FCB";
    private const string Ink = "#16324F";
    private const string Muted = "#5B7089";

    public static string FromText(string text, string buttonLabel, string brand)
    {
        var body = new StringBuilder();

        foreach (var raw in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var paragraph = raw.Trim();

            if (paragraph.Length == 0)
                continue;

            if (IsLink(paragraph))
            {
                var href = WebUtility.HtmlEncode(paragraph);
                body.Append($"<p style=\"margin:24px 0;text-align:center\"><a href=\"{href}\" style=\"background:{Blue};color:#ffffff;text-decoration:none;font-weight:600;padding:13px 28px;border-radius:10px;display:inline-block\">{WebUtility.HtmlEncode(buttonLabel)}</a></p>");
                body.Append($"<p style=\"margin:0 0 16px;font-size:12px;color:{Muted};word-break:break-all\"><a href=\"{href}\" style=\"color:{Muted}\">{href}</a></p>");
            }
            else
            {
                // A paragraph that is only the signature is shown quieter.
                var style = paragraph == brand ? $"margin:24px 0 0;font-weight:700;color:{Blue}" : "margin:0 0 16px";
                body.Append($"<p style=\"{style}\">{WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>")}</p>");
            }
        }

        return $"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:24px 12px;background:#F4F8FC;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};font-size:15px;line-height:1.55">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden">
            <tr><td style="background:{Blue};padding:18px 28px;color:#ffffff;font-size:20px;font-weight:700">{WebUtility.HtmlEncode(brand)}</td></tr>
            <tr><td style="padding:28px">{body}</td></tr>
            </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static bool IsLink(string paragraph) =>
        (paragraph.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || paragraph.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        && !paragraph.Any(char.IsWhiteSpace);
}
