using LocalDeviceToolkit.Windows.Elevation;
using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class NativePeerTests
{
 [Fact] public async Task Peer_identity_is_observed_from_the_process_token()
 {
  if(!OperatingSystem.IsWindows()){Assert.Throws<PlatformNotSupportedException>(()=>NativePeer.Observe(Environment.ProcessId));return;}
  var peer=NativePeer.Observe(Environment.ProcessId);var actor=await new WindowsContextProvider().GetActorAsync(default);
  Assert.Equal(actor,peer.Actor);Assert.True(Path.IsPathFullyQualified(peer.ImagePath));
 }
 [Fact] public async Task Local_pipe_identifies_both_process_endpoints()
 {
  if(!OperatingSystem.IsWindows()){Assert.Throws<PlatformNotSupportedException>(()=>LocalWorkerPipe.Create("LocalDeviceToolkit-"+Guid.NewGuid().ToString("N"),"S-1-5-18"));return;}
  var actor=await new WindowsContextProvider().GetActorAsync(default);var name="LocalDeviceToolkit-"+Guid.NewGuid().ToString("N");
  using var server=LocalWorkerPipe.Create(name,actor.Sid);using var client=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));var connection=server.WaitForConnectionAsync(timeout.Token);await client.ConnectAsync(timeout.Token);await connection;
  Assert.Equal(Environment.ProcessId,NativePeer.PipeClient(server.SafePipeHandle));Assert.Equal(Environment.ProcessId,NativePeer.PipeServer(client.SafePipeHandle));
 }
}
