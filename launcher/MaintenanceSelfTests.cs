using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.IO.Compression;
namespace LeigodAutoPause {
 static partial class Program {
  static void MaintenanceSelfTest(string[] args){
   var passed=new List<string>();string fixture=Path.Combine(Path.GetTempPath(),"leigod-maintenance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
   if(DevelopmentBuild)FixtureCacheRoot=Path.Combine(fixture,"cache");
   Action<string,Action> test=(name,action)=>{action();passed.Add(name);};
   Action<Action,string> rejected=(action,text)=>{try{action();}catch(Exception e){if(text==null||e.Message.Contains(text))return;throw new Exception("Unexpected rejection: "+e.Message);}throw new Exception("Expected rejection.");};
   test("signed-bundle",()=>VerifyBundle(Base));
   test("trailing-separator-does-not-trigger-self-handover",()=>{if(!Same(Base,Base.TrimEnd('\\','/')))throw new Exception("Directory identity differs.");});
   test("inherited-preview-is-cleared",()=>{if(Environment.GetEnvironmentVariable("LEIGOD_AUTOPAUSE_PREVIEW")!=null)throw new Exception("Preview flag leaked into normal launch.");});
   test("watcher-starts-during-client-replacement",()=>{if(!Same(WatchRoot(fixture),fixture))throw new Exception("Watcher requires client files to exist.");});
   test("operations-are-mutually-exclusive",()=>{bool entered=false;using(var gate=new System.Threading.Mutex(false,MutexName(fixture)+"-operation")){gate.WaitOne();var worker=new System.Threading.Thread(()=>WithOperation(fixture,()=>entered=true));worker.Start();System.Threading.Thread.Sleep(150);if(entered)throw new Exception("Operation lock bypassed.");gate.ReleaseMutex();if(!worker.Join(5000)||!entered)throw new Exception("Operation did not resume.");}});
   byte[] manifest=File.ReadAllBytes(Path.Combine(Base,"release.json"));string sig=File.ReadAllText(Path.Combine(Base,"release.sig"));
   test("manifest-tampering",()=>{var b=(byte[])manifest.Clone();b[0]^=1;rejected(()=>VerifySigned(b,sig),"签名");});
   test("signature-tampering",()=>{var b=Convert.FromBase64String(sig);b[0]^=1;rejected(()=>VerifySigned(manifest,Convert.ToBase64String(b)),"签名");});
   foreach(var value in new[]{"../outside","C:/outside","/outside","a\\b","file:stream","a./b","a /b"}){string v=value;test("unsafe-path-"+passed.Count,()=>rejected(()=>Within(fixture,v),null));}
   test("HTTPS-required",()=>rejected(()=>Https("http://updates.invalid/feed.json"),"HTTPS"));
   test("file-feed-rejected",()=>rejected(()=>Https("file:///C:/feed.json"),"HTTPS"));
   test("URL-credentials-rejected",()=>rejected(()=>Https("https://user:fixture@updates.invalid/feed.json"),"HTTPS"));
   test("URL-fragment-rejected",()=>rejected(()=>Https("https://updates.invalid/feed.json#fragment"),"HTTPS"));
   test("HTTPS-valid",()=>Https("https://updates.invalid/feed.json"));
   test("default-public-feed",()=>{if(Settings(fixture).feedUrl!=DefaultFeedUrl)throw new Exception("Default feed missing.");});
   test("blank-feed-remains-disabled",()=>{SaveSettings(fixture,new MaintenanceSettings{feedUrl="",autoUpdate=false});var s=Settings(fixture);if(s.feedUrl!=""||s.autoUpdate)throw new Exception("Explicit opt-out overridden.");});
   test("no-feed-explicit",()=>{if(!CheckUpdates(fixture).Contains("尚未配置"))throw new Exception("Missing no-feed status.");});
   test("GitHub-asset-redirect",()=>{if(!AllowedRedirect(new Uri("https://github.com/owner/repo/releases/download/v1/file.zip"),new Uri("https://release-assets.githubusercontent.com/test")))throw new Exception("GitHub CDN rejected.");});
   test("same-host-redirect",()=>{if(!AllowedRedirect(new Uri("https://updates.example/a"),new Uri("https://updates.example/b")))throw new Exception("Same-host redirect rejected.");});
   test("foreign-host-redirect-rejected",()=>{if(AllowedRedirect(new Uri("https://updates.example/a"),new Uri("https://untrusted.example/b")))throw new Exception("Foreign redirect accepted.");});
   test("lookalike-host-redirect-rejected",()=>{if(AllowedRedirect(new Uri("https://github.com/a"),new Uri("https://release-assets.githubusercontent.com.evil.example/b")))throw new Exception("Lookalike host accepted.");});
   test("redirect-HTTPS-required",()=>rejected(()=>AllowedRedirect(new Uri("https://github.com/a"),new Uri("http://release-assets.githubusercontent.com/b")),"HTTPS"));
   byte[] archive=File.ReadAllBytes(Arg(args,"--zip"));string valid=null;
   test("signed-ZIP",()=>{valid=StageZip(fixture,archive);VerifyBundle(valid,true);});
   test("ZIP-path-traversal",()=>rejected(()=>StageZip(fixture,TestZip("../outside",new byte[]{1},0)),"路径"));
   test("ZIP-link-rejected",()=>rejected(()=>StageZip(fixture,TestZip("link",new byte[]{1},0xA000<<16)),"结构"));
   test("ZIP-size-limit",()=>rejected(()=>StageZip(fixture,TestZip("oversize",new byte[17*1024*1024],0)),"结构"));
   test("ZIP-unsigned-outer-file",()=>{using(var stream=new MemoryStream()){stream.Write(archive,0,archive.Length);using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true)){using(var output=zip.CreateEntry("unsigned.txt").Open())output.WriteByte(1);}rejected(()=>StageZip(fixture,stream.ToArray()),"清单目录");}});
   test("ZIP-payload-tampering",()=>{using(var stream=new MemoryStream()){stream.Write(archive,0,archive.Length);using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true)){ZipArchiveEntry target=null;foreach(var e in zip.Entries)if(e.FullName.EndsWith("/plugin/core.cjs")){target=e;break;}if(target==null)throw new Exception("No fixture payload.");string name=target.FullName;target.Delete();using(var output=zip.CreateEntry(name).Open())output.WriteByte(1);}rejected(()=>StageZip(fixture,stream.ToArray()),"校验");}});
   var stateFolder=PluginDir(fixture);Directory.CreateDirectory(stateFolder);
   test("same-version-replay-rejected",()=>{File.WriteAllText(StateFile(fixture),Json.Serialize(new State{Root=fixture,Version=ToolVersion}));rejected(()=>QueueBundle(fixture,valid),"降级");});
   test("older-version-rejected",()=>{File.WriteAllText(StateFile(fixture),Json.Serialize(new State{Root=fixture,Version="99.0.0"}));rejected(()=>QueueBundle(fixture,valid),"降级");});
   test("health-rejects-missing",()=>{if(Healthy(fixture))throw new Exception("Unverified health accepted.");});
   test("automatic-install-respects-uninstall-marker",()=>{File.WriteAllText(Path.Combine(fixture,"leigod.exe"),"fixture");File.WriteAllText(Path.Combine(fixture,"resources/app.asar"),"fixture");File.WriteAllText(Path.Combine(CacheHome(fixture),"disabled"),"fixture");bool saved=AutomaticInstall;try{AutomaticInstall=true;rejected(()=>Install(fixture,false),"维护已停止");}finally{AutomaticInstall=saved;}});
   test("signed-feed",()=>{var feed=ReadFeed(File.ReadAllBytes(Arg(args,"--feed-fixture")));if(feed.releases.Length!=1||feed.releases[0].version!=ToolVersion)throw new Exception("Feed mismatch.");});
   test("feed-tampering",()=>{var env=Json.Deserialize<Envelope>(File.ReadAllText(Arg(args,"--feed-fixture")));var b=Convert.FromBase64String(env.payload);b[0]^=1;env.payload=Convert.ToBase64String(b);rejected(()=>ReadFeed(Encoding.UTF8.GetBytes(Json.Serialize(env))),"签名");});
   AdaptiveSelfTests(fixture,passed);
   string report=Arg(args,"--output");if(report!=null)File.WriteAllText(report,Json.Serialize(new{ok=true,count=passed.Count,passed}),new UTF8Encoding(false));
  }
  static byte[] TestZip(string name,byte[] bytes,int attributes){using(var stream=new MemoryStream()){using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true)){var entry=archive.CreateEntry(name);entry.ExternalAttributes=attributes;using(var output=entry.Open())output.Write(bytes,0,bytes.Length);}return stream.ToArray();}}
 }
}
