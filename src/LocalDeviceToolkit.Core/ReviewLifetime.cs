namespace LocalDeviceToolkit.Core;
/// <summary>Windows uptime includes sleep. Parent identity prevents reuse after app restart.</summary>
public sealed record ReviewLifetime(long StartedUptimeMilliseconds,int ParentProcessId,DateTimeOffset ParentProcessStartedAt)
{
 public bool IsExpired(long uptimeMilliseconds)=>StartedUptimeMilliseconds<0||uptimeMilliseconds<StartedUptimeMilliseconds||uptimeMilliseconds-StartedUptimeMilliseconds>=600000;
 public static ReviewLifetime Start(){using var process=System.Diagnostics.Process.GetCurrentProcess();return new(Environment.TickCount64,process.Id,new DateTimeOffset(process.StartTime.ToUniversalTime()));}
 public bool Matches(ExecutionActor actor)=>ParentProcessId==actor.ProcessId&&ParentProcessStartedAt==actor.ProcessStartedAt;
}
