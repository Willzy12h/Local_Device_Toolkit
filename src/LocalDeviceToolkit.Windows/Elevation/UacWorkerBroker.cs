using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Elevation;
using System.ComponentModel;
using System.Diagnostics;
namespace LocalDeviceToolkit.Windows.Elevation;
public sealed class ElevationCancelledException:Exception{public ElevationCancelledException():base("Administrator approval was cancelled. No protected action was started."){} }
public sealed class UacWorkerBroker(string workerPath)
{
 public async Task<WorkerResponse> RequestAsync(WorkerRequest request,CancellationToken ct)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  ct.ThrowIfCancellationRequested();var parent=NativePeer.Observe(Environment.ProcessId);
  if(request.OriginalContext.OriginalUserSid!=parent.Actor.Sid)throw new InvalidDataException("Original engineer does not match this Windows session.");
  var name="LocalDeviceToolkit-"+Guid.NewGuid().ToString("N");
  using var pipe=LocalWorkerPipe.Create(name,parent.Actor.Sid);
  var fullWorker=Path.GetFullPath(workerPath);LocalDeviceToolkit.Engine.Evidence.EvidenceStore.RefuseReparsePath(fullWorker);
  var start=new ProcessStartInfo(fullWorker){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(fullWorker)!};
  foreach(var argument in new[]{"--pipe",name,"--parent-pid",Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),"--parent-started",parent.Actor.ProcessStartedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(argument);
  Process? launched;
  try{launched=await Task.Run(()=>Process.Start(start));}
  catch(Win32Exception error) when(error.NativeErrorCode==1223){throw new ElevationCancelledException();}
  using var worker=launched??throw new IOException("Windows did not return a worker process handle.");
  var expected=NativePeer.Observe(worker.Id);
  if(!expected.Actor.IsElevated||!string.Equals(expected.ImagePath,fullWorker,StringComparison.OrdinalIgnoreCase))throw new IOException("Windows launched an unexpected or unelevated worker.");
  using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct))
  {
   deadline.CancelAfter(TimeSpan.FromSeconds(30));await pipe.WaitForConnectionAsync(deadline.Token);
  }
  var peer=NativePeer.Observe(NativePeer.PipeClient(pipe.SafePipeHandle));
  if(peer.Actor!=expected.Actor||!string.Equals(peer.ImagePath,expected.ImagePath,StringComparison.OrdinalIgnoreCase))throw new IOException("The connected process is not the worker launched through UAC.");
  if(request.Plan is {} plan&&(plan.Lifetime is null||!plan.Lifetime.Matches(parent.Actor)||plan.Lifetime.IsExpired(Environment.TickCount64)))throw new InvalidDataException("The review elapsed limit expired during elevation. Check and review again.");
  request=request with {ToolkitBuild=ToolkitBuildIdentity.Current};
  await WorkerFrames.WriteAsync(pipe,request,ct);
  var response=await WorkerFrames.ReadAsync<WorkerResponse>(pipe,ct);
  if(response.SchemaVersion!=2||response.Actor!=peer.Actor||response.Capture is not null&&response.Capture.CapturedBy!=peer.Actor||response.Result?.Actor is not null&&response.Result.Actor!=peer.Actor)throw new InvalidDataException("Worker response identity did not match the OS-observed process.");
  return response;
 }
}

public static class ToolkitBuildIdentity
{
 public static string Current=>System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(ToolkitBuildIdentity).Assembly)?.InformationalVersion??throw new InvalidDataException("Toolkit build identity is missing.");
}
