using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
namespace LocalDeviceToolkit.Tests;
public static class TestData
{
 public static DeviceContext Context => new("11111111-2222-3333-4444-555555555555","SYNTHETIC-PC","S-1-5-21-100-200-300-1001","TEST\\engineer",false,new Version(10,0,26200),"x64");
 public static ExecutionActor Actor => new(Context.OriginalUserSid,Context.OriginalUserName,false,42,DateTimeOffset.UtcNow.AddMinutes(-1));
 public static DeviceCapture Capture() => new(Guid.NewGuid(),Context,CatalogueLoader.LoadShipped().Identity,new("diagnostics","health"),Actor,DateTimeOffset.UtcNow,[]);
}
