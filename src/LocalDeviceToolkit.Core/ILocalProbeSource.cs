using System.Text.Json;
namespace LocalDeviceToolkit.Core;
public interface ILocalProbeSource
{
 Task<LocalProbeResult> ReadAsync(string checkId,JsonElement parameters,DeviceContext context,CancellationToken ct);
}
