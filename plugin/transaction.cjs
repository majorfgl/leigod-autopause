'use strict';
async function pauseTransaction({guard,processState,invoke,sleep,log,targetGameId,closing=false}){
 let stopFailed=false;
 for(let attempt=1;attempt<=3;attempt++){
  try{
   let s=await guard();
   if(s.timeStatus==='timeing'){
    if(s.notCanPause>0)throw new Error('unpausable-time');
    if(s.gameId&&s.gameId===targetGameId){try{stopFailed=!(await invoke('stop-acc',{reason:'other'})).ok||stopFailed;}catch{stopFailed=true;}}
    s=await guard();
    if(s.timeStatus==='timeing'){
     if(!closing&&await processState()!=='exited')throw new Error('cancelled');
     const r=await invoke('pause-user-time');log('pause-response',{attempt,ok:r.ok,code:typeof r.code==='number'?r.code:null});
    }
   }
   const first=await guard();if(first.timeStatus!=='pause'||first.total===null)throw new Error('not-confirmed');
   await sleep(closing?800:3000);
   const second=await guard();if(second.timeStatus!=='pause'||second.total!==first.total)throw new Error('time-not-stable');
   return {status:'confirmed',stopFailed};
  }catch(e){
   const category=['cancelled','unpausable-time','state-unknown','not-confirmed','time-not-stable','timeout','refresh-failed'].includes(e.message)?e.message:'interface-error';log('pause-attempt',{attempt,category});
   if(category==='cancelled')return {status:'cancelled'};
   if(category==='unpausable-time')break;
   if(attempt<3)await sleep(closing?300:2000);
  }
 }
 return {status:'failed'};
}
module.exports={pauseTransaction};
