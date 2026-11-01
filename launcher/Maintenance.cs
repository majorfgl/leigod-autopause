using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.IO.Compression;

namespace LeigodAutoPause {
 public class ReleaseFile { public string path; public string sha256; public long bytes; }
 public class Release { public int schema; public string version; public string compatibilityMode; public string clientVersion; public string asarSha256; public string rendererSha256; public string nativeSha256; public string nativePath; public ReleaseFile[] files; }
 public class FeedItem { public string version; public string url; public string sha256; public long bytes; public string compatibilityMode; public string clientVersion; public string asarSha256; public string rendererSha256; public string nativeSha256; public string nativePath; }
 public class Feed { public int schema; public FeedItem[] releases; }
 public class Envelope { public string payload; public string signature; }
 public class PluginHealth { public int pid; public string version; public string releaseId; public bool healthy; public string updatedAt; public string fingerprint; public bool pauseVerified; }
 public class Activation { public string originalMetadata; public string previousBundle; public string releaseId; }
 public class LaunchTrial { public string startedAt; public string releaseId; public string fingerprint; }
 public class MaintenanceSettings { public int schema; public bool autoRepair=true; public bool autoUpdate=true; public string feedUrl; }
 static partial class Program {
  static bool MaintenanceEnabled=true;
  static bool MaintenanceNotices=true;
  static bool InWatch=false,AutomaticInstall=false;
  static void WithOperation(string root,Action action){using(var mutex=new Mutex(false,MutexName(root)+"-operation")){bool owned=false;try{try{owned=mutex.WaitOne(15000);}catch(AbandonedMutexException){owned=true;}if(!owned)throw new Exception("另一项维护操作仍在进行，请稍后重试。");action();}finally{if(owned)mutex.ReleaseMutex();}}}
  static string RootId(string root){return Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd('\\').ToLowerInvariant())).Substring(0,16);}
  static string FixtureCacheRoot=null;
  static string CacheHome(string root){return DevelopmentBuild&&FixtureCacheRoot!=null?Path.Combine(FixtureCacheRoot,RootId(root)):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"LeigodAutoPause",RootId(root));}
  static string TaskName(string root){return "LeigodAutoPause-"+RootId(root);}
  static string MutexName(string root){return @"Local\LeigodAutoPause-"+RootId(root);}
  static string WatchRoot(string root){if(String.IsNullOrWhiteSpace(root)||!Path.IsPathRooted(root))throw new Exception("后台维护需要明确的安装目录。");return Path.GetFullPath(root).TrimEnd('\\','/');}
  static void RejectLink(string path){if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("维护文件不能是链接或重解析点。");}
  static void SecureDirectory(string folder){
   RejectLink(folder);Directory.CreateDirectory(folder);
   var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
   var inheritance=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
   foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,inheritance,PropagationFlags.None,AccessControlType.Allow));
   acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,inheritance,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(folder,acl);
  }
  static void EnsureHome(string root){var home=CacheHome(root);if(DevelopmentBuild&&FixtureCacheRoot!=null){Directory.CreateDirectory(home);return;}SecureDirectory(Path.GetDirectoryName(home));SecureDirectory(home);}
  static string Within(string folder,string relative){
   if(String.IsNullOrWhiteSpace(relative)||relative.Contains(":")||relative.Contains("\\")||relative.StartsWith("/")||relative.Split('/').Any(p=>p==".."||p=="."||String.IsNullOrWhiteSpace(p)||p.EndsWith(".")||p.EndsWith(" ")))throw new Exception("更新包包含不安全的路径。");
   string full=Path.GetFullPath(Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar))),prefix=Path.GetFullPath(folder).TrimEnd('\\')+"\\";
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("更新包路径越界。");
   string part=folder;foreach(string item in relative.Split('/')){part=Path.Combine(part,item);RejectLink(part);}return full;
  }
  static Version ReleaseVersion(string value){Version v;if(!System.Text.RegularExpressions.Regex.IsMatch(value??"",@"^\d+\.\d+\.\d+$")||!Version.TryParse(value,out v))throw new Exception("版本号格式无效。");return v;}
  static byte[] VerifySigned(byte[] bytes,string signature){
   if(bytes.Length>2*1024*1024)throw new Exception("签名清单过大。");
   using(var key=new RSACryptoServiceProvider(new CspParameters(24))){key.PersistKeyInCsp=false;using(var r=new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("LeigodAutoPause.ReleasePublicKey")))key.FromXmlString(r.ReadToEnd());if(!key.VerifyData(bytes,CryptoConfig.MapNameToOID("SHA256"),Convert.FromBase64String(signature)))throw new Exception("更新包签名无效，已拒绝更新。");}return bytes;
  }
  static Release VerifyBundle(string folder,bool strict=false){
   byte[] bytes=File.ReadAllBytes(Path.Combine(folder,"release.json"));VerifySigned(bytes,File.ReadAllText(Path.Combine(folder,"release.sig")).Trim());
   var release=Json.Deserialize<Release>(Encoding.UTF8.GetString(bytes));if(release.schema!=1||release.files==null||release.files.Length<10||release.files.Length>200)throw new Exception("更新包清单格式无效。");ReleaseVersion(release.version);
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
   foreach(var f in release.files){if(!seen.Add(f.path)||f.bytes<0||f.bytes>16*1024*1024||!System.Text.RegularExpressions.Regex.IsMatch(f.sha256??"",@"^[0-9a-f]{64}$"))throw new Exception("更新包文件记录无效。");string file=Within(folder,f.path);if(!File.Exists(file)||new FileInfo(file).Length!=f.bytes||Hash(file)!=f.sha256)throw new Exception("更新包文件校验失败："+f.path);total+=f.bytes;}
   if(total>128*1024*1024||!seen.Contains("雷神自动暂停.exe")||!seen.Contains("plugin/bootstrap.cjs")||!seen.Contains("original-package.json"))throw new Exception("更新包缺少必要文件。");
   if(strict)foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories)){RejectLink(file);string relative=file.Substring(Path.GetFullPath(folder).TrimEnd('\\').Length+1).Replace('\\','/');if(relative!="release.json"&&relative!="release.sig"&&!seen.Contains(relative))throw new Exception("更新包存在未签名文件："+relative);}
   return release;
  }
  static string CacheBundle(string root){
   var release=VerifyBundle(Base);EnsureHome(root);string folder=Path.Combine(CacheHome(root),"versions",release.version+"-"+Hash(Path.Combine(Base,"release.json")).Substring(0,12));Directory.CreateDirectory(Path.GetDirectoryName(folder));RejectLink(Path.GetDirectoryName(folder));
   if(Same(folder,Base))return folder;
   if(Directory.Exists(folder)){try{VerifyBundle(folder);if(Hash(Path.Combine(folder,"release.json"))==Hash(Path.Combine(Base,"release.json")))return folder;throw new Exception("同版本更新包内容不同，拒绝覆盖。");}catch(FileNotFoundException){}}
   Directory.CreateDirectory(folder);RejectLink(folder);
   foreach(var f in release.files)CopyChecked(Within(Base,f.path),Within(folder,f.path));foreach(string name in new[]{"release.json","release.sig"})CopyChecked(Path.Combine(Base,name),Path.Combine(folder,name));VerifyBundle(folder);return folder;
  }
  static string ActiveBundle(string root){try{string folder=File.ReadAllText(Path.Combine(CacheHome(root),"active.txt")).Trim();if(!folder.StartsWith(CacheHome(root)+"\\versions\\",StringComparison.OrdinalIgnoreCase))return null;return folder;}catch{return null;}}
  static string InstalledBundle(string root){try{string active=ActiveBundle(root);if(active==null)return null;string digest=Hash(Path.Combine(active,"release.json"));string installed=Path.Combine(PluginDir(root),"release.json");if(File.Exists(installed)&&Hash(installed)==digest)return active;var record=Json.Deserialize<Activation>(File.ReadAllText(Path.Combine(CacheHome(root),"activation.json")));if(record.releaseId==digest&&record.previousBundle!=null&&record.previousBundle.StartsWith(CacheHome(root)+"\\versions\\",StringComparison.OrdinalIgnoreCase))return record.previousBundle;}catch{}return null;}
  static MaintenanceSettings Settings(string root){MaintenanceSettings settings=null;try{settings=Json.Deserialize<MaintenanceSettings>(File.ReadAllText(Path.Combine(CacheHome(root),"settings.json")));}catch{}if(settings==null)settings=new MaintenanceSettings();if(settings.schema<1){if(String.IsNullOrWhiteSpace(settings.feedUrl))settings.feedUrl=DefaultFeedUrl;settings.schema=1;}return settings;}
  static void SaveSettings(string root,MaintenanceSettings value){if(!String.IsNullOrWhiteSpace(value.feedUrl))Https(value.feedUrl);value.schema=1;EnsureHome(root);AtomicWrite(Path.Combine(CacheHome(root),"settings.json"),Encoding.UTF8.GetBytes(Json.Serialize(value)));}
  static void Status(string root,string phase,string message){try{EnsureHome(root);AtomicWrite(Path.Combine(CacheHome(root),"status.json"),Encoding.UTF8.GetBytes(Json.Serialize(new{phase,message,checkedAt=DateTime.UtcNow.ToString("o"),toolVersion=ToolVersion,feedConfigured=!String.IsNullOrWhiteSpace(Settings(root).feedUrl)})));}catch{}}
  static bool ClientRunning(string root){return Process.GetProcessesByName("leigod").Any(p=>{try{var image=ImagePath(p);return image!=null&&image.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase);}catch{return true;}});}
  static string ClientStamp(string root){var mount=DiscoverMount(root);return (File.Exists(Path.Combine(root,"version_f.txt"))?File.ReadAllText(Path.Combine(root,"version_f.txt")):"unknown")+String.Join("|",new[]{Path.Combine(root,"resources/app.asar"),Path.Combine(root,"resources/renderer.asar"),mount.NativePath}.Select(p=>{var f=new FileInfo(p);return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"missing";}));}
  static bool NeedsRepair(string root){var state=LoadState(root);if(state==null||state.Version=="uninstalled")return false;var mount=DiscoverMount(root);var metadata=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(mount.PackagePath));if(Hash(mount.PackagePath)!=state.MetadataHash||state.Version!=ToolVersion||!Same(Convert.ToString(metadata["main"]),Path.Combine(PluginDir(root),"plugin/bootstrap.cjs"))||ProfileChanged(root))return true;foreach(var f in Manifest()){string target=Path.Combine(PluginDir(root),"plugin",f.Key);if(!File.Exists(target)||Hash(target)!=f.Value)return true;}foreach(var name in new[]{"雷神自动暂停.exe","release.json","release.sig"}){string target=Path.Combine(PluginDir(root),name);if(!File.Exists(target)||Hash(target)!=Hash(Path.Combine(Base,name)))return true;}return false;}
  static void StartGuardian(string root,string folder){
   if(!MaintenanceEnabled||File.Exists(Path.Combine(CacheHome(root),"disabled")))return;Mutex existing;if(Mutex.TryOpenExisting(MutexName(root),out existing)){existing.Dispose();return;}
   Process.Start(new ProcessStartInfo(Path.Combine(folder,"雷神自动暂停.exe"),"--watch --path \""+root+"\""+(MakeShortcuts?"":" --no-shortcut")+(MaintenanceNotices?"":" --no-maintenance-notices")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  }
  static void RunTask(string[] args,bool required){using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe"),String.Join(" ",args.Select(a=>"\""+a.Replace("\"","&quot;")+"\""))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WindowStyle=ProcessWindowStyle.Hidden})){p.StandardOutput.ReadToEnd();p.StandardError.ReadToEnd();if(!p.WaitForExit(10000)||required&&p.ExitCode!=0)throw new Exception("后台维护登录任务配置失败。");}}
  static void ActivateMaintenance(string root,string folder){
   if(!MaintenanceEnabled)return;EnsureHome(root);string disabled=Path.Combine(CacheHome(root),"disabled");if(File.Exists(disabled)){if(InWatch||AutomaticInstall)return;File.Delete(disabled);}
   AtomicWrite(Path.Combine(CacheHome(root),"active.txt"),Encoding.UTF8.GetBytes(folder));
   AtomicWrite(Path.Combine(CacheHome(root),"installation.json"),File.ReadAllBytes(StateFile(root)));
   AtomicWrite(Path.Combine(PluginDir(root),"maintenance-location.json"),Encoding.UTF8.GetBytes(Json.Serialize(new{status=Path.Combine(CacheHome(root),"status.json"),manager=Path.Combine(folder,"雷神自动暂停.exe")})));
   string escapeRoot=System.Security.SecurityElement.Escape(root),escapeExe=System.Security.SecurityElement.Escape(Path.Combine(folder,"雷神自动暂停.exe"));
   string xml="<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+WindowsIdentity.GetCurrent().User.Value+"</UserId></LogonTrigger></Triggers><Principals><Principal id=\"Author\"><UserId>"+WindowsIdentity.GetCurrent().User.Value+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><StartWhenAvailable>true</StartWhenAvailable></Settings><Actions Context=\"Author\"><Exec><Command>"+escapeExe+"</Command><Arguments>--watch --path &quot;"+escapeRoot+"&quot;</Arguments><WorkingDirectory>"+System.Security.SecurityElement.Escape(folder)+"</WorkingDirectory></Exec></Actions></Task>";
   if(!MakeShortcuts)xml=xml.Replace("</Arguments>"," --no-shortcut</Arguments>");
   if(!MaintenanceNotices)xml=xml.Replace("</Arguments>"," --no-maintenance-notices</Arguments>");
   string task=Path.Combine(CacheHome(root),"watch-task.xml");File.WriteAllText(task,xml,Encoding.Unicode);RunTask(new[]{"/Create","/TN",TaskName(root),"/XML",task,"/F"},true);
   Status(root,"ready",String.IsNullOrWhiteSpace(Settings(root).feedUrl)?"本地自动修复已开启；尚未配置联网更新源。":"本地自动修复及更新包更新已开启。");StartGuardian(root,folder);
  }
  static void DisableMaintenance(string root){
   if(!Directory.Exists(CacheHome(root)))return;File.WriteAllText(Path.Combine(CacheHome(root),"disabled"),"uninstalled");string pending=Path.Combine(CacheHome(root),"pending.txt");if(File.Exists(pending))File.Delete(pending);var state=LoadState(root);if(state!=null&&state.Version=="uninstalled")AtomicWrite(Path.Combine(CacheHome(root),"installation.json"),Encoding.UTF8.GetBytes(Json.Serialize(state)));RunTask(new[]{"/Delete","/TN",TaskName(root),"/F"},false);RunTask(new[]{"/End","/TN",TaskName(root)},false);Status(root,"disabled","插件已卸载，后台维护已停止。");
  }
  static void QueueInstall(string input){
   string root=Normalize(input);ValidateClient(root);Manifest();if(Quarantined(root,Base))throw new Exception("本客户端的自检曾失败，等待客户端变化或新版。");
   WithOperation(root,()=>{var mount=DiscoverMount(root);var previous=LoadState(root);string cached=CacheBundle(root);RejectLink(PluginDir(root));Directory.CreateDirectory(PluginDir(root));
    if(previous==null||previous.Version=="uninstalled"||previous.MetadataHash!=Hash(mount.PackagePath))previous=new State{Root=root,OriginalPackage=Convert.ToBase64String(mount.Original),OriginalHash=mount.OriginalHash,MetadataHash=Hash(mount.PackagePath),Version="pending",PackageRelative=Relative(root,mount.PackagePath),NativeRelative=Relative(root,mount.NativePath)};
    File.WriteAllText(StateFile(root),Json.Serialize(previous),new UTF8Encoding(false));RecordActivation(root,InstalledBundle(root),mount.Original);ActivateMaintenance(root,cached);if(MakeShortcuts)Shortcut(root);Status(root,"install-deferred","自动适配已排队；正常退出雷神后重新挂载并执行功能自检。");
   });
  }
  static void Alert(string message){if(!MaintenanceNotices)return;try{using(var icon=new NotifyIcon{Icon=SystemIcons.Warning,Text="雷神自动暂停维护",Visible=true}){icon.ShowBalloonTip(6000,"雷神自动暂停需要留意",message,ToolTipIcon.Warning);for(int i=0;i<60;i++){Application.DoEvents();Thread.Sleep(100);}}}catch{}}
  static string AdaptationKey(string root,string folder){return Hash(Path.Combine(folder,"release.json"))+":"+Profile(root).fingerprint;}
  static bool Quarantined(string root,string folder){try{return File.ReadAllText(Path.Combine(CacheHome(root),"quarantined.txt")).Trim()==AdaptationKey(root,folder);}catch{return false;}}
  static void RecordActivation(string root,string previous,byte[] original){if(!MaintenanceEnabled)return;EnsureHome(root);var record=new Activation{originalMetadata=Convert.ToBase64String(original),previousBundle=previous,releaseId=Hash(Path.Combine(Base,"release.json"))};AtomicWrite(Path.Combine(CacheHome(root),"activation.json"),Encoding.UTF8.GetBytes(Json.Serialize(record)));}
  static bool Healthy(string root){
   try{var h=Json.Deserialize<PluginHealth>(File.ReadAllText(Path.Combine(PluginDir(root),"plugin-health.json")));var profile=Json.Deserialize<ClientProfile>(File.ReadAllText(Path.Combine(PluginDir(root),"client-profile.json")));if(h.healthy&&h.fingerprint==profile.fingerprint&&h.version==ToolVersion&&h.releaseId==Hash(Path.Combine(Base,"release.json"))&&DateTimeOffset.UtcNow-DateTimeOffset.Parse(h.updatedAt)<TimeSpan.FromSeconds(20)){var p=Process.GetProcessById(h.pid);string image=ImagePath(p);return image!=null&&image.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase);}}catch{}return false;
  }
  static void RecordCompatible(string root){var profile=File.ReadAllBytes(Path.Combine(PluginDir(root),"client-profile.json"));var info=Json.Deserialize<ClientProfile>(Encoding.UTF8.GetString(profile));string folder=Path.Combine(CacheHome(root),"compatible");Directory.CreateDirectory(folder);AtomicWrite(Path.Combine(folder,Hash(Path.Combine(Base,"release.json"))+"-"+info.fingerprint+".json"),profile);AtomicWrite(Path.Combine(CacheHome(root),"last-compatible-profile.json"),profile);}
  static bool PriorProved(string root,string folder,Release release){if(Quarantined(root,folder))return false;if(release.compatibilityMode!="capability")return ReleaseMatches(root,release.clientVersion,release.asarSha256,release.nativeSha256,release.nativePath,release.rendererSha256);return File.Exists(Path.Combine(CacheHome(root),"compatible",Hash(Path.Combine(folder,"release.json"))+"-"+Profile(root).fingerprint+".json"));}
  static bool TrialFailed(LaunchTrial trial,PluginHealth health,bool running,DateTimeOffset now){if(health!=null&&health.healthy&&health.releaseId==trial.releaseId&&health.fingerprint==trial.fingerprint&&DateTimeOffset.Parse(health.updatedAt)>=DateTimeOffset.Parse(trial.startedAt))return false;return !running&&now-DateTimeOffset.Parse(trial.startedAt)>TimeSpan.FromSeconds(90);}
  static void CheckLaunchTrial(string root){string file=Path.Combine(CacheHome(root),"launch-trial.json");if(!File.Exists(file))return;var trial=Json.Deserialize<LaunchTrial>(File.ReadAllText(file));if(trial.releaseId!=Hash(Path.Combine(Base,"release.json"))){File.Delete(file);return;}PluginHealth h=null;try{h=Json.Deserialize<PluginHealth>(File.ReadAllText(Path.Combine(PluginDir(root),"plugin-health.json")));}catch{}if(h!=null&&h.healthy&&h.releaseId==trial.releaseId&&h.fingerprint==trial.fingerprint&&DateTimeOffset.Parse(h.updatedAt)>=DateTimeOffset.Parse(trial.startedAt)){File.Delete(file);return;}if(TrialFailed(trial,h,ClientRunning(root),DateTimeOffset.UtcNow)){File.WriteAllText(Path.Combine(CacheHome(root),"quarantined.txt"),AdaptationKey(root,Base));File.WriteAllText(Path.Combine(CacheHome(root),"rollback-pending"),"launch-failed");File.Delete(file);Alert("插件加载后客户端未通过启动检查，正在撤回本次挂载。");}}
  static void RollbackActivation(string root){
   var record=Json.Deserialize<Activation>(File.ReadAllText(Path.Combine(CacheHome(root),"activation.json")));if(record.releaseId!=Hash(Path.Combine(Base,"release.json")))throw new Exception("回退记录与当前更新包不同。");
   if(!String.IsNullOrEmpty(record.previousBundle)&&!Same(record.previousBundle,Base)){var previous=VerifyBundle(record.previousBundle);if(PriorProved(root,record.previousBundle,previous)){using(var p=Process.Start(new ProcessStartInfo(Path.Combine(record.previousBundle,"雷神自动暂停.exe"),"--install --automatic-update --no-launch --quiet --path \""+root+"\""+(MakeShortcuts?"":" --no-shortcut")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(!p.WaitForExit(30000)||p.ExitCode!=0)throw new Exception("旧更新包回退未完成。");}return;}}
   WithOperation(root,()=>{if(File.Exists(Path.Combine(CacheHome(root),"disabled")))return;ValidateClient(root);var state=LoadState(root);if(state==null||state.Version=="uninstalled"||Hash(Package(root))!=state.MetadataHash)throw new Exception("模块入口已变化，不能覆盖回退。");byte[] original=Convert.FromBase64String(record.originalMetadata);if(Hash(original)!=(state.OriginalHash??PackageHash))throw new Exception("原始入口备份校验失败。");AtomicWrite(Package(root),original);state.MetadataHash=Hash(original);File.WriteAllText(StateFile(root),Json.Serialize(state),new UTF8Encoding(false));File.WriteAllText(Path.Combine(CacheHome(root),"installation.json"),Json.Serialize(state),new UTF8Encoding(false));Status(root,"rolled-back","启动自检未通过，已撤回插件挂载，保留普通雷神；请留意计费并等待新版。");});
  }
  static void Watch(string root){
   InWatch=true;
   string next=null;using(var mutex=new Mutex(false,MutexName(root))){bool owned=false;try{try{owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}if(!owned)return;
    string lastNotice="",lastStamp="";DateTime lastOnline=DateTime.MinValue,clientSeen=DateTime.MinValue,lastClientCheck=DateTime.MinValue;bool validated=false;Exception compatibilityError=null;while(!File.Exists(Path.Combine(CacheHome(root),"disabled"))){
     string active=ActiveBundle(root);if(active!=null&&!Same(active,Base)){VerifyBundle(active);next=active;break;}
     try{
      var settings=Settings(root);if(settings.autoUpdate&&!String.IsNullOrWhiteSpace(settings.feedUrl)&&DateTime.UtcNow-lastOnline>TimeSpan.FromHours(6)){lastOnline=DateTime.UtcNow;CheckUpdates(root);}
      if(ApplyPending(root)){Thread.Sleep(1000);continue;}CheckLaunchTrial(root);
      if(Quarantined(root,Base)){if(!ClientRunning(root)&&File.Exists(Path.Combine(CacheHome(root),"rollback-pending"))){RollbackActivation(root);File.Delete(Path.Combine(CacheHome(root),"rollback-pending"));}else Status(root,"quarantined",File.Exists(Path.Combine(CacheHome(root),"rollback-pending"))?"更新包启动自检未通过；退出雷神后回退，等待新版更新包。":"更新包已隔离，自动挂载已撤回或已回退，等待新版。");Thread.Sleep(1000);continue;}
      var state=LoadState(root);if(state!=null&&state.Version=="uninstalled"){DisableMaintenance(root);break;}
      if(!IsRoot(root)){Status(root,"waiting-client","等待雷神安装或更新完成。");}
      else{
       try{string stamp=ClientStamp(root);if(stamp!=lastStamp||DateTime.UtcNow-lastClientCheck>TimeSpan.FromMinutes(5)){try{ValidateClient(root);compatibilityError=null;}catch(Exception error){compatibilityError=error;}lastStamp=stamp;lastClientCheck=DateTime.UtcNow;}if(compatibilityError!=null)throw compatibilityError;if(settings.autoRepair&&NeedsRepair(root)){if(ClientRunning(root)){Status(root,"deferred","插件需要安装或修复；退出雷神后自动处理，当前不会重启客户端。");}else{Status(root,"repairing","检测到文件变化，正在修复插件。");Install(root,false);Status(root,"repaired","插件已自动修复，下次启动雷神即可使用。");}}
       else{
        Status(root,settings.autoRepair?"ready":"repair-disabled",settings.autoRepair?(String.IsNullOrWhiteSpace(settings.feedUrl)?"本地自动修复已开启；尚未配置联网更新源。":"本地自动修复已开启，定期检查签名更新包。"):"自动修复已关闭。");
        if(ClientRunning(root)){if(clientSeen==DateTime.MinValue)clientSeen=DateTime.UtcNow;if(Healthy(root)){validated=true;RecordCompatible(root);};if(!validated&&DateTime.UtcNow-clientSeen>TimeSpan.FromSeconds(90)){File.WriteAllText(Path.Combine(CacheHome(root),"quarantined.txt"),AdaptationKey(root,Base));File.WriteAllText(Path.Combine(CacheHome(root),"rollback-pending"),"health-check");Alert("插件未通过启动自检。退出雷神后自动回退，请留意当前计费状态。");}}
        else{clientSeen=DateTime.MinValue;validated=false;}
       }}
       catch(Exception){string version=File.Exists(Path.Combine(root,"version_f.txt"))?File.ReadAllText(Path.Combine(root,"version_f.txt")).Trim():"未知";Status(root,"unsupported","客户端结构暂无法确认，自动适配已停止；请用维护管理器查看详情。");if(lastNotice!=version){lastNotice=version;Alert("雷神已更新或文件发生变化。自动暂停可能未启用；请留意计费，等待已验证更新包。");}}
      }
     }catch(Exception e){Status(root,"error",e.Message);}
     for(int i=0;i<15&&!File.Exists(Path.Combine(CacheHome(root),"disabled"));i++)Thread.Sleep(1000);
    }
   }finally{if(owned)mutex.ReleaseMutex();}}
   if(next!=null)StartGuardian(root,next);
  }
  static Uri Https(string value){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo)||!String.IsNullOrEmpty(uri.Fragment))throw new Exception("更新地址必须是 HTTPS，且不能含登录信息或片段。");return uri;}
  static bool AllowedRedirect(Uri current,Uri next){
   Https(next.AbsoluteUri);if(current.DnsSafeHost.Equals(next.DnsSafeHost,StringComparison.OrdinalIgnoreCase))return current.Port==next.Port;
   string from=current.DnsSafeHost.ToLowerInvariant(),to=next.DnsSafeHost.ToLowerInvariant();
   return current.IsDefaultPort&&next.IsDefaultPort&&(from=="github.com"||from=="api.github.com")&&(to=="release-assets.githubusercontent.com"||to=="objects.githubusercontent.com");
  }
  static byte[] Fetch(string url,int limit){
   if(limit<1)throw new Exception("更新下载大小限制无效。");Uri current=Https(url);ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   for(int hop=0;hop<=5;hop++){
    var request=(HttpWebRequest)WebRequest.Create(current);request.AllowAutoRedirect=false;request.Timeout=10000;request.ReadWriteTimeout=10000;request.UserAgent="LeigodAutoPause/"+ToolVersion;
    using(var response=(HttpWebResponse)request.GetResponse()){
     int status=(int)response.StatusCode;if(status==301||status==302||status==303||status==307||status==308){Uri next;string location=response.Headers["Location"];if(hop==5||String.IsNullOrWhiteSpace(location)||!Uri.TryCreate(current,location,out next)||!AllowedRedirect(current,next))throw new Exception("更新下载跳转不受信任或次数过多。");current=next;continue;}
     if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>limit)throw new Exception("更新下载响应无效或文件过大。");using(var input=response.GetResponseStream())using(var output=new MemoryStream()){var buffer=new byte[32768];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new Exception("更新下载超过大小限制。");output.Write(buffer,0,count);}return output.ToArray();}
    }
   }throw new Exception("更新下载跳转次数过多。");
  }
  static Feed ReadFeed(byte[] bytes){if(bytes.Length>2*1024*1024)throw new Exception("更新清单过大。");var envelope=Json.Deserialize<Envelope>(Encoding.UTF8.GetString(bytes));var payload=Convert.FromBase64String(envelope.payload);VerifySigned(payload,envelope.signature);var feed=Json.Deserialize<Feed>(Encoding.UTF8.GetString(payload));if(feed.schema!=1||feed.releases==null||feed.releases.Length>100)throw new Exception("更新清单格式无效。");return feed;}
  static string StageZip(string root,byte[] bytes){
   if(bytes.Length>64*1024*1024)throw new Exception("更新包过大。");EnsureHome(root);string folder=Path.Combine(CacheHome(root),"incoming",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);int count=0;long total=0;
   using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read)){foreach(var entry in zip.Entries){if(++count>300||entry.Length>16*1024*1024||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new Exception("压缩包结构无效。");if(entry.FullName.EndsWith("/")){Within(folder,entry.FullName.TrimEnd('/'));continue;}string target=Within(folder,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(target));using(var input=entry.Open())using(var output=File.Create(target)){var buffer=new byte[32768];int length;long fileBytes=0;while((length=input.Read(buffer,0,buffer.Length))>0){fileBytes+=length;total+=length;if(fileBytes>16*1024*1024||total>128*1024*1024)throw new Exception("解压大小超出限制。");output.Write(buffer,0,length);}if(fileBytes!=entry.Length)throw new Exception("解压文件长度不符。");}}}
   var manifests=Directory.GetFiles(folder,"release.json",SearchOption.AllDirectories);if(manifests.Length!=1)throw new Exception("更新包必须有唯一签名清单。");string bundle=Path.GetDirectoryName(manifests[0]);if(Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Any(f=>!f.StartsWith(bundle+"\\",StringComparison.OrdinalIgnoreCase)))throw new Exception("压缩包存在清单目录以外的文件。");VerifyBundle(bundle,true);return bundle;
  }
  static Version InstalledVersion(string root){Version v;var state=LoadState(root);return state!=null&&Version.TryParse(state.Version,out v)?v:new Version(0,0,0);}
  static void QueueBundle(string root,string folder){var release=VerifyBundle(folder);if(Quarantined(root,folder))throw new Exception("此更新包曾未通过启动检查，已被隔离，请使用新版。");if(ReleaseVersion(release.version)<=InstalledVersion(root))throw new Exception("此版本已安装或低于当前版本，拒绝降级更新。");if(!ReleaseMatches(root,release.clientVersion,release.asarSha256,release.nativeSha256,release.nativePath,release.rendererSha256,release.compatibilityMode))throw new Exception("更新包与本机雷神版本或文件不匹配。");AtomicWrite(Path.Combine(CacheHome(root),"pending.txt"),Encoding.UTF8.GetBytes(folder));Status(root,"pending","更新包已验证；将在退出雷神及游戏后安装。");ApplyPending(root);}
  static bool ReleaseMatches(string root,string version,string asar,string native,string nativePath,string renderer,string mode=null){if(mode=="capability"){try{ValidateClient(root);return true;}catch{return false;}}return !String.IsNullOrEmpty(renderer)&&IsRoot(root)&&Hash(Path.Combine(root,"resources/renderer.asar"))==renderer&&File.ReadAllText(Path.Combine(root,"version_f.txt")).Trim()==version&&Hash(Path.Combine(root,"resources/app.asar"))==asar&&File.Exists(Within(root,nativePath))&&Hash(Within(root,nativePath))==native;}
  static bool ApplyPending(string root){
   string marker=Path.Combine(CacheHome(root),"pending.txt");if(!File.Exists(marker)||File.Exists(Path.Combine(CacheHome(root),"disabled")))return false;if(ClientRunning(root)||Process.GetProcessesByName("TslGame").Length>0){Status(root,"update-deferred","更新包已就绪；退出雷神和游戏后自动安装。");return false;}
   string folder=File.ReadAllText(marker).Trim();if(!folder.StartsWith(CacheHome(root)+"\\incoming\\",StringComparison.OrdinalIgnoreCase))throw new Exception("待安装路径无效。");var release=VerifyBundle(folder);
   if(ReleaseVersion(release.version)<=InstalledVersion(root)){File.Delete(marker);return false;}
   if(!ReleaseMatches(root,release.clientVersion,release.asarSha256,release.nativeSha256,release.nativePath,release.rendererSha256,release.compatibilityMode))throw new Exception("客户端文件已变化，等待匹配的更新包。");
   string args="--install --automatic-update --quiet --no-launch --path \""+root+"\""+(MaintenanceEnabled?"":" --no-maintenance")+(MakeShortcuts?"":" --no-shortcut");
   using(var p=Process.Start(new ProcessStartInfo(Path.Combine(folder,"雷神自动暂停.exe"),args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(!p.WaitForExit(30000)||p.ExitCode!=0)throw new Exception("更新包安装未完成，保留待安装记录。");}
   if(InstalledVersion(root)!=ReleaseVersion(release.version))throw new Exception("更新包版本复核失败。");File.Delete(marker);Status(root,"updated","已安装并复核新版更新包。");return true;
  }
  static string CheckUpdates(string root){var settings=Settings(root);if(String.IsNullOrWhiteSpace(settings.feedUrl)){Status(root,"no-feed","本地自动修复已开启；尚未配置联网更新源。");return "尚未配置更新源，本地自动修复可以正常使用。";}
   var feed=ReadFeed(Fetch(settings.feedUrl,2*1024*1024));
   foreach(var item in feed.releases.OrderByDescending(x=>ReleaseVersion(x.version))){if(ReleaseVersion(item.version)<=InstalledVersion(root))continue;if(!ReleaseMatches(root,item.clientVersion,item.asarSha256,item.nativeSha256,item.nativePath,item.rendererSha256,item.compatibilityMode))continue;if(item.bytes<=0||item.bytes>64*1024*1024||!System.Text.RegularExpressions.Regex.IsMatch(item.sha256??"",@"^[0-9a-f]{64}$"))throw new Exception("更新包大小或摘要无效。");byte[] bytes=Fetch(item.url,64*1024*1024);if(bytes.Length!=item.bytes||Hash(bytes)!=item.sha256)throw new Exception("下载文件摘要不符，拒绝安装。");string folder=StageZip(root,bytes);var release=VerifyBundle(folder);if(release.version!=item.version)throw new Exception("更新清单与更新包版本不符。");QueueBundle(root,folder);return "更新包已验证，退出雷神及游戏后自动安装。";}
   Status(root,"checked","已检查签名更新源，暂时没有匹配本机的新更新包。");return "暂时没有匹配本机的新更新包。";
  }
  class MaintenanceForm:Form {
   TextBox feed;CheckBox repair,online;Label info;string root;
   public MaintenanceForm(string input){root=Normalize(input);Text="更新与修复";ClientSize=new Size(600,310);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
    var settings=Settings(root);repair=new CheckBox{Text="自动检测并修复本机插件（退出雷神后处理）",Checked=settings.autoRepair,Location=new Point(20,20),AutoSize=true};online=new CheckBox{Text="自动检查工具更新",Checked=settings.autoUpdate,Location=new Point(20,55),AutoSize=true};Controls.Add(repair);Controls.Add(online);
    Controls.Add(new Label{Text="更新地址（默认使用项目更新源）",Location=new Point(20,95),AutoSize=true});feed=new TextBox{Text=settings.feedUrl,Location=new Point(20,125),Size=new Size(560,28)};Controls.Add(feed);
    info=new Label{Text=String.IsNullOrWhiteSpace(settings.feedUrl)?"尚未配置联网更新源；本地自动修复仍可使用。":"更新地址已设置，下载后会自动检查文件。",Location=new Point(20,170),Size=new Size(560,48)};Controls.Add(info);
    Add("保存设置",20,()=>{SaveSettings(root,new MaintenanceSettings{autoRepair=repair.Checked,autoUpdate=online.Checked,feedUrl=feed.Text.Trim()});info.Text="设置已保存。";});
    Add("检查更新",160,()=>{info.Text=CheckUpdates(root);});Add("从本地安装更新包",300,()=>{using(var pick=new OpenFileDialog{Filter="更新包|*.zip"})if(pick.ShowDialog(this)==DialogResult.OK){QueueBundle(root,StageZip(root,File.ReadAllBytes(pick.FileName)));info.Text="更新包已验证，退出雷神和游戏后自动安装。";}});
   }
   void Add(string text,int x,Action action){var b=new Button{Text=text,Location=new Point(x,245),Size=new Size(125,36)};b.Click+=(s,e)=>{try{UseWaitCursor=true;b.Enabled=false;action();}catch(Exception ex){info.Text=ex.Message;}finally{b.Enabled=true;UseWaitCursor=false;}};Controls.Add(b);}
  }
 }
}
