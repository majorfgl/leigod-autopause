using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
namespace LeigodAutoPause {
 static partial class Program {
  static void TestPe(string file,int architecture){Directory.CreateDirectory(Path.GetDirectoryName(file));var bytes=new byte[128];bytes[0]=0x4d;bytes[1]=0x5a;Array.Copy(BitConverter.GetBytes(64),0,bytes,60,4);Array.Copy(BitConverter.GetBytes(0x4550),0,bytes,64,4);Array.Copy(BitConverter.GetBytes((ushort)architecture),0,bytes,68,2);File.WriteAllBytes(file,bytes);}
  static void TestAsar(string file,string extra=""){var header=Encoding.UTF8.GetBytes("{\"files\":{\"index.html\":{\"size\":0,\"offset\":\"0\"}"+extra+"}}");var bytes=new byte[16+header.Length];Array.Copy(BitConverter.GetBytes(header.Length),0,bytes,12,4);Array.Copy(header,0,bytes,16,header.Length);Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,bytes);}
  static void AdaptiveSelfTests(string parent,List<string> passed){
   var now=DateTimeOffset.UtcNow;var trial=new LaunchTrial{startedAt=now.AddMinutes(-2).ToString("o"),releaseId="fixture-release",fingerprint="fixture-client"};if(!TrialFailed(trial,null,false,now)||TrialFailed(trial,null,true,now))throw new Exception("Launch failure decision invalid.");passed.Add("early-client-exit-schedules-rollback");
   var verified=new PluginHealth{healthy=true,releaseId=trial.releaseId,fingerprint=trial.fingerprint,updatedAt=now.AddMinutes(-1).ToString("o")};if(TrialFailed(trial,verified,false,now))throw new Exception("Healthy ordinary exit mistaken for crash.");passed.Add("verified-client-exit-does-not-rollback");
   verified.fingerprint="other-client";if(!TrialFailed(trial,verified,false,now))throw new Exception("Stale health proof accepted.");passed.Add("health-proof-is-bound-to-client-fingerprint");
   string root=Path.Combine(parent,"adaptive-client"),module=Path.Combine(root,"resources/app.asar.unpacked/node_modules/@leigod-rs/win32-node-addon-win32-ia32-msvc");Directory.CreateDirectory(module);
   TestPe(Path.Combine(root,"leigod.exe"),0x14c);TestPe(Path.Combine(module,"first.node"),0x14c);TestAsar(Path.Combine(root,"resources/app.asar"));TestAsar(Path.Combine(root,"resources/renderer.asar"));File.WriteAllText(Path.Combine(root,"version_f.txt"),"99.8.7.6");File.WriteAllText(Path.Combine(module,"package.json"),"{\"name\":\"@leigod-rs/win32-node-addon-win32-ia32-msvc\",\"main\":\"first.node\",\"files\":[\"first.node\"]}");
   ValidateClient(root);passed.Add("unknown-version-structure-accepted");
   if(!ReleaseMatches(root,"old-client","old-asar","old-native","old-path","old-renderer","capability"))throw new Exception("Capability update still uses a fixed client whitelist.");passed.Add("capability-update-accepts-current-structure");
   string asarHash=Hash(Path.Combine(root,"resources/app.asar")),nativeHash=Hash(Path.Combine(module,"first.node"));bool maintenance=MaintenanceEnabled,shortcuts=MakeShortcuts;
   try{MaintenanceEnabled=false;MakeShortcuts=false;
    Install(root,false);if(Hash(Path.Combine(root,"resources/app.asar"))!=asarHash||Hash(Path.Combine(module,"first.node"))!=nativeHash)throw new Exception("Vendor files were changed.");passed.Add("adaptive-install-preserves-vendor-binaries");
    var first=Profile(root);File.WriteAllText(Path.Combine(root,"version_f.txt"),"100.0.0.1");TestAsar(Path.Combine(root,"resources/app.asar"),",\"new-style.css\":{\"size\":0,\"offset\":\"0\"}");if(!ProfileChanged(root))throw new Exception("Changed client not detected.");Install(root,false);if(Profile(root).fingerprint==first.fingerprint||ProfileChanged(root))throw new Exception("Local profile did not migrate.");passed.Add("version-and-resource-changes-remount-locally");
    string moved=Path.Combine(root,"resources/node_modules/@leigod-rs/renamed-module");Relative(root,module);Relative(root,moved);Directory.CreateDirectory(Path.GetDirectoryName(moved));Directory.Move(module,moved);module=moved;
    if(!Same(DiscoverMount(root).PackagePath,Path.Combine(moved,"package.json")))throw new Exception("Moved module not discovered.");Install(root,false);passed.Add("owned-module-directory-relocation");
    File.Move(Path.Combine(module,"first.node"),Path.Combine(module,"second.node"));if(!DiscoverMount(root).NativePath.EndsWith("second.node"))throw new Exception("Renamed native module not recovered.");Install(root,false);passed.Add("native-filename-change-recovered");
    Uninstall(root,false);var restored=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(module,"package.json")));if(Convert.ToString(restored["main"])!="second.node")throw new Exception("Dynamic metadata restore failed.");passed.Add("adaptive-uninstall-restores-current-native-entry");
    string second=Path.Combine(root,"resources/node_modules/@leigod-rs/duplicate");Directory.CreateDirectory(second);TestPe(Path.Combine(second,"second.node"),0x14c);File.Copy(Path.Combine(module,"package.json"),Path.Combine(second,"package.json"));bool rejected=false;try{ValidateClient(root);}catch(Exception e){rejected=e.Message.Contains("多个");}if(!rejected)throw new Exception("Ambiguous modules were accepted.");passed.Add("ambiguous-native-modules-stop-adaptation");
   }finally{MaintenanceEnabled=maintenance;MakeShortcuts=shortcuts;}
  }
 }
}
