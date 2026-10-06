// Only use against the restricted local OpenRGB instance after GCC has exited.
const {Client}=require('./sdk/node_modules/openrgb-sdk');
const fs=require('fs');
const {execFileSync}=require('child_process');
const colorArg=process.argv[2]||'64E2C1';
if(!/^[0-9a-f]{6}$/i.test(colorArg)&&colorArg!=='restore')throw Error('Expected RGB hex or restore');
const gcc=execFileSync('powershell.exe',['-NoProfile','-Command','@(Get-Process GCC -ErrorAction SilentlyContinue).Count'],{encoding:'utf8'}).trim();
if(gcc!=='0')throw Error('GCC is still running; do not share controller ownership');
const client=new Client('My Canvas lighting test',6743,'127.0.0.1',{forceProtocolVersion:3});
const timer=setTimeout(()=>{console.error('SDK timeout');process.exit(1)},15000);
(async()=>{
 await client.connect();
 const n=await client.getControllerCount();
 if(n!==1)throw Error('Restricted instance must have exactly one controller');
 let d=await client.getControllerData(0);
 if(d.name!=='X870 EAGLE WIFI7'||d.vendor!=='Gigabyte'||!d.description.startsWith('IT5711-'))throw Error('Unexpected controller');
 // One virtual LED per ARGB header enables uniform built-in effects; this is
 // not an assertion of physical LED count and is not used for per-LED streaming.
 for(const z of d.zones.filter(z=>/^ARGB_V2_[123]$/.test(z.name)&&z.ledsCount===0))client.resizeZone(0,z.id,1);
 d=await client.getControllerData(0);
 if(colorArg==='restore'){
  // GCC's original selected built-in effect is wave 3 / effect value 11.
  const wave=d.modes.find(m=>m.value===11);
  if(!wave)throw Error('Original wave effect unavailable');
  await client.updateMode(0,{id:wave.id,brightness:255});
 }else{
  const color={red:parseInt(colorArg.slice(0,2),16),green:parseInt(colorArg.slice(2,4),16),blue:parseInt(colorArg.slice(4,6),16)};
  if(process.argv[3]==='direct'){ await client.updateMode(0,{name:'Direct',brightness:160}); client.updateLeds(0,Array(d.colors.length).fill(color)); } else { await client.updateMode(0,{name:'Static',brightness:160,colors:[color]}); }
 }
 const after=await client.getControllerData(0);
 const state={at:new Date().toISOString(),requested:colorArg,device:after.name,mode:after.modes[after.activeMode]};
 fs.appendFileSync('work/lighting/test-results.jsonl',JSON.stringify(state)+'\n');
 console.log(JSON.stringify(state));
 await client.disconnect();clearTimeout(timer);
})().catch(e=>{console.error(e);process.exit(1)});
