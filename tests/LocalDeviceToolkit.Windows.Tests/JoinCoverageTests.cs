using LocalDeviceToolkit.Windows.Inspection;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class JoinCoverageTests
{
 [Fact] public void Domain_denied_keeps_Entra_observation_and_incomplete_coverage()
 {
  var result=NativeJoinContext.Project(5,0,0,true,1);Assert.False(result.Complete);Assert.Contains("Entra device join: Observed",result.Current);Assert.Contains("Domain: Unknown",result.Current);
 }
}
