using LocalDeviceToolkit.Windows.Deployments;
using System.Security.AccessControl;
using Xunit;
namespace LocalDeviceToolkit.Windows.Tests;
public sealed class ProfileAuthorityTests
{
 [Theory][InlineData("S-1-5-32-545",true,FileSystemRights.WriteData)][InlineData("S-1-5-21-123",true,FileSystemRights.Delete)][InlineData("S-1-5-32-544",false,FileSystemRights.Read)]
 public void Untrusted_writers_and_inherited_access_lists_are_refused(string sid,bool isolated,FileSystemRights rights)
 {Assert.Throws<UnauthorizedAccessException>(()=>ApprovedProfileAuthority.ValidateRules("S-1-5-32-544",isolated,[(sid,AccessControlType.Allow,rights)]));}
 [Fact] public void Ordinary_engineers_may_read_but_cannot_authorize_profile_changes()
 {ApprovedProfileAuthority.ValidateRules("S-1-5-32-544",true,[("S-1-5-32-545",AccessControlType.Allow,FileSystemRights.ReadAndExecute),("S-1-5-32-544",AccessControlType.Allow,FileSystemRights.FullControl),("S-1-5-18",AccessControlType.Allow,FileSystemRights.FullControl)]);}
 [Fact] public void User_owned_profile_directory_is_not_an_authority_even_if_currently_read_only()
 {Assert.Throws<UnauthorizedAccessException>(()=>ApprovedProfileAuthority.ValidateRules("S-1-5-21-123",true,[]));}
}
