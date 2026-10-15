using LocalDeviceToolkit.Core;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class MalformedPlanTests
{
 [Theory][InlineData(false)][InlineData(true)]
 public async Task Invalid_action_inventory_is_a_controlled_refusal(bool nullItem)
 {
  using var f=new PlanningFixture();var plan=await f.CreatePlan();var action=plan.Actions[0];
  var malformed=plan with{Actions=nullItem?new PlannedAction[]{null!}:null!};
  Assert.False(f.Plans.Validate(malformed,f.Current,f.Catalogue.Identity,new Dictionary<Guid,string>(),plan.CreatedAt).IsValid);
  Assert.False(f.Plans.ValidateAction(malformed,action,f.Current,f.Catalogue.Identity,action.Before,plan.CreatedAt).IsValid);
 }
}
