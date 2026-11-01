'use strict';
function routes(handlers){
 const keys=handlers&&typeof handlers.keys==='function'&&typeof handlers.has==='function'?[...handlers.keys()]:[];
 const matches=keys.filter(k=>typeof k==='string'&&k.endsWith('refresh-user-time-info')).map(k=>k.slice(0,-'refresh-user-time-info'.length)).filter(prefix=>handlers.has(prefix+'pause-user-time'));
 if(matches.length!==1)return {ok:false,reason:matches.length?'ambiguous-billing-interface':'billing-interface-missing'};
 const prefix=matches[0];return {ok:true,prefix,stop:handlers.has(prefix+'stop-acc'),quit:handlers.has(prefix+'quit-app')};
}
function bridge(){for(const key of ['leigodSimplify','leigodAPI','leigod','electronAPI'])if(typeof window[key]?.invoke==='function')return key;const candidates=Object.entries(Object.getOwnPropertyDescriptors(window)).filter(([,d])=>d.value&&typeof d.value==='object'&&typeof Object.getOwnPropertyDescriptor(d.value,'invoke')?.value==='function').map(([key])=>key);return candidates.length===1?candidates[0]:null;}
async function detectProcesses(list,mode,native,compatible){
 if(!list.length)return {state:'error',fallback:false};
 if(mode==='compat')return {state:await compatible(list),fallback:false};
 try{if(typeof native?.isProcessRunning!=='function')throw Error('missing');let unknown=false;for(const name of list){const r=native.isProcessRunning(name,'');if(r===true)return {state:'running',fallback:false};if(r!==false)unknown=true;}if(!unknown)return {state:'exited',fallback:false};}catch{}
 return {state:await compatible(list),fallback:true};
}
module.exports={routes,bridge,detectProcesses};
