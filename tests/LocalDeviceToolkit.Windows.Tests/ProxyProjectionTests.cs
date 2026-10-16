using LocalDeviceToolkit.Windows.Inspection.Probes;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class ProxyProjectionTests
{
 [Fact] public void Pac_credentials_query_and_fragments_are_not_retained()
 {
  var safe=ProxySanitizer.Sanitize("https://user:secret@proxy.example/cfg.pac?token=secret#secret");Assert.DoesNotContain("secret",safe);Assert.DoesNotContain("user",safe);Assert.Contains("proxy.example",safe);
 }
 [Theory][InlineData("file:///private/secret")][InlineData("not a url secret")]
 public void Unsupported_pac_is_guidance_without_raw_value(string raw)=>Assert.DoesNotContain("secret",ProxySanitizer.Sanitize(raw));
}
