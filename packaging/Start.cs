// Tiny windowed launcher targeting Windows' built-in .NET Framework.
// Normal startup shows progress; --silent starts diagnostics without windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
class ToolkitStartup {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int MessageBox(IntPtr owner,string text,string title,uint flags);
 [STAThread] static int Main(string[] args){
  try{
   var root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
   var shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   var arguments="-NoLogo -NoProfile -STA -File \""+Path.Combine(root,"Start.ps1")+"\"";
   if(args.Length!=0){
    if(args[0]=="--silent"){
     if((args.Length!=3&&args.Length!=5&&args.Length!=7)||args[1]!="--preset"||(args[2]!="all"&&args[2]!="fast"))throw new ArgumentException("Use --silent --preset all|fast [--config <file>] [--inputs <file>].");
     arguments="-NonInteractive "+arguments+" -Silent -Preset "+args[2];
     var seen=new HashSet<string>(StringComparer.Ordinal);
     for(var i=3;i<args.Length;i+=2){
      if((args[i]!="--inputs"&&args[i]!="--config")||!seen.Add(args[i]))throw new ArgumentException("Unknown or duplicate silent option.");
      arguments+=(args[i]=="--inputs"?" -InputsPath ":" -ConfigPath ")+QuotePath(args[i+1]);
     }
    }else{
     if(args.Length!=2||(args[0]!="--smoke-output"&&args[0]!="--startup-preview"))throw new ArgumentException("Start without arguments for normal use, or use --silent --preset all|fast.");
     arguments+=(args[0]=="--smoke-output"?" -SmokeOutput ":" -PreviewOutput ")+QuotePath(args[1]);
    }
   }
   var start=new ProcessStartInfo(shell,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,WorkingDirectory=root};
   start.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");
   using(var process=Process.Start(start)){
    if(process==null)throw new IOException("Windows could not start the toolkit.");
    var detail=new StringBuilder();var buffer=new char[512];int read;
    while((read=process.StandardError.Read(buffer,0,buffer.Length))>0){if(detail.Length<2048)detail.Append(buffer,0,Math.Min(read,2048-detail.Length));}
    process.WaitForExit();
    // Diagnostic exits 2/3 are report outcomes, not startup failures.
    if(process.ExitCode!=0&&process.ExitCode!=2&&process.ExitCode!=3)ReportError(detail.ToString(),args.Length==0);
    return process.ExitCode;
   }
  }catch(Exception error){try{ReportError(error.Message,args.Length==0);}catch(Exception){}return error is ArgumentException?64:1;}
 }
 static string QuotePath(string value){
  if(string.IsNullOrWhiteSpace(value)||value.IndexOf('"')>=0)throw new ArgumentException("Invalid input path.");
  foreach(var c in value)if(char.IsControl(c))throw new ArgumentException("Invalid input path.");
  return "\""+Path.GetFullPath(value).TrimEnd('\\')+"\"";
 }
 static void ReportError(string detail,bool showWindow){
  var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","logs");Directory.CreateDirectory(logs);
  var log=Path.Combine(logs,"startup-launcher-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".log");
  using(var output=new StreamWriter(new FileStream(log,FileMode.CreateNew,FileAccess.Write,FileShare.Read)))output.WriteLine(DateTime.UtcNow.ToString("O")+Environment.NewLine+detail);
  var message="Toolkit startup failed. Use your organisation's approved signed package or the self-contained offline edition. No execution policy was changed.\n\nDetails: "+log;
  if(showWindow)MessageBox(IntPtr.Zero,message,"Local Device Toolkit startup",0x10);else Console.Error.WriteLine(message);
 }
}
