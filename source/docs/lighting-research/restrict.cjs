const fs=require('fs');
const p='work/lighting/config/OpenRGB.json';
const c=JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));
const names=Object.keys(c.Detectors.detectors);
if(!names.includes('Gigabyte RGB Fusion 2 USB')) throw Error('Target detector missing');
for(const n of names)c.Detectors.detectors[n]=n==='Gigabyte RGB Fusion 2 USB';
c.Detectors.initial_detection_delay_ms=0;
fs.writeFileSync(p,JSON.stringify(c,null,2));
console.log('Configured',names.length,'detectors; enabled only Gigabyte RGB Fusion 2 USB');
