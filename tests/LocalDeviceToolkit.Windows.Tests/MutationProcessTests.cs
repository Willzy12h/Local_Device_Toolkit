using LocalDeviceToolkit.Windows.Processes;
using System.Diagnostics;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class MutationProcessTests
{
 [Fact] public async Task Timeout_returns_uncertainty_and_leaves_the_owned_process_running()
 {
  var start=OperatingSystem.IsWindows()?new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")):new ProcessStartInfo("/usr/bin/sleep");
  if(OperatingSystem.IsWindows()){foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","[System.Threading.Thread]::Sleep(2000)"})start.ArgumentList.Add(arg);}else start.ArgumentList.Add("2");
  var result=await new MutationProcessRunner().StartAndObserveAsync(start,TimeSpan.FromMilliseconds(25),default);
  Assert.Equal(MutationEnd.StillRunning,result.End);Assert.NotNull(result.ProcessId);using var child=Process.GetProcessById(result.ProcessId!.Value);Assert.False(child.HasExited);
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));await child.WaitForExitAsync(deadline.Token);
 }
 [Fact] public async Task Cancelled_queue_does_not_start_a_mutation()
 {
  using var cancelled=new CancellationTokenSource();cancelled.Cancel();
  await Assert.ThrowsAsync<OperationCanceledException>(()=>new MutationProcessRunner().StartAndObserveAsync(new ProcessStartInfo("THIS-MUST-NOT-START"),TimeSpan.FromSeconds(1),cancelled.Token));
 }
}
