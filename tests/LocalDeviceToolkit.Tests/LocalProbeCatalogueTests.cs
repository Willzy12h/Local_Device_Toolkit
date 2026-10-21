using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Catalogue;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class LocalProbeCatalogueTests
{
 [Fact] public void Local_checks_do_not_change_30_source_controls()
 {
  Assert.Equal(30,CatalogueLoader.LoadShipped().Controls.Count);
  Assert.Equal(24,LocalProbeCatalogue.All.Count);
  Assert.All(LocalProbeCatalogue.All,p=>Assert.StartsWith("local.",p.Id));
  Assert.Equal(24,LocalProbeCatalogue.All.Select(p=>p.Id).Distinct().Count());
 }
 [Theory][InlineData("local.network.tcp","{\"host\":\"example.com\",\"port\":443}")]
 [InlineData("local.network.dns","{\"hostname\":\"example.com\",\"queryType\":\"A\"}")]
 public void Network_checks_accept_only_explicit_bounded_targets(string id,string json)
 {
  using var doc=System.Text.Json.JsonDocument.Parse(json);LocalProbeCatalogue.ValidateInput(id,doc.RootElement);
  Assert.Throws<CatalogueValidationException>(()=>LocalProbeCatalogue.ValidateInput(id,StrictJson.Value(new {host="example.com",port=70000,command="anything"})));
 }
 [Fact] public void Unknown_expected_value_stays_observed_and_unreadable_is_not_missing()
 {
  Assert.Equal("Observed",LocalProbeValues.StatusLabel(ObservationState.Known,null));
  Assert.Equal("Unknown",LocalProbeValues.StatusLabel(ObservationState.AccessDenied,null));
 }
 [Fact] public void Profiles_never_supply_execution_permission()
 {
  Assert.Equal(SupportState.Unknown,ReferenceProfiles.EvaluateSupport("unrecognized","99H9",new DateOnly(2026,10,15)).State);
 }
 [Fact] public void Edition_support_and_reference_freshness_are_independent()
 {
  Assert.Equal(SupportState.OutOfSupport,ReferenceProfiles.EvaluateSupport("Professional","24H2",new DateOnly(2026,10,15)).State);
  Assert.Equal(SupportState.Supported,ReferenceProfiles.EvaluateSupport("Enterprise","24H2",new DateOnly(2026,10,15)).State);
  Assert.Equal(SupportState.Unknown,ReferenceProfiles.EvaluateSupport("Enterprise","24H2",new DateOnly(2026,11,16)).State);
 }
 [Fact] public void Value_projection_refuses_unregistered_fields_and_invented_match()
 {
  var invalid=new LocalProbeValue("Enabled",null,"Current",new Dictionary<string,string>());
  Assert.False(LocalProbeValues.Accepts("local.defender.protection",StrictJson.Value(invalid)));
  Assert.False(LocalProbeValues.Accepts("local.bitlocker.state",StrictJson.Value(new LocalProbeValue("Protected",null,"Observed",new Dictionary<string,string>{{"recoveryPassword","must never persist"}}))));
  using var malformed=System.Text.Json.JsonDocument.Parse("""{"current":"Observed","expected":null,"status":"Observed","details":{"mode":null}}""");
  Assert.False(LocalProbeValues.Accepts("local.defender.protection",malformed.RootElement));
 }
}
