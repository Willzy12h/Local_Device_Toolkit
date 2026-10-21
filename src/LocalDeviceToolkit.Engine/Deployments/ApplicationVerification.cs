namespace LocalDeviceToolkit.Engine.Deployments;
public enum VerificationState { Verified,Failed,Unknown,ManualRequired,NotApplicable }
public sealed record ApplicationVerification(VerificationState PackageChecked,VerificationState Installed,VerificationState Configured,VerificationState SignedIn,VerificationState Licensed,VerificationState Restart,VerificationState Verified,IReadOnlyList<string> Reasons);
public static class ApplicationVerificationService
{
 public static ApplicationVerification Evaluate(DeploymentInventory inventory,string identity,string version,bool pending)
 {
  var installed=!inventory.Available?VerificationState.Unknown:inventory.Present&&inventory.ProductIdentity==identity&&inventory.Version==version?VerificationState.Verified:VerificationState.Failed;
  return new(VerificationState.Unknown,installed,VerificationState.ManualRequired,VerificationState.ManualRequired,VerificationState.ManualRequired,VerificationState.Unknown,pending?VerificationState.Unknown:installed==VerificationState.Failed?VerificationState.Failed:VerificationState.ManualRequired,
   [inventory.Reason,"Installed identity/version do not establish configuration, account sign-in, licensing, or restart completion.",pending?"An unresolved deployment prevents readiness.":"No installer or package download was started; package media was not checked by this inventory read."]);
 }
}
