'use strict';
const fs=require('fs'),rawFs=require('original-fs'),path=require('path'),crypto=require('crypto');
const root=path.dirname(process.execPath),folder=path.join(process.resourcesPath,'leigod-auto-pause');
function inside(relative){if(typeof relative!=='string'||path.isAbsolute(relative)||relative.includes(':')||relative.split(/[\\/]/).some(x=>!x||x==='.'||x==='..'))throw new Error('invalid-profile-path');const full=path.resolve(root,relative);if(!full.toLowerCase().startsWith((root+path.sep).toLowerCase()))throw new Error('profile-outside-client');return full;}
let profile=null;try{profile=JSON.parse(fs.readFileSync(path.join(folder,'client-profile.json'),'utf8'));}catch{}
let native=profile?.nativeRelative;
if(!native){try{native=JSON.parse(fs.readFileSync(path.join(folder,'installation.json'),'utf8')).NativeRelative;}catch{}}
if(!native)native='resources/app.asar.unpacked/node_modules/@leigod-rs/win32-node-addon-win32-ia32-msvc/win32-node-addon.win32-ia32-msvc.node';
module.exports=require(inside(native));global.__leigodAutoPauseOriginalAddon=module.exports;
try{
 if(profile?.schema!==1||!profile.files||Object.keys(profile.files).length<3)throw new Error('profile-missing');
 for(const [relative,expected]of Object.entries(profile.files)){if(!/^[a-f0-9]{64}$/.test(expected)||crypto.createHash('sha256').update(rawFs.readFileSync(inside(relative))).digest('hex')!==expected)throw new Error('client-changed');}
 global.__leigodAutoPauseClientProfile=profile;require('./main.cjs');
}catch{const {app,Notification}=require('electron');app.whenReady().then(()=>new Notification({title:'自动暂停等待重新适配',body:'客户端文件已变化或配置缺失；正常退出雷神后，后台会重新寻找入口并执行功能自检。'}).show());}
