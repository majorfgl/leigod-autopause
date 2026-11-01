'use strict';
class Monitor{
 constructor(){this.reset();this.lastCancel=0;}
 reset(){this.account='';this.gameId=0;this.seen=false;this.started=null;this.deadline=null;this.phase='';this.busy=false;this.failed=false;this.snoozed=false;this.epoch=(this.epoch||0)+1;}
 reconfigure(before,after){
  if(before.detectMode!==after.detectMode||JSON.stringify(before.processMap)!==JSON.stringify(after.processMap)){this.reset();return 'process-reset';}
  if(before.exitMs!==after.exitMs||before.startupMs!==after.startupMs||before.startupEnabled!==after.startupEnabled){this.deadline=null;this.phase='';this.epoch++;return 'wait-reset';}
  return 'preserved';
 }
 step(s,now){
  const result=state=>({state,remaining:this.deadline===null?0:Math.max(0,this.deadline-now),due:state==='due',phase:this.phase,gameId:this.gameId});
  if(!s.config.enabled){this.reset();return result('disabled');}
  if(!s.known){this.reset();return result('unknown');}
  if(!s.loggedIn||!s.account){this.reset();return result('login');}
  if(this.account&&this.account!==s.account){this.reset();this.account=s.account;return result('account-changed');}
  this.account=s.account;
  if(s.timeStatus==='pause'){this.reset();return result('paused');}
  if(s.timeStatus!=='timeing'){this.reset();return result('unknown');}
  if(s.gameId&&this.gameId&&s.gameId!==this.gameId){this.reset();this.account=s.account;}
  if(s.excluded){this.reset();return result('excluded');}
  if(s.gameId&&!this.gameId){this.gameId=s.gameId;this.started=now;this.deadline=null;this.phase='';this.snoozed=false;}
  if(!this.gameId){
   if(s.cancelNonce&&s.cancelNonce!==this.lastCancel){this.lastCancel=s.cancelNonce;this.snoozed=true;this.deadline=null;this.epoch++;}
   if(this.snoozed)return result('snoozed');
   if(this.busy)return result('pausing');
   if(this.failed)return result('failed');
   if(!s.config.startupEnabled)return result('waiting-game');
   if(this.deadline===null){this.phase='startup';this.deadline=now+s.config.startupMs;}
   return result(now>=this.deadline?'due':'startup');
  }
  if(!s.processNames.length){this.deadline=null;this.seen=false;return result('missing');}
  if(s.process==='error'){this.deadline=null;this.seen=false;this.started=now;this.epoch++;return result('unknown');}
  if(s.cancelNonce&&s.cancelNonce!==this.lastCancel){this.lastCancel=s.cancelNonce;this.snoozed=true;this.deadline=null;this.epoch++;}
  if(s.process==='running'){
   this.seen=true;this.deadline=null;this.phase='';this.failed=false;this.snoozed=false;
   return result('running');
  }
  if(this.snoozed)return result('snoozed');
  if(this.busy)return result('pausing');
  if(this.failed)return result('failed');
  const phase=this.seen?'exit':'startup';
  if(phase==='startup'&&!s.config.startupEnabled)return result('waiting-game');
  const duration=phase==='exit'?s.config.exitMs:s.config.startupMs;
  if(this.deadline===null||this.phase!==phase){this.phase=phase;this.deadline=now+duration;}
  return result(now>=this.deadline?'due':phase==='exit'?'countdown':'startup');
 }
 begin(){this.busy=true;return this.epoch;}
 fail(){this.busy=false;this.failed=true;this.deadline=null;}
}
module.exports={Monitor};
