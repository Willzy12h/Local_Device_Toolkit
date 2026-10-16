using LocalDeviceToolkit.Core;
using LocalDeviceToolkit.Engine.Elevation;
using LocalDeviceToolkit.Engine.Execution;
using System.Buffers.Binary;
using Xunit;
namespace LocalDeviceToolkit.Tests;
public sealed class WorkerProtocolTests
{
 [Fact] public async Task Oversized_frame_is_refused_before_body_read()
 {
  using var stream=new MemoryStream();var length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,WorkerFrames.MaximumBytes+1);stream.Write(length);stream.Position=0;
  await Assert.ThrowsAsync<InvalidDataException>(()=>WorkerFrames.ReadAsync<WorkerRequest>(stream,default));Assert.Equal(4,stream.Position);
 }
 [Fact] public async Task Partial_message_is_not_a_completed_request()
 {
  using var stream=new MemoryStream(new byte[]{20,0,0,0,123});await Assert.ThrowsAsync<EndOfStreamException>(()=>WorkerFrames.ReadAsync<WorkerRequest>(stream,default));
 }
 [Fact] public async Task Unknown_operation_and_duplicate_json_are_not_accepted()
 {
  var bytes=System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}");using var stream=new MemoryStream();var size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);stream.Write(size);stream.Write(bytes);stream.Position=0;
  await Assert.ThrowsAsync<System.Text.Json.JsonException>(()=>WorkerFrames.ReadAsync<WorkerRequest>(stream,default));
 }
 [Fact] public void Alternate_admin_cannot_replace_the_original_engineer()
 {
  Assert.Throws<PlanValidationException>(()=>WorkerGuard.ValidateOriginal(TestData.Context with {OriginalUserSid="S-1-5-21-999"},TestData.Context,TestData.Actor));
 }
 [Fact] public void Peer_identity_is_not_inferred_from_the_request()
 {
  Assert.Throws<PlanValidationException>(()=>WorkerGuard.ValidateOriginal(TestData.Context,TestData.Context,TestData.Actor with {IsElevated=true}));
 }
 [Theory][InlineData(null)][InlineData("older-build")]
 public void Mixed_UI_worker_builds_are_refused(string? requested)=>Assert.Throws<PlanValidationException>(()=>WorkerGuard.ValidateBuild(requested,"current-build"));
 [Fact] public void Protected_reads_cannot_switch_to_the_alternate_admins_profile()
 {
  var request=new WorkerRequest(2,WorkerMode.CaptureProtected,TestData.Context,new(["CFG-WIN-012"],new("diagnostics","configuration"),["extensions.visible"],["windows.file-extensions"]),null,null);
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request,LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.LoadShipped(),LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue.LoadShipped()));
 }
 [Fact] public async Task Fixed_typed_request_survives_a_real_frame_roundtrip()
 {
  var request=new WorkerRequest(2,WorkerMode.CaptureProtected,TestData.Context,new(["CMP-WIN-001"],new("diagnostics","health"),["secureboot.enabled"],["readiness.secure-boot"]),null,null);
  using var stream=new MemoryStream();await WorkerFrames.WriteAsync(stream,request,default);stream.Position=0;
  var result=await WorkerFrames.ReadAsync<WorkerRequest>(stream,default);Assert.Equal(request.OriginalContext,result.OriginalContext);Assert.Equal(request.Capture!.ControlIds,result.Capture!.ControlIds);
 }
 [Fact] public void Protected_read_requires_exact_registered_selection_and_current_protocol()
 {
  var catalogue=LocalDeviceToolkit.Engine.Catalogue.CatalogueLoader.LoadShipped();var flows=LocalDeviceToolkit.Engine.Catalogue.WorkflowCatalogue.LoadShipped();
  var request=new WorkerRequest(2,WorkerMode.CaptureProtected,TestData.Context,new(["CMP-WIN-001"],new("diagnostics","health"),["secureboot.enabled"],["readiness.secure-boot"]),null,null);
  WorkerRequestValidator.Validate(request,catalogue,flows);
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with {SchemaVersion=1},catalogue,flows));
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with {Capture=request.Capture! with {CheckIds=["tpm.ready"]}},catalogue,flows));
  Assert.Throws<PlanValidationException>(()=>WorkerRequestValidator.Validate(request with {Capture=request.Capture! with {CheckIds=["secureboot.enabled","secureboot.enabled"]}},catalogue,flows));
 }
}
