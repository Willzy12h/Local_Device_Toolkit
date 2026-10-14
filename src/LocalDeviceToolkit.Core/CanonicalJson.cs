// Adapted from M365-Buildstandards Core/Json/CanonicalJson.cs; see provenance/import-manifest.json.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace LocalDeviceToolkit.Core;
public static class CanonicalJson
{
 public static string Digest<T>(T value) => Hash(Encoding.UTF8.GetBytes(Serialize(StrictJson.Value(value))));
 public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
 public static string Serialize(JsonElement value)
 {
  var result = new StringBuilder(); Write(value, result); return result.ToString();
 }
 static void Write(JsonElement value, StringBuilder result)
 {
  switch(value.ValueKind)
  {
   case JsonValueKind.Object:
    result.Append('{'); bool first=true;
    foreach(var property in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal))
    { if(!first) result.Append(','); first=false; result.Append(JsonSerializer.Serialize(property.Name)).Append(':'); Write(property.Value,result); }
    result.Append('}'); break;
   case JsonValueKind.Array:
    result.Append('['); bool firstItem=true;
    foreach(var item in value.EnumerateArray()) { if(!firstItem)result.Append(',');firstItem=false;Write(item,result); }
    result.Append(']');break;
   case JsonValueKind.String: result.Append(JsonSerializer.Serialize(value.GetString()));break;
   case JsonValueKind.Number: result.Append(value.TryGetDecimal(out var number) ? number.ToString("G29",CultureInfo.InvariantCulture) : value.GetRawText());break;
   default: result.Append(value.GetRawText());break;
  }
 }
}
