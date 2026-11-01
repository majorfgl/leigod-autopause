using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
namespace LeigodAutoPause {
 public class MountInfo { public string PackagePath; public string NativePath; public byte[] Original; public string OriginalHash; public int Score; }
 public class ClientProfile { public int schema=1; public string clientVersion; public string packageRelative; public string nativeRelative; public string originalHash; public string fingerprint; public Dictionary<string,string> files; }
 static partial class Program {
  static string Relative(string root,string path){string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";string full=Path.GetFullPath(path);if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("客户端文件路径越界。");return full.Substring(prefix.Length).Replace('\\','/');}
  static int PeMachine(string file){using(var input=File.OpenRead(file))using(var r=new BinaryReader(input)){if(input.Length<64||r.ReadUInt16()!=0x5a4d)throw new Exception("程序不是有效的 Windows PE 文件。");input.Position=60;int offset=r.ReadInt32();if(offset<64||offset>input.Length-6)throw new Exception("PE 头无效。");input.Position=offset;if(r.ReadUInt32()!=0x4550)throw new Exception("PE 标识无效。");return r.ReadUInt16();}}
  static void AsarStructure(string file){using(var input=File.OpenRead(file))using(var r=new BinaryReader(input)){if(input.Length<16)throw new Exception("客户端资源包不完整。");input.Position=12;uint length=r.ReadUInt32();if(length==0||length>16*1024*1024||16L+length>input.Length)throw new Exception("客户端资源包格式不支持。");var header=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(r.ReadBytes((int)length)));if(!header.ContainsKey("files"))throw new Exception("没有找到 Electron 资源目录。");}}
  static MountInfo DiscoverMount(string root){
   int architecture=PeMachine(Path.Combine(root,"leigod.exe"));var previous=LoadState(root);var matches=new List<MountInfo>();
   foreach(string relative in new[]{"resources/app.asar.unpacked/node_modules","resources/node_modules","resources/app/node_modules"}){
    string modules=Path.Combine(root,relative);if(!Directory.Exists(modules))continue;RejectLink(modules);
    var folders=new List<string>();foreach(string folder in Directory.GetDirectories(modules)){RejectLink(folder);if(Path.GetFileName(folder).StartsWith("@")){folders.AddRange(Directory.GetDirectories(folder));}else folders.Add(folder);}
    foreach(string folder in folders){RejectLink(folder);string package=Path.Combine(folder,"package.json");if(!File.Exists(package))continue;
     try{byte[] current=File.ReadAllBytes(package),original=current;var metadata=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(current));object nameValue,mainValue;
      if(!metadata.TryGetValue("name",out nameValue)||!Convert.ToString(nameValue).ToLowerInvariant().Contains("leigod")||!metadata.TryGetValue("main",out mainValue))continue;
      string entry=Convert.ToString(mainValue);
      if(previous!=null&&Hash(current)==previous.MetadataHash){
       original=Convert.FromBase64String(previous.OriginalPackage);if(Hash(original)!=(previous.OriginalHash??PackageHash))throw new Exception("原始模块元数据备份已变化。");metadata=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(original));entry=Convert.ToString(metadata["main"]);
      }else if(Hash(current)==LegacyHash){original=File.ReadAllBytes(Path.Combine(Base,"original-package.json"));metadata=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(original));entry=Convert.ToString(metadata["main"]);}
      if(Path.IsPathRooted(entry)||!entry.EndsWith(".node",StringComparison.OrdinalIgnoreCase))continue;
      string native=Within(folder,entry.Replace('\\','/'));
      if(!File.Exists(native)){var alternatives=Directory.GetFiles(folder,"*.node").Where(p=>PeMachine(p)==architecture).ToArray();if(alternatives.Length!=1)continue;string oldEntry=entry;entry=Path.GetFileName(alternatives[0]);metadata["main"]=entry;object listed;if(metadata.TryGetValue("files",out listed)&&listed is object[])metadata["files"]=((object[])listed).Select(v=>Convert.ToString(v)==oldEntry?(object)entry:v).ToArray();original=Encoding.UTF8.GetBytes(Json.Serialize(metadata));native=alternatives[0];}
      if(PeMachine(native)!=architecture)continue;
      matches.Add(new MountInfo{PackagePath=package,NativePath=native,Original=original,OriginalHash=Hash(original),Score=Convert.ToString(nameValue).Contains("win32-node-addon")?100:10});
     }catch(ArgumentException){}catch(InvalidOperationException){}
    }
   }
   if(matches.Count==0)throw new Exception("未找到可确认的雷神原生模块入口，需要人工适配。");int score=matches.Max(m=>m.Score);var best=matches.Where(m=>m.Score==score).ToArray();if(best.Length!=1)throw new Exception("存在多个原生模块候选，自动挂载已停止。");return best[0];
  }
  static void ValidateStructure(string root){AsarStructure(Path.Combine(root,"resources/app.asar"));AsarStructure(Path.Combine(root,"resources/renderer.asar"));DiscoverMount(root);}
  static ClientProfile Profile(string root){ValidateStructure(root);var mount=DiscoverMount(root);var profile=new ClientProfile{clientVersion=File.Exists(Path.Combine(root,"version_f.txt"))?File.ReadAllText(Path.Combine(root,"version_f.txt")).Trim():"unknown",packageRelative=Relative(root,mount.PackagePath),nativeRelative=Relative(root,mount.NativePath),originalHash=mount.OriginalHash,files=new Dictionary<string,string>()};foreach(string relative in new[]{"resources/app.asar","resources/renderer.asar",profile.nativeRelative})profile.files[relative]=Hash(Within(root,relative));profile.fingerprint=Hash(Encoding.UTF8.GetBytes(Json.Serialize(profile.files)+profile.packageRelative+profile.clientVersion));return profile;}
  static void WriteProfile(string root,ClientProfile profile){AtomicWrite(Path.Combine(PluginDir(root),"client-profile.json"),Encoding.UTF8.GetBytes(Json.Serialize(profile)));}
  static bool ProfileChanged(string root){try{var p=Json.Deserialize<ClientProfile>(File.ReadAllText(Path.Combine(PluginDir(root),"client-profile.json")));var m=DiscoverMount(root);if(p.schema!=1||p.packageRelative!=Relative(root,m.PackagePath)||p.nativeRelative!=Relative(root,m.NativePath))return true;foreach(var f in p.files)if(!File.Exists(Within(root,f.Key))||Hash(Within(root,f.Key))!=f.Value)return true;string version=File.Exists(Path.Combine(root,"version_f.txt"))?File.ReadAllText(Path.Combine(root,"version_f.txt")).Trim():"unknown";return version!=p.clientVersion;}catch{return true;}}
 }
}
