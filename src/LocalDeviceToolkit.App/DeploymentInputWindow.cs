using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalDeviceToolkit.Engine.Deployments;
using LocalDeviceToolkit.Windows.Deployments;
using LocalDeviceToolkit.Engine.Evidence;
using Microsoft.Win32;
namespace LocalDeviceToolkit.App;
/// <summary>Selects profile/media parameters. Cached files are untrusted display data; the worker authorizes independently.</summary>
public sealed class DeploymentInputWindow : Window
{
 readonly ApprovedProfileStore cache;
 readonly Func<DeploymentProfile, string, CancellationToken, Task<DeploymentProfile>> approveImport;
 readonly string? controlId;
 readonly Func<DeploymentParameters, bool, CancellationToken, Task<string>>? reconcile;
 readonly ComboBox profiles = new() { MinWidth = 320, DisplayMemberPath = "Display", MaxDropDownHeight = 240 };
 readonly TextBox source = new() { MaxLength = 2048, MinWidth = 300 };
 readonly TextBox metadata = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 180, MaxHeight=360 };
 readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
 readonly Button accept = new() { Content = "Use selected profile and media", Padding = new Thickness(10), Margin = new Thickness(4), IsEnabled = false };
 bool busy;
 sealed record ProfileChoice(DeploymentProfile Profile) { public string Display => $"{Profile.ProductName} {Profile.Version} — {Profile.Format} — {Profile.Id[..12]}…"; }
 public DeploymentInputWindow(ApprovedProfileStore cache, Func<DeploymentProfile, string, CancellationToken, Task<DeploymentProfile>> approveImport, string? controlId = null, Func<DeploymentParameters, bool, CancellationToken, Task<string>>? reconcile = null)
 {
  this.cache = cache; this.approveImport = approveImport; this.controlId = controlId; this.reconcile = reconcile;
  Title = "Select deployment profile and local media"; Width = 880; Height = 780; MinWidth = 650; MinHeight = 480; FontSize = 14; WindowStartupLocation = WindowStartupLocation.CenterOwner;
  var panel = new DockPanel { Margin = new Thickness(24) }; Content = new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
  top.Children.Add(new TextBlock { Text = "Deployment inputs", FontSize = 24 });
  top.Children.Add(new TextBlock { Text = "Choose a cached profile and the complete matching local installer folder. Supported profiles are machine MSI, per-machine OneDrive, local Office Deployment Tool media and original-user MSIX/Appx packages or bundles. Profiles require separate administrator approval. MSIX installation and reconciliation stay in the original engineer account; machine actions use the protected worker.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
  Label(top, "Cached profile selection (the user cache does not authorize installation)", profiles);
  var tools = new WrapPanel(); top.Children.Add(tools); Button(tools, "Reload cached profiles", () => ReloadAsync(null)); Button(tools, "Import profile JSON for administrator approval", ImportAsync); Button(tools, "Profile JSON requirements", ShowSchemaAsync);
  Label(top, "Local media folder (all and only the manifest files)", source);
  var media = new WrapPanel(); top.Children.Add(media); Button(media, "Choose local media folder", BrowseAsync);
  if (reconcile is not null) { Button(media, "Read-only reconcile", () => ReconcileAsync(false)); Button(media, "Resolve reviewed completion", () => ReconcileAsync(true)); }
  top.Children.Add(status);
  var bottom = new WrapPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom); bottom.Children.Add(accept); bottom.Children.Add(new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10), Margin = new Thickness(4) });
  accept.Click += (_, _) =>
  {
   try { _ = ValidateDirectory(); if (SelectedProfile is null) throw new InvalidOperationException("Select a cached profile first."); selectedParameters = new(SelectedProfile.Id, ValidateDirectory()); DialogResult = true; }
   catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException or EvidenceValidationException) { status.Text = e.Message; }
  };
  profiles.SelectionChanged += (_, _) => { metadata.Text = SelectedProfile is { } profile ? Describe(profile) : "No profile selected."; UpdateEnabled(); };
  source.TextChanged += (_, _) => UpdateEnabled(); panel.Children.Add(metadata);
  Loaded += async (_, _) => await GuardAsync(() => ReloadAsync(null));
  System.Windows.Automation.AutomationProperties.SetName(metadata, "Complete selected profile metadata, file hashes and signing certificate pins");
 }
 DeploymentParameters? selectedParameters;
 public DeploymentParameters Parameters => selectedParameters ?? throw new InvalidOperationException("Confirm the selected profile and local media first.");
 public DeploymentProfile? SelectedProfile => (profiles.SelectedItem as ProfileChoice)?.Profile;
 static void Label(Panel parent, string text, Control field) { parent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) }); parent.Children.Add(field); System.Windows.Automation.AutomationProperties.SetName(field, text); }
 void Button(Panel panel, string text, Func<Task> action)
 {
  var button = new System.Windows.Controls.Button { Content = text, Padding = new Thickness(8), Margin = new Thickness(4) };
  button.Click += async (_, _) => await GuardAsync(action); panel.Children.Add(button);
 }
 async Task GuardAsync(Func<Task> action)
 {
  if (busy) return; busy = true; UpdateEnabled();
  try { await action(); }
  catch (OperationCanceledException) { status.Text = "Administrator approval was cancelled. No installer was started by this form."; }
  catch (LocalDeviceToolkit.Windows.Elevation.ElevationCancelledException e) { status.Text=e.Message; }
  catch (Exception e)
  { status.Text = "Unable to use deployment input: " + e.Message; }
  finally { busy = false; UpdateEnabled(); }
 }
 void UpdateEnabled() => accept.IsEnabled = !busy && SelectedProfile is not null && !string.IsNullOrWhiteSpace(source.Text);
 string ValidateDirectory()
 {
  if (string.IsNullOrWhiteSpace(source.Text) || !Path.IsPathFullyQualified(source.Text)) throw new ArgumentException("Choose an absolute local media folder.");
  var directory = Path.GetFullPath(source.Text);
  if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.StartsWith("//", StringComparison.Ordinal) || !Directory.Exists(directory)) throw new ArgumentException("Copy the complete approved media to an existing local folder; UNC shares are not accepted.");
  EvidenceStore.RefuseReparsePath(directory); return directory;
 }
 Task BrowseAsync()
 {
  var dialog = new OpenFolderDialog { Title = "Choose complete approved local installer media", Multiselect = false };
  if (dialog.ShowDialog(this) == true) source.Text = dialog.FolderName; return Task.CompletedTask;
 }
 async Task ReloadAsync(string? selectedId)
 {
  var list = await cache.ReadAllAsync(CancellationToken.None);
  profiles.ItemsSource = list.Profiles.Where(p => controlId is null || p.ControlId == controlId).Select(p => new ProfileChoice(p)).ToArray();
  profiles.SelectedItem = profiles.Items.Cast<ProfileChoice>().FirstOrDefault(p => p.Profile.Id == selectedId) ?? profiles.Items.Cast<ProfileChoice>().FirstOrDefault();
  status.Text = $"{profiles.Items.Count} cached profiles for this selection. " + (list.LimitReached ? "Directory exceeds the 1000-file limit; listing is incomplete. " : "") +
   string.Join("\n", list.Notices.Select(n => n.FileName + ": " + n.Message));
 }
 async Task ImportAsync()
 {
  var directory = ValidateDirectory();
  var choose = new OpenFileDialog { Title = "Select proposed deployment profile JSON", Filter = "Deployment profile JSON|*.json", CheckFileExists = true, Multiselect = false };
  if (choose.ShowDialog(this) != true) return;
  var proposed = await ApprovedProfileStore.ReadImportAsync(choose.FileName, CancellationToken.None);
  if (controlId is not null && proposed.ControlId != controlId) throw new InvalidDataException("Imported profile belongs to a different application control.");
  var review = new ProfileReviewWindow(proposed, directory) { Owner = this };
  if (review.ShowDialog() != true) return;
  // This callback performs the independent UAC review and protected write. Import never starts an installer.
  var approved = await approveImport(proposed, directory, CancellationToken.None);
  if (approved.Id != proposed.Id || ApprovedProfileStore.ContentId(approved) != proposed.Id) throw new InvalidDataException("Approval returned different profile content; no cache entry was selected.");
  try { await cache.SaveAsync(approved, CancellationToken.None); }
  catch (IOException) when (File.Exists(Path.Combine(cache.Root, approved.Id + ".json"))) { _ = await cache.LoadAsync(approved.Id, CancellationToken.None); }
  await ReloadAsync(approved.Id);
  status.Text = "Administrator profile approval returned successfully. Cached selection refreshed. Import did not install software. Installation remains a separate reviewed queue action.";
 }
 async Task ReconcileAsync(bool resolve)
 {
  if (reconcile is null || SelectedProfile is null) throw new InvalidOperationException("Select a cached profile to reconcile.");
  var parameters = new DeploymentParameters(SelectedProfile.Id, Directory.Exists(source.Text)?ValidateDirectory():Path.GetTempPath());
  if (resolve && MessageBox.Show(this,
      "Confirm you have independently reviewed vendor completion and verified that the installer and all detached child processes have finished. This requests administrator review to resolve the deployment replay guard only. It does not install, restart, or replay software and does not change the original run outcome. Continue?",
      "Resolve reviewed deployment completion", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
  status.Text = "Requesting independent administrator reconciliation; no installation is requested.";
  var result = await reconcile(parameters, resolve, CancellationToken.None);
  status.Text = result; // Show only the actual returned receipt/result, never an assumed completion.
 }
 static string Describe(DeploymentProfile profile)
 {
  var text = new StringBuilder();
  text.AppendLine($"Content ID: {profile.Id}\nControl: {profile.ControlId}\nProduct: {profile.ProductName}\nProduct identity / MSI ProductCode: {profile.ProductIdentity}\nVersion: {profile.Version}\nPublisher: {profile.Publisher}\nFormat: {profile.Format}\nScope: {profile.Scope}\nArchitecture: {profile.Architecture}\nEntry point: {profile.EntryPoint}\nTimeout: {profile.Timeout}\n\nComplete approved file manifest:");
  foreach (var file in profile.Files) text.AppendLine($"{file.RelativePath}\n  SHA-256: {file.Sha256}\n  Signing certificate SHA-256: {file.SignerCertificateSha256 ?? "No certificate pin for this dependency"}");
  if(profile.Msix is not null)text.AppendLine("MSIX identity: "+JsonSerializer.Serialize(profile.Msix));
  if(profile.Office is not null)text.AppendLine("Office product, channel, language and licensing/EULA authorization: "+JsonSerializer.Serialize(profile.Office));
  text.AppendLine("\nFile hashes, signer trust and installer metadata are independently checked against actual media. Inventory does not prove licensing or sign-in. No command line or installer argument overrides are accepted."); return text.ToString();
 }
 Task ShowSchemaAsync()
 {
  var text = "Provide a proposed deployment profile JSON using the exact fields below (camelCase, no extra/duplicate fields, <=256 KiB). Enter an empty id to compute its content SHA-256; a supplied id must match the complete profile with id empty. All files in the source folder must appear once in files; relative paths must be safe. Use actual vendor-reviewed metadata and independently obtained hashes/certificate pins. Use tools/New-DeploymentProfile.ps1 to create a draft from supported local MSI or OneDrive media, or supply the reviewed format-specific JSON. A draft is not approval.\n\n" +
   "{\n  \"id\": \"\",\n  \"controlId\": \"APP-WIN-001\",\n  \"format\": \"MachineMsi\",\n  \"productIdentity\": \"{EXACT-MSI-PRODUCT-CODE-GUID}\",\n  \"version\": \"1.2.3\",\n  \"productName\": \"Vendor product name\",\n  \"publisher\": \"Vendor publisher\",\n  \"scope\": \"Machine\",\n  \"architecture\": \"x64\",\n  \"entryPoint\": \"setup.msi\",\n  \"files\": [{\"relativePath\":\"setup.msi\",\"sha256\":\"64 hex characters\",\"signerCertificateSha256\":\"64 hex characters\"}],\n  \"timeout\": \"00:20:00\"\n}\n\nOfficeOdt requires Microsoft Office Deployment Tool, exact target Office version, full offline Office media and office={productId:O365BusinessRetail or O365ProPlusRetail,channel:MonthlyEnterprise,language:en-gb,licenseAuthorized:true}. No activation or license keys are supplied. CurrentUserMsix uses scope CurrentUser and msix={name,publisher,familyName}; family must derive from the exact publisher/name, with signed package dependencies. Both optional fields must be null for other formats. OneDriveMachineExe is the only standalone vendor EXE format. It requires control APP-WIN-005, productIdentity Microsoft.OneDrive, publisher Microsoft Corporation, and one pinned signed setup.exe. Timeout must be greater than zero and at most one hour. Profile import requests administrator review and writes configuration only; installation is a separate action.";
  new Window { Owner = this, Title = "Required proposed profile JSON schema", Width = 760, Height = 660, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(20) } }.ShowDialog();
  return Task.CompletedTask;
 }
 sealed class ProfileReviewWindow : Window
 {
  public ProfileReviewWindow(DeploymentProfile profile, string directory)
  {
   Title = "Review full proposed deployment profile"; Width = 800; Height = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner;
   var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
   var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
   var confirmed = new CheckBox { Content = new TextBlock { Text = "I reviewed client authorization, licensing, effective GPO/MDM policy, vendor guidance, product metadata, complete file hashes, signing certificate pins and source folder. Request administrator approval of this profile; no software is installed by import.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 10, 0, 10) }; bottom.Children.Add(confirmed);
   var buttons = new WrapPanel(); bottom.Children.Add(buttons); var approve = new System.Windows.Controls.Button { Content = "Request administrator approval", IsEnabled = false, Padding = new Thickness(10) }; buttons.Children.Add(approve); buttons.Children.Add(new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(10) });
   confirmed.Checked += (_, _) => approve.IsEnabled = true; confirmed.Unchecked += (_, _) => approve.IsEnabled = false; approve.Click += (_, _) => DialogResult = true;
   panel.Children.Add(new TextBox { Text = "Source folder: " + directory + "\n\n" + Describe(profile), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
  }
 }
}
