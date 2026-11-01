'use strict';
function request(token){return {args:['--silent','--show-error','--connect-timeout','1','--max-time','2','--max-redirs','0','--proto','=https','-H','Content-Type: application/json','--data-binary','@-','https://webapi.leigod.com/api/user/pause'],options:{input:JSON.stringify({account_token:token,lang:'zh_CN'}),encoding:'utf8',timeout:2500,windowsHide:true}};}
function attempt(snapshot,exec,executable){
 if(snapshot?.config.enabled===false||!snapshot?.config.shutdownPause||!snapshot.loggedIn||snapshot.timeStatus!=='timeing'||!snapshot.shutdownToken)return {attempted:false};
 try{const r=request(snapshot.shutdownToken),body=JSON.parse(exec(executable,r.args,r.options));return {attempted:true,accepted:body.code===0||body.code===400803,code:typeof body.code==='number'?body.code:null};}
 catch{return {attempted:true,accepted:false};}
}
module.exports={request,attempt};
