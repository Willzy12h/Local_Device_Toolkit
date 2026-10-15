using System.Security.Cryptography;
using System.Text;
namespace LocalDeviceToolkit.Engine.Elevation;
public static class ProtectedJournalIds
{
 public static Guid Result(Guid planId,Guid actionId)=>new(SHA256.HashData(Encoding.UTF8.GetBytes(planId.ToString("D")+"/"+actionId.ToString("D")+"/result")).AsSpan(0,16));
}
