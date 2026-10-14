using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace LocalDeviceToolkit.Core;
public static class StrictJson
{
 public const int RecordLimit = 8 * 1024 * 1024;
 public static readonly JsonSerializerOptions Options = CreateOptions();
 static JsonSerializerOptions CreateOptions()
 {
  var result = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, MaxDepth = 64 };
  result.Converters.Add(new StrictEnumConverterFactory()); return result;
 }
 public static JsonDocument Parse(ReadOnlyMemory<byte> bytes, int maximumBytes = RecordLimit)
 {
  if(bytes.Length > maximumBytes) throw new InvalidDataException("JSON exceeds the permitted size.");
  _ = new UTF8Encoding(false, true).GetCharCount(bytes.Span);
  var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
  try { Check(document.RootElement); return document; } catch { document.Dispose(); throw; }
 }
 static void Check(JsonElement element)
 {
  if(element.ValueKind == JsonValueKind.Object)
  {
   var names = new HashSet<string>(StringComparer.Ordinal);
   foreach(var p in element.EnumerateObject()) { if(!names.Add(p.Name)) throw new JsonException("Duplicate JSON member: " + p.Name); Check(p.Value); }
  }
  else if(element.ValueKind == JsonValueKind.Array) foreach(var item in element.EnumerateArray()) Check(item);
 }
 public static T Read<T>(ReadOnlyMemory<byte> bytes, int maximumBytes = RecordLimit)
 {
  using var document = Parse(bytes, maximumBytes);
  return document.RootElement.Deserialize<T>(Options) ?? throw new JsonException("Null JSON record.");
 }
 public static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
 public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
sealed class StrictEnumConverterFactory : JsonConverterFactory
{
 public override bool CanConvert(Type type) => type.IsEnum;
 public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) => (JsonConverter)Activator.CreateInstance(typeof(StrictEnumConverter<>).MakeGenericType(type))!;
}
sealed class StrictEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
 public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
 {
  if(reader.TokenType != JsonTokenType.String) throw new JsonException("An enum name is required.");
  var text = reader.GetString();
  if(text is null || !Enum.GetNames<T>().Contains(text, StringComparer.Ordinal)) throw new JsonException("Unknown enum value.");
  return Enum.Parse<T>(text);
 }
 public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
 {
  if(!Enum.IsDefined(value)) throw new JsonException("Unknown enum value.");
  writer.WriteStringValue(value.ToString());
 }
}
