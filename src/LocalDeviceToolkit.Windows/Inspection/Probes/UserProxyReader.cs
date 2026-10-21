using System.Runtime.InteropServices;
namespace LocalDeviceToolkit.Windows.Inspection.Probes;
public static class ProxySanitizer
{
 public static string Sanitize(string? raw)
 {
  if(string.IsNullOrWhiteSpace(raw))return "Not configured";
  if(raw.Length>4096||!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))return "Unrecognized PAC address (not collected)";
  return new UriBuilder(uri){UserName="",Password="",Query="",Fragment=""}.Uri.AbsoluteUri;
 }
 public static string ProxyList(string? raw)
 {
  if(string.IsNullOrWhiteSpace(raw))return "Not configured";
  if(raw.Length>4096)return "Unrecognized proxy configuration";
  return string.Join("; ",raw.Split(';').Select(value=>
  {
   var clean=value.Trim();var at=clean.LastIndexOf('@');if(at>=0)clean=clean[(at+1)..];
   return clean.Any(c=>char.IsControl(c)||c is '?' or '#' or '/' or '\\')?"Unrecognized proxy entry":clean;
  }));
 }
}
public sealed record ProxyConfiguration(string Proxy,string AutoDetect,string PacUrl,string Bypass);
public static class UserProxyReader
{
 public static ProxyConfiguration Read(bool currentUser)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  if(currentUser)
  {
   if(!WinHttpGetIEProxyConfigForCurrentUser(out var config))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try{return new(ProxySanitizer.ProxyList(Text(config.Proxy)),config.AutoDetect?"Enabled":"Disabled",ProxySanitizer.Sanitize(Text(config.Pac)),ProxySanitizer.ProxyList(Text(config.Bypass)));}
   finally{Free(config.Proxy);Free(config.Pac);Free(config.Bypass);}
  }
  if(!WinHttpGetDefaultProxyConfiguration(out var system))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  try{return new(ProxySanitizer.ProxyList(Text(system.Proxy)),"Not evaluated","Not evaluated",ProxySanitizer.ProxyList(Text(system.Bypass)));}
  finally{Free(system.Proxy);Free(system.Bypass);}
 }
 static string? Text(IntPtr ptr)=>ptr==IntPtr.Zero?null:Marshal.PtrToStringUni(ptr);
 static void Free(IntPtr ptr){if(ptr!=IntPtr.Zero)GlobalFree(ptr);}
 [StructLayout(LayoutKind.Sequential)]struct UserConfig{[MarshalAs(UnmanagedType.Bool)]public bool AutoDetect;public IntPtr Pac;public IntPtr Proxy;public IntPtr Bypass;}
 [StructLayout(LayoutKind.Sequential)]struct SystemConfig{public uint AccessType;public IntPtr Proxy;public IntPtr Bypass;}
 [DllImport("winhttp.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool WinHttpGetIEProxyConfigForCurrentUser(out UserConfig config);
 [DllImport("winhttp.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool WinHttpGetDefaultProxyConfiguration(out SystemConfig config);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr memory);
}
