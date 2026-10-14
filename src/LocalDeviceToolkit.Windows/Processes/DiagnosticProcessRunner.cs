using System.Diagnostics;
using System.Text;
namespace LocalDeviceToolkit.Windows.Processes;
public enum DiagnosticEnd { Exited,NotStarted,TimedOut,Cancelled,OutputLimitReached,OutputHeldOpen,NotStopped }
public sealed record DiagnosticResult(DiagnosticEnd End,int? ExitCode,string StandardOutput,string StandardError);
public sealed class DiagnosticProcessRunner
{
 internal async Task<DiagnosticResult> RunCommandAsync(ProcessStartInfo start,TimeSpan timeout,CancellationToken ct)
 {
  if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromMinutes(5))throw new ArgumentOutOfRangeException(nameof(timeout));
  start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
  var output=new BoundedText();var errors=new BoundedText();
  var result=await OwnedProcess.RunAsync(start,output.ReadAsync,errors.ReadAsync,timeout,()=>output.Limited||errors.Limited,()=>{},TimeSpan.Zero,ct);
  var end=result.End switch
  {
   OwnedProcessEnd.Exited=>DiagnosticEnd.Exited,OwnedProcessEnd.NotStarted=>DiagnosticEnd.NotStarted,OwnedProcessEnd.TimedOut=>DiagnosticEnd.TimedOut,OwnedProcessEnd.Cancelled=>DiagnosticEnd.Cancelled,
   OwnedProcessEnd.LimitExceeded=>DiagnosticEnd.OutputLimitReached,OwnedProcessEnd.OutputHeldOpen=>DiagnosticEnd.OutputHeldOpen,_=>DiagnosticEnd.NotStopped
  };
  if(end is DiagnosticEnd.NotStopped or DiagnosticEnd.OutputHeldOpen)return new(end,null,"","");
  return new(end,result.ExitCode,output.Text,errors.Text);
 }
 sealed class BoundedText
 {
  readonly StringBuilder text=new();int bytes;public volatile bool Limited;
  public string Text=>text.ToString();
  public async Task ReadAsync(StreamReader reader)
  {
   var buffer=new char[4096];
   try
   {
    int count;while((count=await reader.ReadAsync(buffer.AsMemory()))>0)
    {
     if(Limited)continue;
     var addition=Encoding.UTF8.GetByteCount(buffer.AsSpan(0,count));
     if(bytes+addition>1024*1024){Limited=true;continue;}
     text.Append(buffer,0,count);bytes+=addition;
    }
   }
   catch(Exception e) when(e is IOException or ObjectDisposedException){ }
  }
 }
}
