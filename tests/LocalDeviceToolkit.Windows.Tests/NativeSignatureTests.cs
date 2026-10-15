using LocalDeviceToolkit.Windows.Deployments;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class NativeSignatureTests
{
 [Fact] public void Windows_trust_returns_the_actual_accepted_signer_and_refuses_a_different_pin()
 {
  if(!OperatingSystem.IsWindows()){Assert.False(new WindowsSignatureTrust().Verify("unsupported",new string('a',64)).Accepted);return;}
  var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
#pragma warning disable SYSLIB0057 // Test extraction from a known PE; production uses the signer accepted by WinVerifyTrust, including SIP packages.
  using var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
  var pin=Convert.ToHexString(SHA256.HashData(certificate.RawData));var actual=new WindowsSignatureTrust().Verify(path,pin);
  Assert.True(actual.Accepted,actual.Reason);Assert.Equal("Microsoft Corporation",actual.SignerOrganization);
  var wrong=new WindowsSignatureTrust().Verify(path,new string('0',64));Assert.False(wrong.Accepted);Assert.Contains("pin",wrong.Reason,StringComparison.OrdinalIgnoreCase);
 }
}
