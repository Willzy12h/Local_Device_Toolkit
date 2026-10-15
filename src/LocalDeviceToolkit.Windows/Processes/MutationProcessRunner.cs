using System.Diagnostics;
namespace LocalDeviceToolkit.Windows.Processes;
public enum MutationEnd { Exited,NotStarted,StillRunning,CancelledWhileRunning,ObservationLost }
public sealed record MutationObservation(MutationEnd End,int? ProcessId,DateTimeOffset? ProcessStartedAt,int? ExitCode);
public sealed class MutationProcessRunner
{
 internal async Task<MutationObservation> StartAndObserveAsync(ProcessStartInfo start,TimeSpan timeout,CancellationToken stop)
 {
  stop.ThrowIfCancellationRequested();if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromHours(1))throw new ArgumentOutOfRangeException(nameof(timeout));
  start.UseShellExecute=false;start.CreateNoWindow=true;
  // Installers are never supervised by the diagnostic tree-kill runner. No inherited pipes can hold their completion hostage.
  start.RedirectStandardOutput=start.RedirectStandardError=start.RedirectStandardInput=false;start.StandardOutputEncoding=start.StandardErrorEncoding=null;
  using var process=new Process {StartInfo=start};
  try{if(!process.Start())return new(MutationEnd.NotStarted,null,null,null);}
  catch(System.ComponentModel.Win32Exception){return new(MutationEnd.NotStarted,null,null,null);}
  var pid=process.Id;DateTimeOffset? created=null;
  try
  {
   created=new DateTimeOffset(process.StartTime.ToUniversalTime());
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);deadline.CancelAfter(timeout);
   try{await process.WaitForExitAsync(deadline.Token);return new(MutationEnd.Exited,pid,created,process.ExitCode);}
   catch(OperationCanceledException)
   {
    if(process.HasExited)return new(MutationEnd.Exited,pid,created,process.ExitCode);
    return new(stop.IsCancellationRequested?MutationEnd.CancelledWhileRunning:MutationEnd.StillRunning,pid,created,null);
   }
  }
  catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception){return new(MutationEnd.ObservationLost,pid,created,null);}
  // Disposing a Process handle never kills the child. Unknown outcomes require read-only reconciliation and a fresh plan.
 }
}
