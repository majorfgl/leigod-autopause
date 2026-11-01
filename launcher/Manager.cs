using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly:AssemblyTitle("雷神自动暂停")]
[assembly:AssemblyDescription("非官方雷神内嵌自动暂停管理器")]

namespace LeigodAutoPause {
 public class State { public string Root; public string OriginalPackage; public string MetadataHash; public string Version; public string PackageRelative; public string NativeRelative; public string OriginalHash; public string Fingerprint; }
 static partial class Program {
  const string AsarHash="a6cc00e50861d9a0495b845a49e68075350e1ce746f32478acffe385ded74a5e";
  const string NativeHash="36a85a880f9389ccd0522a4715320b0a4a0781ded54f34628017d6d7e518163d";
  const string RendererHash="052efb7f8939913e42923482057ca950d87a45b69f77b21586485f11a0fe1138";
  const string PackageHash="a0009496aa724fc7680f50a3ae1b5ba7de3fc671e3c1b6ae2094581337fbb975";
  const string LegacyHash="c5702a8936c49755922e2a6774e574eaa62e9476e2ca7668af92abac86bd0f64";
  static readonly string Base=AppDomain.CurrentDomain.BaseDirectory;
  static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  static bool MakeShortcuts=true;
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(int access,bool inherit,int id);
  [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr handle,int flags,StringBuilder name,ref int size);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  static string ImagePath(Process p){IntPtr h=OpenProcess(0x1000,false,p.Id);if(h==IntPtr.Zero)return null;try{var b=new StringBuilder(2048);int size=b.Capacity;return QueryFullProcessImageName(h,0,b,ref size)?b.ToString():null;}finally{CloseHandle(h);}}
  static string Arg(string[] args,string key){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
  static string Hash(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
  static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static string Package(string root){return DiscoverMount(root).PackagePath;}
  static string PluginDir(string root){return Path.Combine(root,@"resources\leigod-auto-pause");}
  static string StateFile(string root){return Path.Combine(PluginDir(root),"installation.json");}
  static bool IsRoot(string root){return !String.IsNullOrEmpty(root)&&File.Exists(Path.Combine(root,"leigod.exe"))&&File.Exists(Path.Combine(root,@"resources\app.asar"));}
  static string Normalize(string root){if(!IsRoot(root))throw new Exception("请选择雷神安装目录，其中应包含 leigod.exe。");return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);}
  static State LoadState(string root){foreach(string file in new[]{StateFile(root),Path.Combine(CacheHome(root),"installation.json")})try{var state=Json.Deserialize<State>(File.ReadAllText(file,Encoding.UTF8));if(state!=null&&Same(state.Root,root))return state;}catch{}return null;}
  static void AtomicWrite(string file,byte[] bytes){string temp=file+".autopause.tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
  static bool Same(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);}
  static void ValidateClient(string root){ValidateStructure(root);}
  static Dictionary<string,string> Manifest(){
   var release=VerifyBundle(Base);if(release.version!=ToolVersion)throw new Exception("管理器与签名清单版本不同。");
   var files=new Dictionary<string,string>();
   foreach(var line in File.ReadAllLines(Path.Combine(Base,"plugin-hashes.txt"),Encoding.UTF8)){
    if(String.IsNullOrWhiteSpace(line))continue;string[] pair=line.Split('|');
    if(pair.Length!=2||!System.Text.RegularExpressions.Regex.IsMatch(pair[0],"^[a-z][a-z0-9.-]+\\.cjs$")||pair[1].Length!=64)throw new Exception("安装包清单格式有误。");
    files.Add(pair[0],pair[1].ToLowerInvariant());
   }
   foreach(string required in new[]{"bootstrap.cjs","config.cjs","core.cjs","main.cjs","renderer.cjs","transaction.cjs","shutdown.cjs","processes.cjs","adapter.cjs"})if(!files.ContainsKey(required))throw new Exception("安装包缺少 "+required);
   foreach(var f in files)if(Hash(Path.Combine(Base,"plugin",f.Key))!=f.Value)throw new Exception("安装包文件校验失败："+f.Key);
   return files;
  }
  static void StopClient(string root){
   var targets=new List<Process>();
   foreach(string name in new[]{"leigod","leishenSdk"})foreach(var p in Process.GetProcessesByName(name)){
    string file=ImagePath(p);if(file!=null&&file.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))targets.Add(p);
   }
   if(targets.Count>0&&Process.GetProcessesByName("TslGame").Length>0)throw new Exception("PUBG 正在运行，请退出游戏后再安装或卸载。");
   foreach(var p in targets)try{if(!p.HasExited){p.Kill();p.WaitForExit(5000);}}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){if(!p.HasExited)throw new Exception("无法结束雷神进程，请退出客户端后再试。");}
   System.Threading.Thread.Sleep(1000);
   foreach(var p in Process.GetProcessesByName("leigod")){string file=ImagePath(p);if(file!=null&&file.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("雷神仍在运行，请关闭客户端后再试。");}
  }
  static void CopyChecked(string source,string dest){if(Same(source,dest))return;Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(source,dest,true);if(Hash(source)!=Hash(dest))throw new Exception("复制后校验失败："+Path.GetFileName(dest));}
  static void Shortcut(string root){
   Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);
   string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   string file=Path.Combine(desktop,"雷神自动暂停.lnk");
   object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});Type lt=link.GetType();
   lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(ActiveBundle(root)??PluginDir(root),"雷神自动暂停.exe")});
   lt.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--launch --path \""+root+"\""});
   lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{root});
   lt.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(ActiveBundle(root)??PluginDir(root),"雷神自动暂停.exe")+",0"});
   lt.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"雷神内嵌自动暂停；点击顶部状态按钮设置"});
   lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
  }
  static void Install(string input,bool launch){
   string root=Normalize(input);WithOperation(root,()=>{if((InWatch||AutomaticInstall)&&File.Exists(Path.Combine(CacheHome(root),"disabled")))throw new Exception("自动维护已停止。");InstallCore(root,launch);});
  }
  static void InstallCore(string input,bool launch){
   string root=Normalize(input);ValidateClient(root);Manifest();if(Quarantined(root,Base))throw new Exception("本客户端上的自检已失败，等待文件变化或新版工具。");string previousBundle=InstalledBundle(root);
   var mount=DiscoverMount(root);var profile=Profile(root);string package=mount.PackagePath;byte[] original=mount.Original,beforeMetadata=File.ReadAllBytes(package);
   string cached=MaintenanceEnabled?CacheBundle(root):null;StopClient(root);string dir=PluginDir(root);RejectLink(dir);Directory.CreateDirectory(dir);
   var release=VerifyBundle(Base);var rollback=new Dictionary<string,byte[]>();
   var targets=release.files.Select(f=>Within(dir,f.path)).Concat(new[]{Path.Combine(dir,"release.json"),Path.Combine(dir,"release.sig"),Path.Combine(dir,"client-profile.json"),StateFile(root)}).ToArray();
   foreach(string target in targets)rollback[target]=File.Exists(target)?File.ReadAllBytes(target):null;
   var state=new State{Root=root,OriginalPackage=Convert.ToBase64String(original),OriginalHash=mount.OriginalHash,Version=ToolVersion,PackageRelative=profile.packageRelative,NativeRelative=profile.nativeRelative,Fingerprint=profile.fingerprint};
   try{
    foreach(var f in release.files)CopyChecked(Within(Base,f.path),Within(dir,f.path));foreach(string name in new[]{"release.json","release.sig"})CopyChecked(Path.Combine(Base,name),Path.Combine(dir,name));WriteProfile(root,profile);
    var meta=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(original));meta["main"]=Path.Combine(dir,@"plugin\bootstrap.cjs");byte[] bytes=new UTF8Encoding(false).GetBytes(Json.Serialize(meta));AtomicWrite(package,bytes);state.MetadataHash=Hash(bytes);File.WriteAllText(StateFile(root),Json.Serialize(state),new UTF8Encoding(false));
    if(ProfileChanged(root))throw new Exception("安装期间客户端文件变化，本次挂载已撤回。");
   }catch{AtomicWrite(package,beforeMetadata);foreach(var f in rollback){try{if(f.Value==null){if(File.Exists(f.Key))File.Delete(f.Key);}else AtomicWrite(f.Key,f.Value);}catch{}}if(launch)Launch(root);throw;}
   if(MaintenanceEnabled){RecordActivation(root,previousBundle,original);ActivateMaintenance(root,cached);}if(MakeShortcuts)Shortcut(root);if(launch)Launch(root);
  }
  static void Uninstall(string input,bool launch){
   string root=Normalize(input);DisableMaintenance(root);WithOperation(root,()=>UninstallCore(root,launch));
  }
  static void UninstallCore(string input,bool launch){
   string root=Normalize(input);State state=LoadState(root);if(state==null)throw new Exception("没有找到此工具的安装记录。");
   string package=String.IsNullOrEmpty(state.PackageRelative)?Package(root):Within(root,state.PackageRelative);if(Hash(package)!=state.MetadataHash)throw new Exception("客户端模块已变化，为避免覆盖更新后的文件，卸载已停止。");
   byte[] original=Convert.FromBase64String(state.OriginalPackage);if(Hash(original)!=(state.OriginalHash??PackageHash))throw new Exception("备份校验失败。");
   DisableMaintenance(root);
   StopClient(root);AtomicWrite(package,original);
   state.MetadataHash=Hash(original);state.Version="uninstalled";File.WriteAllText(StateFile(root),Json.Serialize(state),new UTF8Encoding(false));
   if(Directory.Exists(CacheHome(root)))AtomicWrite(Path.Combine(CacheHome(root),"installation.json"),Encoding.UTF8.GetBytes(Json.Serialize(state)));
   // Retain inert files and backups so a running manager never deletes itself.
   Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"雷神自动暂停.lnk");
   if(File.Exists(shortcut)){object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});Type lt=link.GetType();string target=Convert.ToString(lt.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));if(Same(target,Path.Combine(PluginDir(root),"雷神自动暂停.exe"))||target.StartsWith(CacheHome(root)+"\\",StringComparison.OrdinalIgnoreCase)){lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(root,"leigod_launcher.exe")});lt.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{""});lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}}
   if(launch)Launch(root);
  }
  static void Launch(string input){string root=Normalize(input);if(MaintenanceEnabled&&!ClientRunning(root)&&LoadState(root)!=null&&LoadState(root).Version==ToolVersion&&!Quarantined(root,Base)){EnsureHome(root);var profile=Profile(root);AtomicWrite(Path.Combine(CacheHome(root),"launch-trial.json"),Encoding.UTF8.GetBytes(Json.Serialize(new LaunchTrial{startedAt=DateTime.UtcNow.ToString("o"),releaseId=Hash(Path.Combine(Base,"release.json")),fingerprint=profile.fingerprint})));}Process.Start(new ProcessStartInfo(Path.Combine(root,"leigod_launcher.exe")){WorkingDirectory=root,WindowStyle=ProcessWindowStyle.Hidden,UseShellExecute=false,CreateNoWindow=true});}
  static void LaunchChecked(string root){
   try{State state=LoadState(root);if(state!=null&&state.Version!="uninstalled"){if(MaintenanceEnabled)StartGuardian(root,ActiveBundle(root)??Base);if(Quarantined(root,Base))throw new Exception("此适配包未通过启动自检，已停用自动挂载，请等待新版。");ValidateClient(root);Manifest();if(NeedsRepair(root)){if(!Settings(root).autoRepair)throw new Exception("自动修复已关闭，请在管理器中手动修复。");if(ClientRunning(root))throw new Exception("检测到插件需要修复，请退出雷神后重新启动；后台维护也会在退出后处理。");Install(root,false);}}Launch(root);}
   catch(Exception e){Launch(root);MessageBox.Show("已尝试启动雷神，但自动暂停未能启用。\r\n\r\n"+e.Message,"自动暂停需要检查",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  }
  static string Discover(){
   foreach(var p in Process.GetProcessesByName("leigod"))try{string image=ImagePath(p);if(image==null)continue;string root=Path.GetDirectoryName(image);if(IsRoot(root))return root;}catch{}
   string installedParent=Path.GetFullPath(Path.Combine(Base,"..",".."));if(IsRoot(installedParent))return installedParent;
   var candidates=new List<string>();
   foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})try{
    using(var key=RegistryKey.OpenBaseKey(hive,view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))if(key!=null)foreach(string name in key.GetSubKeyNames())using(var item=key.OpenSubKey(name)){
     string display=Convert.ToString(item.GetValue("DisplayName"));if(display.Contains("雷神")||display.IndexOf("leigod",StringComparison.OrdinalIgnoreCase)>=0){string root=Convert.ToString(item.GetValue("InstallLocation"));if(IsRoot(root))candidates.Add(root);string icon=Convert.ToString(item.GetValue("DisplayIcon")).Trim('"');int comma=icon.LastIndexOf(',');if(comma>=0)icon=icon.Substring(0,comma).Trim('"');if(File.Exists(icon)&&IsRoot(Path.GetDirectoryName(icon)))candidates.Add(Path.GetDirectoryName(icon));}
    }
   }catch{}
   foreach(string baseDir in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}){string p=Path.Combine(baseDir,"LeiGod_Acc");if(IsRoot(p))candidates.Add(p);}
   var unique=candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return unique.Length==1?unique[0]:"";
  }
  static void Report(string root,string output){var result=new Dictionary<string,object>();result["toolVersion"]=ToolVersion;result["clientFound"]=IsRoot(root);if(IsRoot(root)){result["clientVersion"]=File.ReadAllText(Path.Combine(root,"version_f.txt")).Trim();result["asarOriginal"]=Hash(Path.Combine(root,@"resources\app.asar"))==AsarHash;var state=LoadState(root);result["metadataInstalled"]=state!=null&&state.Version!="uninstalled"&&Hash(Package(root))==state.MetadataHash;}File.WriteAllText(output,Json.Serialize(result),new UTF8Encoding(false));}
  [STAThread]static int Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   MakeShortcuts=!args.Contains("--no-shortcut");
   MaintenanceEnabled=!args.Contains("--no-maintenance");
   MaintenanceNotices=!args.Contains("--no-maintenance-notices");
   AutomaticInstall=args.Contains("--automatic-update");
   Environment.SetEnvironmentVariable("LEIGOD_AUTOPAUSE_PREVIEW",args.Contains("--preview")?"1":null);
   try{
    string root=Arg(args,"--path")??Discover();
    if(args.Contains("--verify-bundle")){VerifyBundle(Arg(args,"--bundle")??Base);return 0;}
    if(args.Contains("--maintenance-self-test")){MaintenanceSelfTest(args);return 0;}
    if(args.Contains("--watch")){root=WatchRoot(root);if(!File.Exists(Path.Combine(CacheHome(root),"disabled")))Watch(root);return 0;}
    if(args.Contains("--maintenance")){Application.Run(new MaintenanceForm(root));return 0;}
    if(args.Contains("--check-updates")){CheckUpdates(Normalize(root));return 0;}
    if(args.Contains("--queue-install")){QueueInstall(root);return 0;}
    if(args.Contains("--apply-pending")){ApplyPending(Normalize(root));return 0;}
    if(args.Contains("--import-update")){root=Normalize(root);QueueBundle(root,StageZip(root,File.ReadAllBytes(Arg(args,"--import-update"))));return 0;}
    if(args.Contains("--self-test")){Manifest();if(Hash(Path.Combine(Base,"original-package.json"))!=PackageHash)throw new Exception("参考元数据校验失败。");return 0;}
    if(args.Contains("--diagnose")){Report(root,Arg(args,"--output")??Path.Combine(Base,"diagnostics.json"));return 0;}
    if(args.Contains("--probe-structure")){File.WriteAllText(Arg(args,"--output"),Json.Serialize(Profile(Normalize(root))),new UTF8Encoding(false));return 0;}
    if(args.Contains("--render")){using(var form=new ManagerForm(root)){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Arg(args,"--output"),System.Drawing.Imaging.ImageFormat.Png);}form.Close();}return 0;}
    if(args.Contains("--install")){Install(root,!args.Contains("--no-launch"));return 0;}
    if(args.Contains("--uninstall")){Uninstall(root,!args.Contains("--no-launch"));return 0;}
    if(args.Contains("--launch")){LaunchChecked(root);return 0;}
    Application.Run(new ManagerForm(root));return 0;
   }catch(Exception e){if(args.Contains("--quiet")){File.WriteAllText(Path.Combine(Base,"operation-error.txt"),e.Message,Encoding.UTF8);}else MessageBox.Show(e.Message,"雷神自动暂停",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
  class ManagerForm:Form{
   TextBox location;Label status;Button install,launch,remove;
   public ManagerForm(string root){Text="雷神自动暂停 · 管理器 "+ToolVersion;ClientSize=new Size(580,350);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(35,35,38);ForeColor=Color.Gainsboro;Font=new Font("Microsoft YaHei UI",10);
    Controls.Add(new Label{Text="雷神自动暂停",Location=new Point(24,22),AutoSize=true,Font=new Font(Font.FontFamily,15,FontStyle.Bold)});
    Controls.Add(new Label{Text="共用雷神登录 · 游戏退出等待可调 · 不修改原始 ASAR",Location=new Point(25,63),AutoSize=true,ForeColor=Color.Silver});
    location=new TextBox{Text=root,Location=new Point(25,104),Size=new Size(437,28)};Controls.Add(location);
    var browse=new Button{Text="选择…",Location=new Point(474,102),Size=new Size(80,31)};browse.Click+=(s,e)=>{using(var d=new OpenFileDialog{Title="选择雷神安装目录中的 leigod.exe",Filter="雷神客户端|leigod.exe"})if(d.ShowDialog()==DialogResult.OK){location.Text=Path.GetDirectoryName(d.FileName);RefreshStatus();}};Controls.Add(browse);
    status=new Label{Location=new Point(25,147),Size=new Size(530,42),ForeColor=Color.FromArgb(224,180,106)};Controls.Add(status);
    install=new Button{Text="安装 / 修复",Location=new Point(25,201),Size=new Size(166,39)};launch=new Button{Text="启动雷神",Location=new Point(204,201),Size=new Size(166,39)};remove=new Button{Text="卸载插件",Location=new Point(383,201),Size=new Size(171,39)};
    install.Click+=(s,e)=>Action(()=>{if(MessageBox.Show("安装需要重启雷神，请先结束游戏及加速。继续安装？","安装自动暂停",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)==DialogResult.OK)Install(location.Text,true);});
    launch.Click+=(s,e)=>Action(()=>LaunchChecked(location.Text));remove.Click+=(s,e)=>Action(()=>{if(MessageBox.Show("将恢复原始模块入口并重启雷神，继续？","卸载自动暂停",MessageBoxButtons.OKCancel)==DialogResult.OK)Uninstall(location.Text,true);});Controls.Add(install);Controls.Add(launch);Controls.Add(remove);
    foreach(var b in new[]{browse,install,launch,remove}){b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(64,64,69);b.ForeColor=Color.White;b.FlatAppearance.BorderColor=Color.FromArgb(85,85,90);}location.BackColor=Color.FromArgb(24,24,26);location.ForeColor=Color.Gainsboro;
    var maintenance=new Button{Text="自动检测与修复 / 更新源 / 导入适配包",Location=new Point(25,253),Size=new Size(529,34)};maintenance.Click+=(s,e)=>Action(()=>{using(var f=new MaintenanceForm(location.Text))f.ShowDialog(this);});Controls.Add(maintenance);
    Controls.Add(new Label{Text="按结构与运行功能自检 · 小更新自动重新探测",Location=new Point(25,310),AutoSize=true,ForeColor=Color.Gray,Font=new Font(Font.FontFamily,9)});RefreshStatus();
   }
   void Action(Action action){try{UseWaitCursor=true;install.Enabled=launch.Enabled=remove.Enabled=false;action();RefreshStatus();}catch(Exception e){MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{UseWaitCursor=false;install.Enabled=launch.Enabled=remove.Enabled=true;}}
   void RefreshStatus(){try{string root=Normalize(location.Text);ValidateClient(root);State state=LoadState(root);status.Text=state!=null&&state.Version!="uninstalled"&&Hash(Package(root))==state.MetadataHash?(state.Version==ToolVersion?"已安装。打开雷神后点击搜索框右侧的自动监控按钮设置。":"本机已安装 "+state.Version+"，退出游戏后可升级到 "+ToolVersion+"。"):"客户端结构可识别，安装后执行运行功能自检。";}catch(Exception e){status.Text=e.Message;}}
  }
 }
}
