using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class ReviewLifetimeTests
{
 [Fact] public void Wall_clock_rollback_cannot_extend_elapsed_limit_and_restart_is_not_replayable()
 {
  var lifetime=new ReviewLifetime(1000,42,DateTimeOffset.UtcNow);
  Assert.False(lifetime.IsExpired(600999));Assert.True(lifetime.IsExpired(601000));Assert.True(lifetime.IsExpired(999));
 }
}
