using System.Net;
using System.Text;
using System.Text.Json;
namespace LocalDeviceToolkit.Engine.Reporting;
/// <summary>Static exports: no scripts, links, external resources or overwrite by default.</summary>
public static class ReportExporter
{
 public static string CsvCell(string? value)
 {
  value ??= "";
  var significant = value.SkipWhile(c => char.IsWhiteSpace(c) || char.IsControl(c)).FirstOrDefault();
  if (significant is '=' or '+' or '-' or '@') value = "'" + value;
  return "\"" + value.Replace("\"", "\"\"") + "\"";
 }
 public static string Render(LocalReport report, ReportFormat format) => format switch
 {
  ReportFormat.Json => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
  ReportFormat.Csv => Csv(report), ReportFormat.Html => Html(report), _ => throw new ArgumentOutOfRangeException(nameof(format))
 };
 static string Csv(LocalReport report)
 {
  List<string[]> rows = [["Type", "Id", "Name/operation", "Status", "Reason/expected/requested", "Capture/time", "Actor/source", "Observed/before/after"]];
  rows.Add(["Report", report.Purpose.ToString(), report.Context.ComputerName, report.Completion, string.Join(" | ", report.Limitations), report.GeneratedAt.ToString("O"), report.Context.OriginalUserSid, JsonSerializer.Serialize(report.Catalogue)]);
  foreach (var c in report.Controls) rows.Add(["Control", c.ControlId, c.Name, c.Status.ToString(), c.Expected + " | " + c.Reason, c.CaptureId + " | " + c.CapturedAt?.ToString("O"), JsonSerializer.Serialize(c.CapturedBy), JsonSerializer.Serialize(c.Observations)]);
  foreach (var c in report.Captures) rows.Add(["Capture", c.Id.ToString(), c.Origin.WorkflowId + "/" + c.Origin.StageId, "Historical snapshot", "", c.CapturedAt.ToString("O"), JsonSerializer.Serialize(c.CapturedBy), JsonSerializer.Serialize(c.Observations)]);
  foreach (var r in report.ManualReviews) rows.Add(["Manual review", r.Id.ToString(), r.ControlId, r.Outcome.ToString(), r.Note + " | " + r.ScopeReason, r.ReviewedAt.ToString("O"), JsonSerializer.Serialize(r.Actor) + " | " + r.Source, ""]);
  foreach (var a in report.DeploymentActions) rows.Add(["Action", a.Planned.Id.ToString(), a.Planned.Request.OperationId, a.Result?.Outcome.ToString() ?? "Missing result / uncertain", JsonSerializer.Serialize(a.Planned.Request), report.Run?.StartedAt.ToString("O") ?? "No run", JsonSerializer.Serialize(a.Result?.Actor), JsonSerializer.Serialize(new { a.Planned.Before, a.Result?.After, a.Result?.Reason })]);
  foreach (var r in report.StandaloneResults) rows.Add(["Standalone result", r.RunId.ToString(), r.Result.ActionId.ToString(), r.Result.Outcome.ToString(), "Incomplete run / catalogue lineage unless paired with validated plan and run", "", JsonSerializer.Serialize(r.Result.Actor), JsonSerializer.Serialize(r)]);
  foreach(var r in report.DeploymentResolutions??[])rows.Add(["Deployment completion review",r.Id.ToString(),r.Inventory.ProductIdentity??"",r.ProcessStatus.ToString(),"Explicit administrator review; original uncertain outcome is unchanged",r.ReviewedAt.ToString("O"),JsonSerializer.Serialize(r.ReviewedBy),JsonSerializer.Serialize(r)]);
  foreach (var i in report.DispatchIntents) rows.Add([report.UnfinishedIntents.Contains(i) ? "Unfinished intent" : "Dispatch intent", i.RunId.ToString(), i.Action.Request.OperationId, "Uncertain; do not replay", i.PlanId.ToString(), "", "", JsonSerializer.Serialize(i)]);
  return string.Join("\r\n", rows.Select(row => string.Join(",", row.Select(CsvCell)))) + "\r\n";
 }
 static string Html(LocalReport report)
 {
  static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
  var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>Local device report</title><style>body{font:16px system-ui;margin:2rem;color:#17202a}table{border-collapse:collapse;width:100%}td,th{border:1px solid #aaa;padding:.5rem;text-align:left;vertical-align:top}pre{white-space:pre-wrap;overflow-wrap:anywhere}li{margin:.5rem 0}</style></head><body>");
  html.Append("<h1>").Append(E(report.Purpose)).Append("</h1><p>").Append(E(report.GeneratedAt.ToString("O"))).Append("</p><p>").Append(E(report.Context.ComputerName)).Append(" — original requester: ").Append(E(report.Context.OriginalUserName)).Append(" (").Append(E(report.Context.OriginalUserSid)).Append(")</p><p>").Append(E(report.Completion)).Append("</p><ul>");
  foreach (var limitation in report.Limitations) html.Append("<li>").Append(E(limitation)).Append("</li>");
  html.Append("</ul><h2>Control snapshots</h2><table><tr><th>Control / expected</th><th>Status / reason</th><th>Capture provenance</th><th>Observations / review</th></tr>");
  foreach (var c in report.Controls)
  {
   html.Append("<tr><td>").Append(E(c.ControlId)).Append(" — ").Append(E(c.Name)).Append("<p>").Append(E(c.Expected)).Append("</p></td><td>").Append(E(c.Status)).Append("<p>").Append(E(c.Reason)).Append("</p></td><td>").Append(E(c.CaptureId)).Append("<p>").Append(E(c.CapturedAt?.ToString("O"))).Append("</p><pre>").Append(E(JsonSerializer.Serialize(c.CapturedBy))).Append("</pre></td><td><pre>").Append(E(JsonSerializer.Serialize(new { c.Observations, c.ManualReview }, new JsonSerializerOptions { WriteIndented = true }))).Append("</pre></td></tr>");
  }
  html.Append("</table><h2>Complete selected evidence and deployment details</h2><p>Original requesting identity and actual executing actors, before/after values, parameters and uncertainty are retained below.</p><pre>");
  html.Append(E(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }))).Append("</pre></body></html>");
  return html.ToString();
 }
 public static async Task<ExportReceipt> ExportAsync(LocalReport report, ReportFormat format, string destination, CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  var bytes = new UTF8Encoding(false).GetBytes(Render(report, format));
  var fullPath = Path.GetFullPath(destination);
  var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".report-pending-" + Guid.NewGuid().ToString("N") + ".tmp");
  try
  {
   await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
   { await output.WriteAsync(bytes, ct); await output.FlushAsync(ct); }
   ct.ThrowIfCancellationRequested();
   File.Move(temporary, fullPath, false);
  }
  finally { if (File.Exists(temporary)) File.Delete(temporary); }
  return new(Path.GetFullPath(destination), format, bytes.Length);
 }
}
