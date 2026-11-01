'use strict';
const TIMES=[0,15000,30000,60000,180000,300000,600000,900000];
const DEFAULTS={enabled:true,exitMs:300000,startupMs:180000,startupEnabled:true,closePause:true,shutdownPause:false,notifications:true,detectMode:'native',processMap:{}};
const EXCLUDED=new Set([109,437,274,1921,1342,860,2529,4371]);
function names(value){return [...new Set((Array.isArray(value)?value:String(value||'').split(/[,;\n\r，；]+/)).map(s=>String(s).trim()).filter(s=>s&&s.length<=160&&/\.exe$/i.test(s)&&!/[\\/:*?"<>|]/.test(s)))].slice(0,20);}
function config(raw={}){
 const out={...DEFAULTS,processMap:{}};
 for(const k of ['enabled','startupEnabled','closePause','shutdownPause','notifications'])if(typeof raw[k]==='boolean')out[k]=raw[k];
 for(const k of ['exitMs','startupMs'])if(TIMES.includes(raw[k]))out[k]=raw[k];
 if(['native','compat'].includes(raw.detectMode))out.detectMode=raw.detectMode;
 for(const [id,v] of Object.entries(raw.processMap||{}))if(/^\d{1,9}$/.test(id)){const n=names(v);if(n.length)out.processMap[id]=n;}
 return out;
}
module.exports={TIMES,DEFAULTS,EXCLUDED,names,config};
