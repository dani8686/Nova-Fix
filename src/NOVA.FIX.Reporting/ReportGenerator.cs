using System.Net;
using System.Text;
using System.Text.Json;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Reporting;

public enum ReportFormat
{
    Txt,
    Html,
    Json,
    Csv,
}

/// <summary>
/// Report Center: generates TXT/HTML/JSON/CSV reports locally. Reports are
/// sanitized (no passwords, keys, tokens, creator email, or full hardware
/// serials) and are never transmitted anywhere.
/// </summary>
public sealed class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Serializes a report in the requested format and writes it locally.</summary>
    public string Generate(ReportPayload payload, ReportFormat format, string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = payload.Metadata.GeneratedAt.ToString("yyyyMMdd-HHmmss");
        var (ext, content) = format switch
        {
            ReportFormat.Txt => ("txt", BuildTxt(payload)),
            ReportFormat.Html => ("html", BuildHtml(payload)),
            ReportFormat.Json => ("json", BuildJson(payload)),
            ReportFormat.Csv => ("csv", BuildCsv(payload)),
            _ => ("txt", BuildTxt(payload)),
        };

        var file = Path.Combine(directory, $"novafix-report-{stamp}.{ext}");
        File.WriteAllText(file, content, new UTF8Encoding(false));
        return file;
    }

    /// <summary>
    /// Sanitization gate: strips secrets and personally sensitive values before
    /// any report content is produced.
    /// </summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Never leak the creator's email into diagnostic reports.
        text = text.Replace("daniel.865559@gmail.com", "[attribution-redacted]");

        // Strip common secret patterns.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"(?i)(password|pwd|secret|token|api[_-]?key)\s*[=:]\s*\S+",
            "$1=[redacted]");
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"\b[A-Za-z0-9+/]{40,}={0,2}\b", "[possible-key-redacted]");

        // Hardware serial numbers: mask to last 4 characters.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"(?i)\b(serial|sn)\s*[:=]\s*([A-Za-z0-9]{6,})\b",
            m => $"{m.Groups[1].Value}={Mask(m.Groups[2].Value)}");

        return text;
    }

    private static string Mask(string s)
        => s.Length <= 4 ? "****" : "****" + s[^4..];

    private static string BuildTxt(ReportPayload p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=========================================================");
        sb.AppendLine("  NOVA FIX — System Report");
        sb.AppendLine("=========================================================");
        sb.AppendLine($"Generated : {p.Metadata.GeneratedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Product   : {p.Metadata.ProductVersion}");
        sb.AppendLine($"AI model  : {p.Metadata.AiModelVersion}");
        sb.AppendLine($"Knowledge : {p.Metadata.KnowledgeDbVersion}");
        sb.AppendLine($"Rules     : {p.Metadata.RulesDbVersion}");
        sb.AppendLine($"Machine   : {p.Metadata.MachineAlias}");
        sb.AppendLine();

        if (p.Windows is not null)
        {
            sb.AppendLine("--- WINDOWS ---");
            sb.AppendLine($"  {p.Windows.ProductName} {p.Windows.Edition}");
            sb.AppendLine($"  Version {p.Windows.Version} build {p.Windows.Build} " +
                          $"({p.Windows.Architecture})");
            sb.AppendLine($"  Activation: {p.Windows.ActivationStatus}");
            sb.AppendLine();
        }

        if (p.Hardware is not null)
        {
            var h = p.Hardware;
            sb.AppendLine("--- HARDWARE ---");
            sb.AppendLine($"  CPU   : {h.Cpu.Name} ({h.Cpu.PhysicalCores}C/" +
                          $"{h.Cpu.LogicalProcessors}T)");
            sb.AppendLine($"  RAM   : {h.Memory.TotalBytes / (1024 * 1024 * 1024)} GB");
            sb.AppendLine($"  Board : {h.Motherboard.Manufacturer} {h.Motherboard.Product}");
            sb.AppendLine($"  BIOS  : {h.Bios.Manufacturer} {h.Bios.Version}");
            foreach (var g in h.Gpus)
                sb.AppendLine($"  GPU   : {g.Name} (driver {g.DriverVersion})");
            foreach (var d in h.Drives)
                sb.AppendLine($"  Disk  : {d.Model} {d.SizeBytes / (1024 * 1024 * 1024)} GB " +
                              $"({d.InterfaceType})");
            if (h.ProbeFailures.Count > 0)
            {
                sb.AppendLine("  Probe issues:");
                foreach (var f in h.ProbeFailures)
                    sb.AppendLine($"    ! {f}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"--- FINDINGS ({p.Findings.Count}) ---");
        foreach (var f in p.Findings.OrderByDescending(x => x.Severity))
        {
            sb.AppendLine($"  [{f.Severity}] {f.Title}");
            sb.AppendLine($"      Component : {f.AffectedComponent}");
            sb.AppendLine($"      Confidence: {f.Confidence}" +
                          (f.IsAiInterpreted ? " (AI-interpreted)" : " (rule-based)"));
            sb.AppendLine($"      What to do: {f.RecommendedSolution}");
            sb.AppendLine($"      Verify    : {f.VerificationMethod}");
            sb.AppendLine();
        }

        sb.AppendLine($"--- ACTIONS PERFORMED ({p.ActionsPerformed.Count}) ---");
        foreach (var a in p.ActionsPerformed)
        {
            sb.AppendLine($"  {a.ActionId}: {a.Status} (exit {a.ExitCode})");
            if (a.VerificationPassed is not null)
                sb.AppendLine($"      verification: {a.VerificationPassed} " +
                              $"— {a.VerificationDetail}");
        }
        sb.AppendLine();

        sb.AppendLine($"--- REMAINING ISSUES ({p.RemainingIssues.Count}) ---");
        foreach (var r in p.RemainingIssues)
            sb.AppendLine($"  • {r}");

        sb.AppendLine();
        sb.AppendLine("Report generated locally. Nothing was transmitted.");
        return Sanitize(sb.ToString());
    }

    private static string BuildHtml(ReportPayload p)
    {
        Func<string?, string> e = WebUtility.HtmlEncode;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>NOVA FIX Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            body{font-family:'Segoe UI',sans-serif;background:#0B0F14;color:#F1F5F9;
                 margin:0;padding:32px;}
            .card{background:#151E29;border:1px solid #1C2734;border-radius:12px;
                  padding:20px;margin:16px 0;}
            h1{color:#35C9F0;} h2{color:#7C6CFF;font-size:16px;}
            .sev-Critical,.sev-High{color:#F16D76;} .sev-Medium{color:#F5B942;}
            .sev-Low,.sev-Informational{color:#31C48D;}
            .muted{color:#94A3B8;font-size:13px;}
            table{width:100%;border-collapse:collapse;}
            td,th{padding:6px 10px;border-bottom:1px solid #1C2734;text-align:left;}
            code{background:#1C2734;padding:2px 6px;border-radius:4px;}
            """);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>NOVA FIX — System Report</h1>");
        sb.AppendLine($"<p class=\"muted\">Generated {e(p.Metadata.GeneratedAt.ToString("u"))} · " +
                      $"product {e(p.Metadata.ProductVersion)} · AI model " +
                      $"{e(p.Metadata.AiModelVersion)} · knowledge " +
                      $"{e(p.Metadata.KnowledgeDbVersion)} · rules " +
                      $"{e(p.Metadata.RulesDbVersion)}</p>");

        sb.AppendLine("<div class=\"card\"><h2>FINDINGS</h2><table>");
        sb.AppendLine("<tr><th>Severity</th><th>Title</th><th>Component</th>" +
                      "<th>Confidence</th></tr>");
        foreach (var f in p.Findings.OrderByDescending(x => x.Severity))
        {
            sb.AppendLine(
                $"<tr><td class=\"sev-{f.Severity}\">{f.Severity}</td>" +
                $"<td><strong>{e(f.Title)}</strong><div class=\"muted\">" +
                $"{e(f.Description)}</div></td>" +
                $"<td>{e(f.AffectedComponent)}</td><td>{f.Confidence}</td></tr>");
        }
        sb.AppendLine("</table></div>");

        if (p.ActionsPerformed.Count > 0)
        {
            sb.AppendLine("<div class=\"card\"><h2>ACTIONS PERFORMED</h2><table>");
            sb.AppendLine("<tr><th>Action</th><th>Status</th><th>Exit</th><th>Verified</th></tr>");
            foreach (var a in p.ActionsPerformed)
            {
                sb.AppendLine(
                    $"<tr><td><code>{e(a.ActionId)}</code></td><td>{a.Status}</td>" +
                    $"<td>{a.ExitCode}</td><td>{a.VerificationPassed?.ToString() ?? "—"}</td></tr>");
            }
            sb.AppendLine("</table></div>");
        }

        sb.AppendLine("<div class=\"card\"><h2>REMAINING ISSUES</h2><ul>");
        foreach (var r in p.RemainingIssues)
            sb.AppendLine($"<li>{e(r)}</li>");
        sb.AppendLine("</ul></div>");
        sb.AppendLine("<p class=\"muted\">Generated locally by NOVA FIX — not transmitted.</p>");
        sb.AppendLine("</body></html>");

        return Sanitize(sb.ToString());
    }

    private static string BuildJson(ReportPayload p)
    {
        var json = JsonSerializer.Serialize(p, JsonOptions);
        return Sanitize(json);
    }

    private static string BuildCsv(ReportPayload p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Severity,Title,Component,Confidence,Basis,Recommendation");
        foreach (var f in p.Findings.OrderByDescending(x => x.Severity))
        {
            sb.AppendLine(string.Join(',',
                Csv(f.Severity.ToString()),
                Csv(f.Title),
                Csv(f.AffectedComponent),
                Csv(f.Confidence.ToString()),
                Csv(f.IsAiInterpreted ? "AI-interpreted" : "rule-based"),
                Csv(f.RecommendedSolution)));
        }
        return Sanitize(sb.ToString());

        static string Csv(string s)
        {
            s = s.Replace("\"", "\"\"");
            return s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s}\""
                : s;
        }
    }
}
