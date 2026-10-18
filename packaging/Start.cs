// Tiny windowed launcher targeting Windows' built-in .NET Framework.
// Runtime detection, verified installation and progress remain in Start.ps1.
using System;
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
    if(args.Length!=2||(args[0]!="--smoke-output"&&args[0]!="--startup-preview")||args[1].IndexOf('"')>=0||args[1].IndexOf('\r')>=0||args[1].IndexOf('\n')>=0)throw new ArgumentException("Start without arguments for normal use.");
    arguments+=(args[0]=="--smoke-output"?" -SmokeOutput ":" -PreviewOutput ")+"\""+Path.GetFullPath(args[1]).TrimEnd('\\')+"\"";
   }
   var start=new ProcessStartInfo(shell,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,WorkingDirectory=root};
   start.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules");
   using(var process=Process.Start(start)){
    if(process==null)throw new IOException("Windows could not start the toolkit.");
    var detail=new StringBuilder();var buffer=new char[512];int read;
    while((read=process.StandardError.Read(buffer,0,buffer.Length))>0){if(detail.Length<2048)detail.Append(buffer,0,Math.Min(read,2048-detail.Length));}
    process.WaitForExit();
    if(process.ExitCode!=0&&process.ExitCode!=2){
     var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalDeviceToolkit","logs");Directory.CreateDirectory(logs);
     var log=Path.Combine(logs,"startup-launcher-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".log");File.WriteAllText(log,DateTime.UtcNow.ToString("O")+Environment.NewLine+detail);
     var message="Startup was blocked before the window could open. Use your organisation's approved signed package or the self-contained offline edition. No execution policy was changed.\n\nDetails: "+log;
     if(args.Length==0)MessageBox(IntPtr.Zero,message,"Local Device Toolkit startup",0x10);else Console.Error.WriteLine(message);
    }
    return process.ExitCode;
   }
  }catch(Exception error){MessageBox(IntPtr.Zero,error.Message,"Local Device Toolkit startup",0x10);return 1;}
 }
}
