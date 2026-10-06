const {Client}=require('./sdk/node_modules/openrgb-sdk');
const c=new Client('My Canvas Gen2 discovery',6743,'127.0.0.1',{forceProtocolVersion:3});
const timer=setTimeout(()=>{console.error('Gen2 discovery timed out');process.exit(1)},20000);
(async()=>{
 await c.connect();const d=await c.getControllerData(0);
 if(await c.getControllerCount()!==1||d.name!=='X870 EAGLE WIFI7')throw Error('Unexpected controller');
 const text=Buffer.from(JSON.stringify({gen2_d_led1:true,gen2_d_led2:true,gen2_d_led3:true,persist_lighting_on_exit:false,calibration_enabled:false})+'\0');
 const length=Buffer.alloc(2);length.writeUInt16LE(text.length);
 c.sendMessage(1130,Buffer.concat([length,text]),0);
 const after=await c.getControllerData(0);
 console.log(JSON.stringify(after.zones));await c.disconnect();clearTimeout(timer);
})().catch(e=>{console.error(e);process.exit(1)});
