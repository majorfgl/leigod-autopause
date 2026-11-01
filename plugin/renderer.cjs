'use strict';
function snapshot(){
 const root=document.querySelector('#app')||document.body.firstElementChild;
 const app=root?.__vue_app__,provides=app?._context?.provides||{};
 const pinia=app?.config?.globalProperties?.$pinia||root?.__vue__?.$pinia||Object.values(provides).find(p=>p?._s&&typeof p._s.values==='function');
 const stores=[...(pinia?._s?.values()||[])].slice(0,64);
 const values=s=>Object.values(s).filter(v=>v&&typeof v==='object').slice(0,128);
 const times=v=>v&&typeof v==='object'&&('timeStatus' in v||'time_status' in v)&&('totalTimeLeft' in v||'total_time_left' in v);
 const accs=v=>v&&typeof v==='object'&&('game_id' in v||'gameId' in v)&&('accStatus' in v||'status' in v);
 const users=stores.filter(s=>typeof(s.isLogin??s.isLoggedIn)==='boolean'&&(times(s.userTimeInfo)||values(s).some(times)));
 const accelerators=stores.filter(s=>accs(s.accInfo)||values(s).some(accs));
 const user=users.length===1?users[0]:null,acc=accelerators.length===1?accelerators[0]:null;
 const t=times(user?.userTimeInfo)?user.userTimeInfo:user&&values(user).find(times),g=accs(acc?.accInfo)?acc.accInfo:acc&&values(acc).find(accs);
 const identity=user?.userInfo||user?.accountInfo||user&&values(user).find(v=>'nn_number' in v||'user_id' in v);
 const loggedIn=!!(user?.isLogin??user?.isLoggedIn),account=String(identity?.nn_number??identity?.user_id??'');
 const raw=t?.timeStatus??t?.time_status,status=raw==='paused'?'pause':raw==='timing'?'timeing':raw||'';
 const total=t?.totalTimeLeft??t?.total_time_left,gameId=Number(g?.game_id??g?.gameId),accStatus=String(g?.accStatus??g?.status??'').toLowerCase();const gameKnown=Number.isSafeInteger(gameId)&&gameId>=0&&(gameId>0||['normal','idle','stopped','none',''].includes(accStatus));
 const known=!!user&&!!acc&&gameKnown&&(!loggedIn||(!!account&&['pause','timeing'].includes(status)&&Number.isFinite(total)&&total>=0));
 let config={};try{config=JSON.parse(localStorage.getItem('leigod-autopause-v2')||'{}');}catch{}
 return {known,loggedIn,account,timeStatus:status,total:Number.isFinite(total)?total:null,canPause:Number.isFinite(t?.canPauseTimeLeft)?t.canPauseTimeLeft:null,notCanPause:Number.isFinite(t?.notCanPauseTimeLeft)?t.notCanPauseTimeLeft:null,gameId:gameKnown?gameId:0,gameName:String(g?.game_name??g?.gameName??''),shutdownToken:config.shutdownPause&&loggedIn?String(user?.accountToken||''):'',config,cancelNonce:Number(window.__leigodAutoPauseCancel)||0};
}
async function gameInfo(id){
 let dbInfo=null,api=null;
 const readDB=name=>new Promise(resolve=>{let ended=false;const finish=v=>{if(!ended){ended=true;resolve(v);}};const request=indexedDB.open(name);request.onupgradeneeded=()=>{request.transaction.abort();finish(null);};request.onerror=()=>finish(null);request.onsuccess=()=>{const db=request.result;if(ended){db.close();return;}const storeName=[...db.objectStoreNames].find(n=>n==='game_list')||[...db.objectStoreNames].find(n=>/^(games|gameList|game-list)$/i.test(n));if(!storeName){db.close();finish(null);return;}try{const store=db.transaction(storeName,'readonly').objectStore(storeName),source=store.indexNames.contains('id')?store.index('id'):store;const get=key=>{const q=source.get(key);q.onerror=()=>{db.close();finish(null);};q.onsuccess=()=>{if(q.result||typeof key==='string'){db.close();finish(q.result||null);}else get(String(id));};};get(id);}catch{db.close();finish(null);}};});
 try{let names=['leigod_database_11.0.0.0'];if(typeof indexedDB.databases==='function')names=(await indexedDB.databases()).map(d=>d.name).filter(n=>typeof n==='string'&&/leigod/i.test(n)).slice(0,8);for(const name of names){dbInfo=await readDB(name);if(dbInfo)break;}}catch{}
 try{const r=await window[window.__leigodAutoPauseBridge||'leigodSimplify'].invoke('get-game-info',{game_id:id});const value=r?.result?.data??r?.result??r?.data??r;api=Array.isArray(value)?value:Array.isArray(value?.game_list)?value.game_list:value?.game_process?[value]:null;}catch{}
 const item=api?.find(x=>typeof x.game_process==='string'&&x.game_process.trim())||dbInfo;
 return {processes:typeof item?.game_process==='string'?item.game_process:'',name:String(dbInfo?.game_name||item?.game_name||''),free:String(dbInfo?.is_free??item?.is_free)==='1'};
}
function mount(defaults,times,version=''){
 const id='leigod-autopause-host';if(document.getElementById(id))return {existing:true};
 document.getElementById('pubg-autopause-host')?.remove();
 let config={...defaults,processMap:{}};
 try{const saved=JSON.parse(localStorage.getItem('leigod-autopause-v2')||'null');if(saved)config={...config,...saved};else{const oldExit=Number(localStorage.getItem('leigod_grace_time'));const oldStartup=Number(localStorage.getItem('leigod_startup_time'));if(oldExit>0&&times.includes(oldExit))config.exitMs=oldExit;if(oldStartup>0&&times.includes(oldStartup))config.startupMs=oldStartup;config.enabled=localStorage.getItem('local-pubg-autopause-enabled')!=='false';}}catch{}
 const persist=()=>localStorage.setItem('leigod-autopause-v2',JSON.stringify(config));persist();
 const host=document.createElement('div');host.id=id;host.style.cssText='position:relative;display:inline-flex;align-items:center;justify-content:center;flex-shrink:0;min-width:124px;margin:0 14px 0 7px;height:26px;z-index:9999;-webkit-app-region:no-drag;';
 const shadow=host.attachShadow({mode:'open'});
 shadow.innerHTML=`<style>:host{font:12px "Microsoft YaHei",sans-serif;color:#ddd}*{box-sizing:border-box}button,input,textarea{font:inherit}button{cursor:pointer;-webkit-app-region:no-drag}#badge{height:26px;max-width:146px;padding:0 11px;border:1px solid transparent;border-radius:14px;background:rgba(255,255,255,.1);color:#b9b9b9;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-variant-numeric:tabular-nums}#badge:hover{background:rgba(255,255,255,.17);color:#fff}#overlay[hidden]{display:none!important}#overlay{position:fixed;inset:0;display:flex;justify-content:center;align-items:center;background:#0009;z-index:2147483646;-webkit-app-region:no-drag}#panel{width:500px;max-width:90vw;max-height:88vh;overflow:auto;padding:22px 26px;border:1px solid #454545;border-radius:12px;background:#292929;box-shadow:0 14px 70px #0009;color:#ddd}header{display:flex;align-items:center;justify-content:space-between;margin-bottom:12px}h2{font-size:17px;font-weight:500;margin:0;color:#eee}h3{font-size:13px;font-weight:500;margin:16px 0 9px;color:#f3aa43}p{line-height:1.65;margin:7px 0;color:#aaa}.choices{display:grid;grid-template-columns:repeat(4,1fr);gap:7px}.choice{padding:7px 0;background:#3b3b3b;color:#ccc;border:1px solid transparent;border-radius:5px}.choice.active{background:#b97419;border-color:#e6a34b;color:#fff}.check{display:flex;align-items:center;gap:7px;margin:9px 0;cursor:pointer}.check input{accent-color:#e9a240}#close{border:0;background:transparent;color:#aaa;font-size:24px;padding:0 5px}#status{background:#222;border-radius:6px;padding:9px 11px;margin-bottom:12px;color:#b6d5bd}#snooze{width:100%;border:1px solid #986b2b;color:#efba6b;background:#3a2d1c;padding:8px;border-radius:6px;margin:5px 0}textarea{width:100%;height:56px;resize:vertical;padding:8px;background:#1e1e1e;color:#ddd;border:1px solid #555;border-radius:5px}#save-process{margin-top:6px;background:#444;border:0;color:#eee;border-radius:5px;padding:7px 14px}.small{font-size:11px;color:#999}.mode{display:flex;gap:10px}#process-error{color:#e5a15c}.footer{border-top:1px solid #444;padding-top:10px;margin-top:15px;font-size:11px;color:#999}</style>
 <button id="badge" aria-label="自动暂停设置" title="点击设置自动暂停">⚙ 自动监控</button>
 <div id="overlay" hidden><section id="panel" role="dialog" aria-label="自动暂停设置"><header><h2>自动暂停设置</h2><button id="close" aria-label="关闭设置">×</button></header><div id="status">正在检查客户端状态</div><label class="check"><input id="enabled" type="checkbox">启用自动暂停</label><button id="snooze" hidden>暂缓本轮倒计时</button><h3>游戏退出等待时间</h3><p>游戏退出后等待，期间重新进入游戏会取消。</p><div id="exit-options" class="choices"></div><h3>首次启动等待</h3><label class="check"><input id="startupEnabled" type="checkbox">开启加速后，未启动游戏也自动暂停</label><div id="startup-options" class="choices"></div><h3>当前游戏进程</h3><p id="game-label">开启游戏加速后可设置。自动识别失败时填写，手动设置优先。</p><textarea id="processes" placeholder="例如 TslGame.exe；多个进程用逗号分隔"></textarea><button id="save-process">保存进程设置</button><p id="process-error" class="small"></p><h3>进程检测</h3><div class="mode"><label><input name="mode" type="radio" value="native" title="优先使用客户端检测，失败时尝试 Windows 检测">自动检测</label><label><input name="mode" type="radio" value="compat">Windows 检测</label></div><h3>其他保护</h3><label class="check"><input id="closePause" type="checkbox">关闭客户端时尝试暂停</label><label class="check"><input id="shutdownPause" type="checkbox">关机 / 重启时尝试暂停（实验性）</label><p class="small">系统关机可能不给予足够处理时间，不能保证暂停成功。</p><label class="check"><input id="notifications" type="checkbox">显示暂停结果和异常通知</label><div class="footer"><p id="maintenance-status">正在读取自动维护状态</p><button id="maintenance-open">更新与修复</button></div><div class="footer">设置自动保存 · 共用雷神登录 · 本工具为非官方插件</div></section></div>`;
 shadow.querySelector('style').textContent+='#badge{width:max-content;min-width:124px;max-width:none;flex-shrink:0}#panel::-webkit-scrollbar{width:6px}#panel::-webkit-scrollbar-track{background:#232323}#panel::-webkit-scrollbar-thumb{background:#666;border-radius:5px}';
 const portal=document.createElement('div');portal.id='leigod-autopause-dialog';portal.style.cssText='position:fixed;inset:0;z-index:2147483647;pointer-events:none;-webkit-app-region:no-drag;';
 const dialogShadow=portal.attachShadow({mode:'open'});dialogShadow.appendChild(shadow.querySelector('style').cloneNode(true));
 const overlay=shadow.getElementById('overlay');overlay.style.pointerEvents='auto';dialogShadow.appendChild(overlay);document.body.appendChild(portal);
 const el=n=>shadow.getElementById(n)||dialogShadow.getElementById(n);let current={gameId:0,gameName:'',state:'idle'};
 const versionLabel=document.createElement('span');versionLabel.id='plugin-version';versionLabel.textContent=version?'v'+String(version):'版本未知';versionLabel.title='当前加载的插件版本';versionLabel.style.cssText='margin-left:8px;font-size:12px;font-weight:400;color:#aaa;white-space:nowrap';dialogShadow.querySelector('h2').append(' ',versionLabel);
 const label=ms=>ms===0?'立即':ms<60000?ms/1000+' 秒':ms/60000+' 分钟';
 function choices(target,key){el(target).replaceChildren();for(const ms of times){const b=document.createElement('button');b.className='choice'+(config[key]===ms?' active':'');b.textContent=label(ms);b.onclick=()=>{config[key]=ms;persist();choices(target,key);};el(target).appendChild(b);}}
 for(const key of ['enabled','startupEnabled','closePause','shutdownPause','notifications']){el(key).checked=!!config[key];el(key).onchange=()=>{config[key]=el(key).checked;persist();};}
 for(const radio of dialogShadow.querySelectorAll('[name=mode]')){radio.checked=radio.value===config.detectMode;radio.onchange=()=>{config.detectMode=radio.value;persist();};}
 choices('exit-options','exitMs');choices('startup-options','startupMs');
 function open(){el('processes').value=(config.processMap?.[current.gameId]||[]).join(', ');el('game-label').textContent=current.gameId?`${current.gameName||'当前游戏'} · 手动设置优先；留空恢复自动识别。`:'开启游戏加速后可设置进程。';el('save-process').disabled=!current.gameId;el('overlay').hidden=false;el('close').focus();}
 function close(){el('overlay').hidden=true;el('badge').focus();}
 el('maintenance-open').onclick=()=>window[window.__leigodAutoPauseBridge||'leigodSimplify'].invoke('autopause-maintenance').catch(()=>{el('maintenance-status').textContent='维护管理器未能打开，请使用桌面入口检查。';});el('badge').onclick=open;el('close').onclick=close;el('overlay').onclick=e=>{if(e.target===el('overlay'))close();};for(const container of [host,portal])container.addEventListener('keydown',e=>{if(e.key==='Escape')close();});
 window.__leigodAutoPauseOpenSettings=open;
 el('snooze').onclick=()=>{window.__leigodAutoPauseCancel=(Number(window.__leigodAutoPauseCancel)||0)+1;close();};
 el('save-process').onclick=()=>{const list=el('processes').value.split(/[,;\n\r，；]+/).map(x=>x.trim()).filter(Boolean);if(list.some(x=>x.length>160||!/^.+\.exe$/i.test(x)||/[\\/:*?"<>|]/.test(x))){el('process-error').textContent='请填写进程文件名（以 .exe 结尾），不要填写路径。';return;}config.processMap=config.processMap||{};if(list.length)config.processMap[current.gameId]=[...new Set(list)].slice(0,20);else delete config.processMap[current.gameId];persist();el('process-error').textContent='已保存';};
 window.__leigodAutoPauseRender=state=>{current=state;el('maintenance-status').textContent=state.maintenance?.message||'尚未读取到自动维护状态';el('badge').textContent=state.badge;el('badge').title=state.detail+'；点击设置';el('badge').style.color=state.color||'#aaa';el('status').textContent=state.detail;el('snooze').hidden=!['countdown','startup'].includes(state.state);};
 function anchors(){
 const visible=n=>{const r=n?.getBoundingClientRect();return r&&r.top>=0&&r.top<90&&r.width>0&&r.height>0;};
 const search=[...document.querySelectorAll('input')].find(n=>visible(n)&&/搜索|search/i.test((n.placeholder||'')+' '+(n.getAttribute('aria-label')||'')))||[...document.querySelectorAll('input,[role=searchbox],[class*=search]')].filter(n=>visible(n)&&n.getBoundingClientRect().width>100&&n.getBoundingClientRect().width<600).sort((a,b)=>b.getBoundingClientRect().width-a.getBoundingClientRect().width)[0];
 let time=document.querySelector('.time-format-view,[data-role="remaining-time"],[class*="remaining-time"]');
 if(!visible(time))time=[...document.querySelectorAll('span,button')].filter(n=>visible(n)&&/^(?:\d+\s*时.*分|暂停时长|开启时长)/.test(n.textContent.trim())).sort((a,b)=>a.textContent.length-b.textContent.length)[0];
 let nav=document.querySelector('.nav-control'),right=document.querySelector('.redpack-wrap');
 if(nav&&right&&right.parentNode===nav)return {nav,right,search,time};
 if(!search||!time||search.getBoundingClientRect().right>time.getBoundingClientRect().left)return {};
 nav=time.parentElement;for(let i=0;nav&&i<6&&!nav.contains(search);i++)nav=nav.parentElement;
 if(!nav||!nav.contains(search)||nav.getBoundingClientRect().height>100)return {};
 right=time;while(right.parentElement!==nav)right=right.parentElement;return {nav,right,search,time};
 }
 function place(){const {nav,right}=anchors();if(nav&&right){if(host.parentNode!==nav||host.nextSibling!==right)nav.insertBefore(host,right);return true;}return false;}
 if(!place()){host.style.display='none';document.body.appendChild(host);}
 const observer=new MutationObserver(()=>{if(place())host.style.display='inline-flex';});observer.observe(document.body,{childList:true,subtree:true});
 window.__leigodAutoPauseLayout=()=>{const rect=n=>{const r=n?.getBoundingClientRect();return r?{left:r.left,right:r.right,top:r.top,bottom:r.bottom,width:r.width}:null;};const a=anchors();return {host:rect(host),search:rect(a.search),time:rect(a.time),parent:host.parentElement?.className,visible:!!host.offsetWidth,located:!!a.nav&&!!a.search&&!!a.time};};
 window.__leigodAutoPauseUiSelfTest=()=>{
  const saved=localStorage.getItem('leigod-autopause-v2'),savedCurrent=current,savedCancel=window.__leigodAutoPauseCancel;
  try{open();if(el('overlay').hidden||document.elementFromPoint(window.innerWidth/2,window.innerHeight/2)!==portal)throw new Error('panel-occluded');el('exit-options').children[times.indexOf(15000)].click();if(JSON.parse(localStorage.getItem('leigod-autopause-v2')).exitMs!==15000)throw new Error('time-selection');el('enabled').click();if(JSON.parse(localStorage.getItem('leigod-autopause-v2')).enabled===JSON.parse(saved).enabled)throw new Error('toggle');close();if(!el('overlay').hidden)throw new Error('panel-close');return {ok:true,timeSelection:true,toggle:true,panel:true,occlusion:true,layout:window.__leigodAutoPauseLayout()};}
  finally{localStorage.setItem('leigod-autopause-v2',saved);config=JSON.parse(saved);current=savedCurrent;window.__leigodAutoPauseCancel=savedCancel;for(const key of ['enabled','startupEnabled','closePause','shutdownPause','notifications'])el(key).checked=!!config[key];choices('exit-options','exitMs');choices('startup-options','startupMs');close();}
 };
 return window.__leigodAutoPauseLayout();
}
module.exports={snapshot,gameInfo,mount};
