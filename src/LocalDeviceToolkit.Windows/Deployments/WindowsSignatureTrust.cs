using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace LocalDeviceToolkit.Windows.Deployments;
public sealed class WindowsSignatureTrust:IPackageSignatureTrust
{
 public SignatureTrust Verify(string path,string expectedCertificateSha256)
 {
  if(!OperatingSystem.IsWindows())return new(false,"Windows trust APIs are unavailable on this host.");
  var file=new TrustFile {Size=(uint)Marshal.SizeOf<TrustFile>(),Path=path};var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
  try
  {
   Marshal.StructureToPtr(file,pointer,false);
   var data=new TrustData {Size=(uint)Marshal.SizeOf<TrustData>(),UiChoice=2,RevocationChecks=1,UnionChoice=1,File=pointer,StateAction=1,ProviderFlags=0x80};
   var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   try
   {
    int status=WinVerifyTrust(new IntPtr(-1),ref action,ref data);
    if(status!=0)return new(false,"WinVerifyTrust returned 0x"+status.ToString("X8")+" (revocation checking enabled).");
    // Read the primary signer actually accepted by the SIP/trust provider. This also supports SIP-signed MSI files;
    // PE-only embedded-certificate extraction would not establish which signer Windows accepted.
    var provider=WTHelperProvDataFromStateData(data.StateData);if(provider==IntPtr.Zero)return new(false,"Windows did not expose the verified trust provider state.");
    var signerPointer=WTHelperGetProvSignerFromChain(provider,0,false,0);if(signerPointer==IntPtr.Zero)return new(false,"Windows did not expose the verified primary signer.");
    var signer=Marshal.PtrToStructure<ProviderSignerPrefix>(signerPointer);
    if(signer.CertificateCount==0||signer.CertificateChain==IntPtr.Zero)return new(false,"The verified signer has no certificate chain.");
    var certPointer=Marshal.PtrToStructure<ProviderCertificatePrefix>(signer.CertificateChain).Certificate;
    if(certPointer==IntPtr.Zero)return new(false,"The verified signer certificate is missing.");
    var certificate=Marshal.PtrToStructure<CertificateContextPrefix>(certPointer);
    if(certificate.Encoded==IntPtr.Zero||certificate.EncodedSize is <1 or >1048576)return new(false,"The verified signer certificate is invalid.");
    var bytes=new byte[certificate.EncodedSize];Marshal.Copy(certificate.Encoded,bytes,0,bytes.Length);
    var actual=Convert.ToHexString(SHA256.HashData(bytes));
    using var signerCertificate=X509CertificateLoader.LoadCertificate(bytes);
    var organizations=signerCertificate.SubjectName.EnumerateRelativeDistinguishedNames().Where(r=>!r.HasMultipleElements&&r.GetSingleElementType().Value=="2.5.4.10").Select(r=>r.GetSingleElementValue()).ToArray();var organization=organizations.Length==1?organizations[0]:null;
    return actual.Equals(expectedCertificateSha256,StringComparison.OrdinalIgnoreCase)?new(true,"Windows Authenticode chain and verified primary signer certificate SHA-256 match the approved pin.",organization):new(false,"The Windows-verified signer certificate differs from the approved profile pin.");
   }
   finally{data.StateAction=2;_ = WinVerifyTrust(new IntPtr(-1),ref action,ref data);}
  }
  catch(Exception error) when(error is CryptographicException or IOException or UnauthorizedAccessException){return new(false,"Cannot verify the signed package: "+error.GetType().Name);}
  finally{Marshal.DestroyStructure<TrustFile>(pointer);Marshal.FreeHGlobal(pointer);}
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct TrustFile {public uint Size;[MarshalAs(UnmanagedType.LPWStr)]public string Path;public IntPtr File,KnownSubject;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct TrustData {public uint Size;public IntPtr PolicyCallback,SipClient;public uint UiChoice,RevocationChecks,UnionChoice;public IntPtr File;public uint StateAction;public IntPtr StateData,UrlReference;public uint ProviderFlags,UiContext;public IntPtr SignatureSettings;}
 // Only the documented leading fields are read. The trust provider owns all referenced memory until WTD_STATEACTION_CLOSE.
 [StructLayout(LayoutKind.Sequential)]struct ProviderSignerPrefix {public uint Size;public System.Runtime.InteropServices.ComTypes.FILETIME VerifyAsOf;public uint CertificateCount;public IntPtr CertificateChain;}
 [StructLayout(LayoutKind.Sequential)]struct ProviderCertificatePrefix {public uint Size;public IntPtr Certificate;}
 [StructLayout(LayoutKind.Sequential)]struct CertificateContextPrefix {public uint Encoding;public IntPtr Encoded;public uint EncodedSize;}
 [DllImport("wintrust.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
 [DllImport("wintrust.dll",ExactSpelling=true)]static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
 [DllImport("wintrust.dll",ExactSpelling=true)]static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider,uint signer,[MarshalAs(UnmanagedType.Bool)]bool counterSigner,uint counterSignerIndex);
}
