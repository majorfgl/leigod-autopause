'use strict';
const path=require('path'),{execFile}=require('child_process');
function compatCheck(list,execute=execFile){
 if(!list.length)return Promise.resolve('error');
 // Let Windows compare the Unicode image name. Only parse numeric CSV PIDs,
 // so non-ASCII process names are not damaged by an OEM output code page.
 return Promise.all(list.map(name=>new Promise(resolve=>{
  execute(path.join(process.env.SystemRoot||'C:/Windows','System32/tasklist.exe'),['/FI','IMAGENAME eq '+name,'/FO','CSV','/NH'],{windowsHide:true,timeout:4000,encoding:'utf8',maxBuffer:1024*1024},(err,out)=>{
   if(err||typeof out!=='string'||!out.trim()){resolve('error');return;}
   if(/^\uFEFF?"[^"\r\n]+","\d+"/m.test(out)){resolve('running');return;}
   const message=out.trim();
   resolve(message.length<1024&&/^[^"\r\n:]{1,80}:\s*\S/.test(message)&&!/^\s*(ERROR|WARNING|FEHLER|ERREUR)\b/i.test(message)?'exited':'error');
  });
 }))).then(results=>results.includes('running')?'running':results.includes('error')?'error':'exited');
}
module.exports={compatCheck};
