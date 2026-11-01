'use strict';
const {app,Notification,dialog,ipcMain}=require('electron');
const fs=require('fs'),path=require('path'),{execFile,execFileSync}=require('child_process'),{performance}=require('perf_hooks');
const {Monitor}=require('./core.cjs'),{config,DEFAULTS,TIMES,EXCLUDED,names}=require('./config.cjs'),{snapshot,gameInfo,mount}=require('./renderer.cjs'),{pauseTransaction}=require('./transaction.cjs');
const {compatCheck}=require('./processes.cjs');
const {routes,bridge,detectProcesses}=require('./adapter.cjs');
const VERSION=require('../release.config.json').version,monitor=new Monitor(),clientRequire=require('module').createRequire(path.join(app.getAppPath(),'package.json'));
const folder=path.join(app.getPath('userData'),'leigod-auto-pause');fs.mkdirSync(folder,{recursive:true});
const logFile=path.join(folder,'events.jsonl');let win=null,busy=false,ending=false,allowQuit=false,lastState='',lastConfig='',lastSnapshot=null,addon=null,shutdown=false;
const cache=new Map(),sleep=ms=>new Promise(r=>setTimeout(r,ms));
let uiReady=false,lastHealth=0,pauseVerified=false,lastProbe=0,capability={ok:false,reason:'checking'},bridgeKey='leigodSimplify',fallbackUsed=false;const profile=global.__leigodAutoPauseClientProfile||{};const maintenanceChannels=new Set();const installFolder=path.join(process.resourcesPath,'leigod-auto-pause');
let releaseId='';try{releaseId=require('crypto').createHash('sha256').update(fs.readFileSync(path.join(installFolder,'release.json'))).digest('hex');}catch{}
function health(ok){if(Date.now()-lastHealth<5000)return;lastHealth=Date.now();try{fs.writeFileSync(path.join(installFolder,'plugin-health.json'),JSON.stringify({pid:process.pid,version:VERSION,releaseId,healthy:!!ok,fingerprint:profile.fingerprint,pauseVerified,updatedAt:new Date().toISOString()}));}catch{}}
function maintenance(){try{const location=JSON.parse(fs.readFileSync(path.join(installFolder,'maintenance-location.json'),'utf8'));const state=JSON.parse(fs.readFileSync(location.status,'utf8'));return {phase:state.phase,message:state.message,checkedAt:state.checkedAt};}catch{return {phase:'unavailable',message:'尚未读取到维护状态，请打开管理器检查。'};}}
function wireMaintenance(prefix){const channel=prefix+'autopause-maintenance';if(maintenanceChannels.has(channel))return;maintenanceChannels.add(channel);ipcMain.handle(channel,event=>{if(!win||event.sender.id!==win.webContents.id)throw new Error('wrong-window');let manager=path.join(installFolder,'雷神自动暂停.exe');try{manager=JSON.parse(fs.readFileSync(path.join(installFolder,'maintenance-location.json'),'utf8')).manager||manager;}catch{}const child=require('child_process').spawn(manager,['--maintenance','--path',path.dirname(process.execPath)],{detached:true,stdio:'ignore',windowsHide:false});child.on('error',()=>notice('维护管理器未能打开','请使用桌面入口检查自动维护状态。',true));child.unref();return {result:{code:200}};});}
wireMaintenance('leigod-simplify-');
function log(event,data={}){try{if(fs.existsSync(logFile)&&fs.statSync(logFile).size>1048576){fs.copyFileSync(logFile,logFile+'.previous');fs.writeFileSync(logFile,'');}fs.appendFileSync(logFile,JSON.stringify({time:new Date().toISOString(),pid:process.pid,version:VERSION,event,...data})+'\n');}catch{}}
function notice(title,body,force=false){if(force||lastSnapshot?.config.notifications)try{new Notification({title,body}).show();}catch{}}
function timeout(p,ms=6000){let timer;return Promise.race([p,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('timeout')),ms);})]).finally(()=>clearTimeout(timer));}
async function js(code){if(!win||win.isDestroyed())throw new Error('window-unavailable');return timeout(win.webContents.executeJavaScript(code));}
async function invoke(command,args){if(command!=='refresh-user-time-info'&&!capability.ok)throw new Error('capability-unconfirmed');return js(`window[${JSON.stringify(bridgeKey)}].invoke(${JSON.stringify(command)}${args===undefined?'':','+JSON.stringify(args)}).then(r=>{const code=r?.error?.message?.code??r?.error?.code??r?.result?.code??r?.code??null;return {ok:!r?.error&&(code===null||code===0||code===200||(${JSON.stringify(command)}==='pause-user-time'&&code===400803)),code};})`);}
async function processState(list,mode){const result=await detectProcesses(list,mode,global.__leigodAutoPauseOriginalAddon,compatCheck);if(result.fallback&&!fallbackUsed){fallbackUsed=true;log('process-detector-fallback');}return result.state;}
async function probe(s){
 if(Date.now()-lastProbe<15000&&capability.ok)return true;lastProbe=Date.now();
 const discovered=routes(ipcMain._invokeHandlers),selected=await js('('+bridge.toString()+')()');
 const layout=await js('window.__leigodAutoPauseLayout?.()');uiReady=!!layout?.visible&&!!layout?.located;
 let next={ok:false,reason:!discovered.ok?discovered.reason:!selected?'bridge-missing':!uiReady?'layout-missing':!s.known?'state-schema-missing':'checking'};
 if(discovered.ok&&selected&&uiReady&&s.known){bridgeKey=selected;await js('window.__leigodAutoPauseBridge='+JSON.stringify(selected));wireMaintenance(discovered.prefix);
  if(!capability.ok&&s.loggedIn){const response=await invoke('refresh-user-time-info');if(!response.ok)throw new Error('readonly-probe-failed');}
  next={ok:true,reason:'capability-ready',prefix:discovered.prefix,native:typeof global.__leigodAutoPauseOriginalAddon?.isProcessRunning==='function',stop:discovered.stop};
 }
 if(JSON.stringify(next)!==JSON.stringify(capability))log('capability-probe',next);capability=next;return capability.ok;
}
async function read(refresh=false){
 if(refresh){const r=await invoke('refresh-user-time-info');if(!r.ok)throw new Error('refresh-failed');await sleep(350);}
 const s=await js(`(${snapshot.toString()})()`);s.config=config(s.config);
 const id=s.gameId||monitor.gameId;
 if(id&&(!cache.has(id)||(!names(cache.get(id).processes).length&&Date.now()-cache.get(id).at>30000))){let info={};try{info=await js(`(${gameInfo.toString()})(${id})`);}catch{}cache.set(id,{...info,at:Date.now()});}
 const info=cache.get(id)||{};
 s.processNames=s.config.processMap[id]||names(info.processes);
 if(!s.processNames.length&&id===106)s.processNames=['TslGame.exe'];
 s.gameName=s.gameName||info.name||'';s.excluded=EXCLUDED.has(id)||!!info.free;
 s.billingOnly=!id;
 s.process=s.billingOnly?'exited':await processState(s.processNames,s.config.detectMode);return s;
}
function presentation(r,s){
 const n=Math.ceil(r.remaining/1000),clock=String(Math.floor(n/60)).padStart(2,'0')+':'+String(n%60).padStart(2,'0');
 const map={disabled:['自动监控已关闭','自动暂停已关闭','#999'],unknown:['状态无法确认','客户端或进程状态异常，本轮暂停已取消','#e7a65a'],login:['请先登录','请在雷神中登录','#999'],paused:['已暂停计费','已确认客户端显示时长暂停','#91bb9c'],running:['● 监控中',(s.gameName||'游戏')+'正在运行，退出后开始等待','#75c08e'],countdown:['⏳ '+clock,'游戏已退出，重进游戏会取消倒计时','#e9aa4b'],startup:['等待启动 '+clock,'已开启加速，等待游戏启动','#e9aa4b'],due:['复核中','正在重新检查游戏及计费状态','#e9aa4b'],pausing:['正在暂停','正在暂停计费并复核结果','#e9aa4b'],failed:['暂停失败','暂停计费未能确认，请手动检查剩余时长','#e78b67'],missing:['设置进程','未识别到进程，请点击填写当前游戏的进程名称','#70b8e6'],excluded:['自动监控已跳过','当前为平台或免费加速项，已跳过','#aaa'],idle:['⚙ 自动监控','开启游戏加速后自动监控','#aaa'],snoozed:['本轮已暂缓','本轮倒计时已暂缓；重新进入游戏或开启新加速会话后恢复','#aaa'],'waiting-game':['等待游戏','未启用首次启动等待，将在游戏运行后开始监控','#aaa'],'account-changed':['账号已变化','已取消旧账号的暂停任务','#aaa']};
 const [badge,detail,color]=r.state==='startup'&&s.billingOnly?['未加速 '+clock,'计费已开启但未加速游戏，到时自动暂停；开始加速游戏会重新判断。','#e9aa4b']:map[r.state]||map.unknown;return {...r,badge,detail,color,gameId:s.gameId||monitor.gameId,gameName:s.gameName,maintenance:maintenance()};
}
async function render(r,s){if(lastState!==r.state){lastState=r.state;log('state',{state:r.state,phase:r.phase||'',waitMs:r.remaining||0,gameId:r.gameId||0});}await js(`window.__leigodAutoPauseRender?.(${JSON.stringify(presentation(r,s))})`);}
async function perform(closing=false){
 if(!capability.ok)throw new Error('capability-unconfirmed');
 const epoch=monitor.begin(),identity=lastSnapshot?.account,targetGameId=lastSnapshot?.gameId||monitor.gameId;
 const guard=async()=>{const s=await read(true);if(!s.known||!s.loggedIn||s.account!==identity||(!closing&&(!s.config.enabled||monitor.epoch!==epoch||s.cancelNonce!==lastSnapshot.cancelNonce||JSON.stringify(s.config)!==lastConfig||s.process!=='exited'||s.excluded||(s.gameId&&s.gameId!==targetGameId))))throw new Error('cancelled');if(!['pause','timeing'].includes(s.timeStatus))throw new Error('state-unknown');return s;};
 const outcome=await pauseTransaction({guard,processState:()=>lastSnapshot.billingOnly?'exited':processState(lastSnapshot.processNames,lastSnapshot.config.detectMode),invoke,sleep,log,targetGameId,closing});
 if(outcome.status==='confirmed'){pauseVerified=true;monitor.reset();log('pause-confirmed',{reason:closing?'client-close':'monitor',stopFailed:outcome.stopFailed});notice('雷神已暂停计费',outcome.stopFailed?'计费已暂停，但停止加速未能确认，请检查。':'已确认暂停，剩余时长稳定。');}
 else if(outcome.status==='cancelled'){monitor.reset();log('pause-cancelled');}
 else{monitor.fail();notice('自动暂停未能确认','请打开雷神检查是否仍在计费。',true);}
 return outcome.status==='confirmed';
}
async function tick(){
 if(busy||ending||!win||win.isDestroyed())return;busy=true;
 try{
  const s=await read();lastSnapshot=s;if(!await probe(s)){monitor.reset();health(false);await js('window.__leigodAutoPauseRender?.('+JSON.stringify({state:'checking',badge:'适配自检中',detail:'正在确认界面和计费接口，未确认前不会自动暂停。',maintenance:maintenance()})+')');return;}const signature=JSON.stringify(s.config);
  if(lastConfig&&lastConfig!==signature){const effect=monitor.reconfigure(JSON.parse(lastConfig),s.config);if(effect==='process-reset')cache.clear();if(effect!=='preserved')lastState='';log('settings-changed',{effect});}lastConfig=signature;
  const r=monitor.step(s,performance.now());await render(r,s);health(s.known&&uiReady&&capability.ok);
  if(r.due){await render({state:'pausing'},s);await perform();}
 }catch{health(false);monitor.reset();if(lastState!=='unknown')log('read-failed');try{await render({state:'unknown'},lastSnapshot||{});}catch{}}
 finally{busy=false;}
}
async function mountWindow(candidate){
 if(!candidate.webContents.getURL().replace(/\\/g,'/').includes('/renderer.asar/'))return;
 if(win&&!win.isDestroyed()&&win!==candidate)return;
 win=candidate;uiReady=false;monitor.reset();monitor.lastCancel=0;lastState='';
 try{const layout=await js(`(${mount.toString()})(${JSON.stringify(DEFAULTS)},${JSON.stringify(TIMES)})`);log('mounted',{layout});setTimeout(async()=>{try{const finalLayout=await js('window.__leigodAutoPauseLayout?.()');uiReady=!!finalLayout?.visible;log('layout-ready',{layout:finalLayout});if(process.env.LEIGOD_AUTOPAUSE_PREVIEW==='1'){log('ui-self-test',await js('window.__leigodAutoPauseUiSelfTest()'));await js('window.__leigodAutoPauseOpenSettings?.()');}}catch{log('ui-self-test-failed');}},2000);await tick();}catch{log('mount-failed');notice('自动暂停界面未加载','请用管理器修复或检查客户端版本。',true);}
 if(!candidate.__autoPauseSessionHook){candidate.__autoPauseSessionHook=true;candidate.once('session-end',()=>{shutdown=true;ending=true;const result=require('./shutdown.cjs').attempt(capability.ok?lastSnapshot:null,execFileSync,path.join(process.env.SystemRoot||'C:/Windows','System32/curl.exe'));log('shutdown-attempt',result);});}
}
app.on('browser-window-created',(_,candidate)=>candidate.webContents.on('did-finish-load',()=>mountWindow(candidate)));
async function protectExit(){
 if(ending)return false;ending=true;log('client-close-request');
 let ok=false;try{while(busy)await sleep(50);ok=await perform(true);}catch{}
 if(ok)return true;
 const result=await dialog.showMessageBox(win,{type:'warning',title:'尚未确认暂停计费',message:'暂停计费未能确认，是否仍要退出雷神？',buttons:['保持雷神运行','仍然退出'],defaultId:0,cancelId:0});
 if(result.response===1)return true;ending=false;return false;
}
app.whenReady().then(()=>{
 let tries=0;const timer=setInterval(()=>{
  const discovered=routes(ipcMain._invokeHandlers),channel=(discovered.prefix||'leigod-simplify-')+'quit-app',original=ipcMain._invokeHandlers?.get(channel);
  if(!original){if(++tries>200){clearInterval(timer);log('close-hook-unavailable');}return;}
  clearInterval(timer);ipcMain.removeHandler(channel);
  ipcMain.handle(channel,async(event,...args)=>{
   if(!allowQuit&&capability.ok&&lastSnapshot?.config.enabled&&lastSnapshot.config.closePause&&lastSnapshot.timeStatus==='timeing'){
    if(!await protectExit())return {result:{code:200,cancelled:true}};allowQuit=true;
   }
   try{const result=await original(event,...args);setTimeout(()=>{if(win&&!win.isDestroyed()){allowQuit=false;ending=false;}},5000).unref();return result;}catch(e){allowQuit=false;ending=false;throw e;}
  });log('close-hook-ready');
 },100);timer.unref();
});
app.on('before-quit',event=>{
 if(allowQuit||shutdown||!capability.ok||!lastSnapshot?.config.enabled||!lastSnapshot.config.closePause||lastSnapshot.timeStatus==='pause')return;
 event.preventDefault();protectExit().then(ok=>{if(ok){allowQuit=true;app.quit();}});
});
setInterval(tick,1000).unref();
log('plugin-loaded');
