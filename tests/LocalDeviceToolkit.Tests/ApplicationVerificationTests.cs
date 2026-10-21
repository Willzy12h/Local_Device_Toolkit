using LocalDeviceToolkit.Engine.Deployments;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ApplicationVerificationTests
{
 [Fact] public void Installed_is_not_configured_signed_in_or_licensed()
 {
  var result=ApplicationVerificationService.Evaluate(new(true,true,"product","1.0","Installed"),"product","1.0",false);
  Assert.Equal(VerificationState.Verified,result.Installed);Assert.Equal(VerificationState.ManualRequired,result.Licensed);Assert.Equal(VerificationState.ManualRequired,result.SignedIn);Assert.NotEqual(VerificationState.Verified,result.Verified);
 }
 [Fact] public void Pending_process_blocks_verification()
 {Assert.Equal(VerificationState.Unknown,ApplicationVerificationService.Evaluate(new(true,true,"product","1.0","Installed"),"product","1.0",true).Verified);}
}
