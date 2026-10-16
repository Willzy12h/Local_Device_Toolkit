using System.Net;
using System.Text;
using System.Text.Json;
using LocalDeviceToolkit.Core;
namespace LocalDeviceToolkit.Engine.Reporting;
public sealed record ReportItemView(string Title,string State,string Summary,IReadOnlyDictionary<string,string> Details);
public sealed record ReportExportView(ReportPurpose Purpose,DateTimeOffset GeneratedAt,string DeviceLabel,string UserLabel,IReadOnlyList<ReportItemView> Items,IReadOnlyList<string> Limitations);
public static class ReportDisclosure
{
 public static ReportExportView Project(LocalReport report,bool includePermittedIdentifiers)
 {
  var items=new List<ReportItemView>();
  foreach(var control in report.Controls)items.Add(new(control.Name,control.Status.ToString(),control.Expected,new Dictionary<string,string>{{"collectedUtc",control.CapturedAt?.ToString("O")??"Not checked"},{"stale",control.Stale.ToString()}}));
  foreach(var capture in report.Captures)
   foreach(var observation in capture.Observations.Where(o=>LocalProbeValues.IsRegistered(o.CheckId)))
   {
    LocalProbeValue? value=observation.Value is {} raw&&LocalProbeValues.Accepts(observation.CheckId,raw)?StrictJson.Read<LocalProbeValue>(StrictJson.Bytes(raw)):null;
    var details=new Dictionary<string,string>{{"collectedUtc",observation.CapturedAt.ToString("O")},{"coverage",observation.Coverage is {Complete:true,HasMore:false}?"Complete":"Incomplete or unknown"},{"source",observation.Source}};
    if(includePermittedIdentifiers&&value is not null)foreach(var item in value.Details)details[item.Key]=item.Value;
    items.Add(new(observation.CheckId,LocalProbeValues.StatusLabel(observation.State,value),includePermittedIdentifiers?value?.Current??"Unknown":"Permitted values available in the local evidence viewer.",details));
   }
  foreach(var action in report.DeploymentActions)items.Add(new(action.Planned.Request.OperationId,action.Result?.Outcome.ToString()??"Unknown","Review the local audit for before/after values and execution identity.",new Dictionary<string,string>()));
  foreach(var review in report.ManualReviews)items.Add(new(review.ControlId,"Manual: "+review.Outcome,includePermittedIdentifiers?review.Note:"Scope and note retained in local evidence.",new Dictionary<string,string>{{"reviewedUtc",review.ReviewedAt.ToString("O")}}));
  return new(report.Purpose,report.GeneratedAt,includePermittedIdentifiers?report.Context.ComputerName:"Device identifiers omitted",includePermittedIdentifiers?report.Context.OriginalUserName:"User identifiers omitted",items,["Dated report projection; it is not original digest-valid evidence or permission to execute.","Unknown, incomplete and manual checks do not establish current readiness.",includePermittedIdentifiers?"Permitted identifying details included after explicit selection. Review before sharing.":"Identifying details and free-text notes are minimized. Original evidence remains local."]);
 }
 public static string Render(ReportExportView report,ReportFormat format)
 {
  if(format==ReportFormat.Json)return JsonSerializer.Serialize(report,new JsonSerializerOptions(StrictJson.Options){WriteIndented=true});
  if(format==ReportFormat.Csv)return string.Join("\r\n",new[]{new[]{"Title","State","Summary","Details"},new[]{"Report",report.Purpose.ToString(),report.GeneratedAt.ToString("O"),report.DeviceLabel+"; "+report.UserLabel+"; "+string.Join("; ",report.Limitations)}}.Concat(report.Items.Select(i=>new[]{i.Title,i.State,i.Summary,string.Join("; ",i.Details.Select(p=>p.Key+"="+p.Value))})).Select(row=>string.Join(",",row.Select(ReportExporter.CsvCell))))+"\r\n";
  if(format!=ReportFormat.Html)throw new ArgumentOutOfRangeException(nameof(format));
  static string E(string text)=>WebUtility.HtmlEncode(text);
  return "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'\"><title>Device report</title><body><h1>"+E(report.Purpose.ToString())+"</h1><p>"+E(report.GeneratedAt.ToString("O")+" · "+report.DeviceLabel+" · "+report.UserLabel)+"</p>"+string.Join("",report.Items.Select(i=>"<h2>"+E(i.Title)+"</h2><p>"+E(i.State+": "+i.Summary)+"</p><pre>"+E(string.Join("\n",i.Details.Select(p=>p.Key+": "+p.Value)))+"</pre>"))+string.Join("",report.Limitations.Select(l=>"<p>"+E(l)+"</p>"))+"</body></html>";
 }
 public static async Task ExportAsync(ReportExportView report,ReportFormat format,string destination,CancellationToken ct)
 {
  var bytes=new UTF8Encoding(false).GetBytes(Render(report,format));var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{await File.WriteAllBytesAsync(temporary,bytes,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,destination,false);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
