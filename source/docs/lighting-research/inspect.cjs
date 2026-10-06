const {Client}=require('./sdk/node_modules/openrgb-sdk');
const fs=require('fs');
const client=new Client('My Canvas lighting test',6743,'127.0.0.1',{forceProtocolVersion:3});
const timer=setTimeout(()=>{console.error('SDK timeout');process.exit(1)},12000);
(async()=>{await client.connect();const n=await client.getControllerCount();const devices=[];for(let i=0;i<n;i++)devices.push(await client.getControllerData(i));fs.writeFileSync('work/lighting/devices.json',JSON.stringify(devices,null,2));console.log(JSON.stringify(devices,null,2));await client.disconnect();clearTimeout(timer)})().catch(e=>{console.error(e);process.exit(1)});
