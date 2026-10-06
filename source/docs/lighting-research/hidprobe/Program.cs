using System.Runtime.InteropServices;
using System.Text.Json;
NativeLibrary.SetDllImportResolver(typeof(Hid).Assembly,(name,assembly,path)=>name=="hidapi"?NativeLibrary.Load(Path.GetFullPath("work/lighting/OpenRGB/OpenRGB Windows 64-bit/hidapi-hotplug.dll")):name=="ghid"?NativeLibrary.Load(@"C:\Program Files\GIGABYTE\Control Center\GHidApi.dll"):IntPtr.Zero);
if(args.Contains("--probe7")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void Apply(){var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;Vendor.Write(0x048D,0x5711,0,a,64);}
 void Zone(int d,byte mode,byte bri,byte r,byte g,byte b,ushort p0=0,ushort p1=0,ushort p2=0){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=mode;e[12]=bri;e[14]=b;e[15]=g;e[16]=r;
  BitConverter.GetBytes(p0).CopyTo(e,18);BitConverter.GetBytes(p1).CopyTo(e,20);BitConverter.GetBytes(p2).CopyTo(e,22);
  Vendor.Write(0x048D,0x5711,0,e,64);
 }
 void OffOthers(){for(int z=0;z<=10;z++)if(z!=7)Zone(z,1,255,0,0,0);}
 OffOthers(); Zone(7,1,255,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 1/4 zone7 static red FULL"); Thread.Sleep(6000);
 Zone(7,1,64,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 2/4 zone7 static red 25%"); Thread.Sleep(6000);
 Zone(7,2,255,255,0,0,800,800,200); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 3/4 zone7 BREATHING red"); Thread.Sleep(6000);
 Zone(7,4,255,0,0,0,700); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 4/4 zone7 COLOR CYCLE"); Thread.Sleep(6000);
 for(int z=0;z<=10;z++)Zone(z,1,255,255,107,53); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} restore all orange");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("probe7 done");
 return;
}
if(args.Contains("--probe6")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void Apply(){var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;Vendor.Write(0x048D,0x5711,0,a,64);}
 void Zone(int d,byte mode,byte bri,byte r,byte g,byte b,ushort p0=0,ushort p1=0,ushort p2=0){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=mode;e[12]=bri;e[14]=b;e[15]=g;e[16]=r;
  BitConverter.GetBytes(p0).CopyTo(e,18);BitConverter.GetBytes(p1).CopyTo(e,20);BitConverter.GetBytes(p2).CopyTo(e,22);
  Vendor.Write(0x048D,0x5711,0,e,64);
 }
 void OffOthers(){for(int z=0;z<=10;z++)if(z!=6)Zone(z,1,255,0,0,0);}
 OffOthers(); Zone(6,1,255,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 1/4 zone6 static red FULL"); Thread.Sleep(6000);
 Zone(6,1,64,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 2/4 zone6 static red 25%"); Thread.Sleep(6000);
 Zone(6,2,255,255,0,0,800,800,200); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 3/4 zone6 BREATHING red"); Thread.Sleep(6000);
 Zone(6,4,255,0,0,0,700); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 4/4 zone6 COLOR CYCLE"); Thread.Sleep(6000);
 for(int z=0;z<=10;z++)Zone(z,1,255,255,107,53); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} restore all orange");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("probe6 done");
 return;
}
if(args.Contains("--probe5")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void Apply(){var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;Vendor.Write(0x048D,0x5711,0,a,64);}
 void Zone(int d,byte mode,byte bri,byte r,byte g,byte b,ushort p0=0,ushort p1=0,ushort p2=0){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=mode;e[12]=bri;e[14]=b;e[15]=g;e[16]=r;
  BitConverter.GetBytes(p0).CopyTo(e,18);BitConverter.GetBytes(p1).CopyTo(e,20);BitConverter.GetBytes(p2).CopyTo(e,22);
  Vendor.Write(0x048D,0x5711,0,e,64);
 }
 void OffOthers(){for(int z=0;z<=10;z++)if(z!=5)Zone(z,1,255,0,0,0);}
 OffOthers(); Zone(5,1,255,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 1/4 static red FULL brightness"); Thread.Sleep(6000);
 Zone(5,1,64,255,0,0); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 2/4 static red LOW brightness (25%)"); Thread.Sleep(6000);
 Zone(5,2,255,255,0,0,800,800,200); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 3/4 BREATHING red"); Thread.Sleep(6000);
 Zone(5,4,255,0,0,0,700); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} 4/4 COLOR CYCLE"); Thread.Sleep(6000);
 OffOthers(); Zone(5,1,255,255,107,53); Apply();
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} restore orange");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("probe5 done");
 return;
}
if(args.Contains("--find-zone")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void WriteZone(int d,byte r,byte g,byte b){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=1;e[12]=255;e[14]=b;e[15]=g;e[16]=r;
  Vendor.Write(0x048D,0x5711,0,e,64);
 }
 void Apply(){var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;Vendor.Write(0x048D,0x5711,0,a,64);}
 foreach(int d in new[]{5,6,7}){
  for(int z=0;z<=10;z++)WriteZone(z,0,0,0);   // all off
  WriteZone(d,255,0,0);                         // only this zone red
  Apply();
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} ZONE {d} = RED (others off)");
  Thread.Sleep(6000);
 }
 for(int z=0;z<=10;z++)WriteZone(z,0,0,0);Apply();
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("find-zone done");
 return;
}
if(args.Contains("--hold")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 for(int d=0;d<=10;d++){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=1;e[12]=255;e[14]=0;e[15]=0;e[16]=255;   // red
  Vendor.Write(0x048D,0x5711,0,e,64);
  Thread.Sleep(15);
 }
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;
 Vendor.Write(0x048D,0x5711,0,a,64);
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} set RED, holding 12s (no more writes)");
 Thread.Sleep(12000);
 Console.WriteLine($"{DateTime.Now:HH:mm:ss} still holding, now disconnect");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("hold done");
 return;
}
if(args.Contains("--all-color")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void SetAll(byte r,byte g,byte b,string name){
  for(int d=0;d<=10;d++){
   var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
   BitConverter.GetBytes(1u<<d).CopyTo(e,2);
   e[11]=1;e[12]=255;e[14]=b;e[15]=g;e[16]=r;
   Vendor.Write(0x048D,0x5711,0,e,64);
   Thread.Sleep(15);
  }
  var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;a[3]=0x00;
  Vendor.Write(0x048D,0x5711,0,a,64);
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} ALL {name}");
  Thread.Sleep(5000);
 }
 SetAll(255,0,0,"RED");
 SetAll(0,255,0,"GREEN");
 SetAll(0,0,255,"BLUE");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("all-color done");
 return;
}
if(args.Contains("--identify-zone")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 void Show(int d,byte r,byte g,byte b,string name){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=1;e[12]=255;e[14]=b;e[15]=g;e[16]=r;
  Vendor.Write(0x048D,0x5711,0,e,64);
  var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=(byte)d;a[3]=0x00;
  Vendor.Write(0x048D,0x5711,0,a,64);
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} div={d} {name}");
  Thread.Sleep(5000);
 }
 Show(8,255,0,0,"RED");
 Show(9,0,255,0,"GREEN");
 Show(10,0,0,255,"BLUE");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("identify done");
 return;
}
if(args.Contains("--sweep-div")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 var en=new byte[64];en[0]=0xCC;en[1]=0x32;Vendor.Write(0x048D,0x5711,0,en,64);
 for(int d=0;d<=10;d++){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+d);
  BitConverter.GetBytes(1u<<d).CopyTo(e,2);
  e[11]=1;e[12]=255;e[14]=0;e[15]=0;e[16]=255;   // static, bright red
  int w=Vendor.Write(0x048D,0x5711,0,e,64);
  var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=(byte)d;a[3]=0x00;
  int aw=Vendor.Write(0x048D,0x5711,0,a,64);
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} div={d} write={w} apply={aw}  <-- watch now");
  Thread.Sleep(2500);
 }
 var ra=new byte[64];ra[0]=0xCC;ra[1]=0x28;ra[2]=0xFF;ra[3]=0x00;Vendor.Write(0x048D,0x5711,0,ra,64);
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("sweep done");
 return;
}
if(args.Contains("--audio")){
 try{
  var en=(IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
  en.GetDefaultAudioEndpoint(0,0,out IMMDevice dev);
  Guid iid=typeof(IAudioMeterInformation).GUID;
  dev.Activate(ref iid,1,IntPtr.Zero,out object act);
  var meter=(IAudioMeterInformation)act;
  Console.WriteLine("meter ready, sampling 8s... play some sound");
  for(int i=0;i<80;i++){
   meter.GetPeakValue(out float p);
   if(i%10==0||p>0.001) Console.WriteLine($"{DateTime.Now:HH:mm:ss.ff} peak={p:0.000}");
   Thread.Sleep(100);
  }
 }catch(Exception ex){Console.WriteLine("audio error: "+ex.Message);}
 return;
}
if(args.Contains("--devices")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 var devs=new ConnectedDevices[16];
 int n=Vendor.GetList(0x048D,0x5711,devs);
 Console.WriteLine("connectedList="+n);
 for(int i=0;i<n;i++){
  Console.WriteLine(JsonSerializer.Serialize(new{index=i,devNum=devs[i].DevNum,device=devs[i].Device}));
 }
 Vendor.Disconnect(0x048D,0x5711,0);
 return;
}
if(args.Contains("--drgb")){
 // DeepRGB (DRGBmod) asynchronous ARGB controller, OpenRGB "version 4" protocol.
 // 16 channels x 256 LEDs max. Output report is 1025 bytes (id 0x00 + 1024).
 Hid.Init();
 string? target=null;
 var list=Hid.Enumerate(0,0);
 try{
  for(var cur=list;cur!=IntPtr.Zero;){
   var fi=Marshal.PtrToStructure<HidInfo>(cur);
   if(fi.vendor==0x2486&&fi.product==0x3217){target=Marshal.PtrToStringUTF8(fi.path);break;}
   cur=fi.next;
  }
 }finally{Hid.FreeEnumeration(list);}
 if(target==null)throw new Exception("DeepRGB device not found");
 Console.WriteLine("path="+target);
 IntPtr dev=Hid.Open(target);
 if(dev==IntPtr.Zero)throw new Exception("open failed");

 const int channels=16, perChannel=32;
 const int total=channels*perChannel;           // 512 LEDs
 var rgb=new byte[2048];
 for(int ch=0;ch<channels;ch++){ rgb[ch*2]=0; rgb[ch*2+1]=(byte)perChannel; }
 for(int i=0;i<total;i++){ rgb[72+i*3+0]=0; rgb[72+i*3+1]=0; rgb[72+i*3+2]=255; } // B,G,R

 int colPackets=1;
 if(total>316) colPackets=1+((total-316)/340)+((((total-316)%340)>0)?1:0);
 Console.WriteLine($"total={total} packets={colPackets}");

 var packets=new List<byte[]>();
 int remaining=total;
 for(int i=0;i<colPackets;i++){
  var b=new byte[1025];
  b[0]=0x00; b[1]=(byte)(i+100); b[2]=(byte)(colPackets+99);
  int hi=remaining/256>=1?1:0;
  int lo=remaining>=316?60:(remaining%256);
  b[3]=(byte)hi; b[4]=(byte)lo;
  Array.Copy(rgb,i*1020,b,5,1020);
  if(remaining>0) remaining=remaining<=340?0:remaining-340;
  packets.Add(b);
 }

 int sent=0, fails=0; var first=new List<string>();
 var sw=System.Diagnostics.Stopwatch.StartNew();
 while(sw.ElapsedMilliseconds<12000){
  foreach(var p in packets){
   int len=p.Length;
   int r=Hid.Write(dev,p,(nuint)len);
   if(first.Count<4) first.Add($"len={len} ret={r} err={Marshal.PtrToStringUni(Hid.Error(dev))}");
   if(r<0)fails++; else sent++;
  }
  Thread.Sleep(250);
 }
 foreach(var f in first) Console.WriteLine("write "+f);
 // 65-byte keepalive report used by the OpenRGB driver's idle thread
 var ka=new byte[65]; ka[0]=0x00; ka[1]=0x65;
 Console.WriteLine("keepalive ret="+Hid.Write(dev,ka,(nuint)ka.Length));
 Console.WriteLine(JsonSerializer.Serialize(new{outputs=sent,failures=fails,error=Marshal.PtrToStringUni(Hid.Error(dev))}));
 Hid.Close(dev);Hid.Exit();
 Console.WriteLine("drgb done (left showing red; will revert ~1s after stop)");
 return;
}
if(args.Contains("--list-all")){
 Hid.Init();
 var list=Hid.Enumerate(0,0);
 try{
  for(var cur=list;cur!=IntPtr.Zero;){
   var fi=Marshal.PtrToStructure<HidInfo>(cur);
   Console.WriteLine(JsonSerializer.Serialize(new{
    vid=$"0x{fi.vendor:X4}",pid=$"0x{fi.product:X4}",
    usagePage=$"0x{fi.usagePage:X4}",usage=$"0x{fi.usage:X4}",
    iface=fi.usbInterface,
    manufacturer=Marshal.PtrToStringUni(fi.manufacturer),
    product=Marshal.PtrToStringUni(fi.productName)
   }));
   cur=fi.next;
  }
 }finally{Hid.FreeEnumeration(list);Hid.Exit();}
 return;
}
if(args.Contains("--direct-red")){
 // Per-LED direct control (OpenRGB "Direct" path).
 // Disable built-in effects on every ARGB header, declare LED counts, then
 // stream raw LED data packets to each header.
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 void W(string label,byte[] pkt){
  int w=Vendor.Write(0x048D,0x5711,0,pkt,64);
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} {label} write={w} {Convert.ToHexString(pkt.AsSpan(0,16))}");
  Thread.Sleep(30);
 }
 // 0x32 with mask 0x7B = built-in effects OFF for all six headers.
 var off=new byte[64];off[0]=0xCC;off[1]=0x32;off[2]=0x7B;W("effects-off",off);
 // 0x34 LED-count declaration; 0 in each nibble = LEDS_32 (32 LEDs/header).
 var cnt=new byte[64];cnt[0]=0xCC;cnt[1]=0x34;W("led-count",cnt);
 byte[] headers={0x58,0x59,0x62,0x63,0x64,0x65};
 var frames=new List<byte[]>();
 foreach(byte hdr in headers){
  int total=57, done=0;
  while(done<total){
   int n=Math.Min(19,total-done);
   var p=new byte[64];
   p[0]=0xCC;p[1]=hdr;
   BitConverter.GetBytes((ushort)(done*3)).CopyTo(p,2);
   p[4]=(byte)(n*3);
   for(int i=0;i<n;i++){
    int off5=i*3+5;
    p[off5+0]=0;    // blue  (BGR order)
    p[off5+1]=0;    // green
    p[off5+2]=255;  // red
   }
   frames.Add(p);
   done+=n;
  }
 }
 var ap=new byte[64];ap[0]=0xCC;ap[1]=0x28;ap[2]=0xFF;ap[3]=0x07;
 var sw=System.Diagnostics.Stopwatch.StartNew();
 while(sw.ElapsedMilliseconds<15000){
  foreach(var p in frames)Vendor.Write(0x048D,0x5711,0,p,64);
  Vendor.Write(0x048D,0x5711,0,ap,64);
  Thread.Sleep(200);
 }
 Console.WriteLine($"direct frames={frames.Count} looped for 15s");
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("direct done");
 return;
}
if(args.Contains("--mode-sweep")){
 // Correct GCC/OpenRGB effect layout:
 //   [1]=header [2..5]=zone0 [6..9]=zone1 [10]=reserved
 //   [11]=effect_type [12]=max_brightness [13]=min_brightness
 //   [14]=blue [15]=green [16]=red  [18..]=period0..period3
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 void Send(string label,byte[] pkt){
  int w=Vendor.Write(0x048D,0x5711,0,pkt,64);
  Console.WriteLine($"{DateTime.Now:HH:mm:ss} {label} write={w} {Convert.ToHexString(pkt.AsSpan(0,24))}");
  Thread.Sleep(80);
 }
 byte[] Effect(byte mode,byte r,byte g,byte b,ushort p0=0,ushort p1=0,ushort p2=0){
  var e=new byte[64];
  e[0]=0xCC;e[1]=0x20;
  BitConverter.GetBytes(0x07FFu).CopyTo(e,2);
  e[11]=mode;e[12]=255;e[13]=0;
  e[14]=b;e[15]=g;e[16]=r;
  BitConverter.GetBytes(p0).CopyTo(e,18);
  BitConverter.GetBytes(p1).CopyTo(e,20);
  BitConverter.GetBytes(p2).CopyTo(e,22);
  return e;
 }
 var apply=new byte[64];apply[0]=0xCC;apply[1]=0x28;apply[2]=0xFF;apply[3]=0x07;
 var enable=new byte[64];enable[0]=0xCC;enable[1]=0x32;
 Send("enable-builtin-effect",enable);
 Send("static-red",Effect(1,255,0,0));  Send("apply",apply); Thread.Sleep(6000);
 Send("colorcycle",Effect(4,0,0,0,700));Send("apply",apply); Thread.Sleep(6000);
 Send("breathing-green",Effect(2,0,255,0,1000,1000,200)); Send("apply",apply); Thread.Sleep(6000);
 Send("wave-restore",Effect(9,0,0,0));  Send("apply",apply);
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("sweep done");
 return;
}
if(args.Contains("--all-static-orange")){
 // Try ALL zones with static orange, one at a time.
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 for(int zone=0;zone<8;zone++){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+zone);
  BitConverter.GetBytes(1u<<zone).CopyTo(e,2);
  e[10]=1;e[11]=255;e[12]=71;e[13]=179;e[14]=255;
  Console.WriteLine(JsonSerializer.Serialize(new{zone,write=Vendor.Write(0x048D,0x5711,0,e,64)}));
  Thread.Sleep(50);
 }
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;
 Console.WriteLine(JsonSerializer.Serialize(new{apply=Vendor.Write(0x048D,0x5711,0,a,64)}));
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("all done");
 return;
}
if(args.Contains("--breathe-test")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 // Breathing (mode 2) with bright red, brightness 255.
 var e=new byte[64];e[0]=0xCC;e[1]=0x25;
 BitConverter.GetBytes(1u<<5).CopyTo(e,2);
 e[10]=2;e[11]=255;e[12]=0;e[13]=0;e[14]=255; // BGR: blue=0, green=0, red=255
 e[15]=0xE8;e[16]=0x03; // period0=1000ms
 e[17]=0xE8;e[18]=0x03; // period1=1000ms
 e[19]=0xC8;            // period2=200ms
 Console.WriteLine(JsonSerializer.Serialize(new{write=Vendor.Write(0x048D,0x5711,0,e,64),bytes=Convert.ToHexString(e.AsSpan(0,24))}));
 Thread.Sleep(100);
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0x20;
 Console.WriteLine(JsonSerializer.Serialize(new{apply=Vendor.Write(0x048D,0x5711,0,a,64)}));
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("breathe done");
 return;
}
if(args.Contains("--wave-test")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 // GCC's default: Wave 3 (mode 9) on all zones, brightness 255, no color.
 var e=new byte[64];e[0]=0xCC;e[1]=0x25;
 BitConverter.GetBytes(1u<<5).CopyTo(e,2);
 e[10]=9;e[11]=255;e[12]=0;e[13]=0;e[14]=0;
 Console.WriteLine(JsonSerializer.Serialize(new{write=Vendor.Write(0x048D,0x5711,0,e,64),bytes=Convert.ToHexString(e.AsSpan(0,20))}));
 Thread.Sleep(100);
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0x20;
 Console.WriteLine(JsonSerializer.Serialize(new{apply=Vendor.Write(0x048D,0x5711,0,a,64)}));
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("wave done");
 return;
}
if(args.Contains("--calm-orange")){
 // Slow, deliberate write — one effect + one apply, with a pause.
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 Thread.Sleep(200);
 var e=new byte[64];e[0]=0xCC;e[1]=0x25;
 BitConverter.GetBytes(1u<<5).CopyTo(e,2);
 e[10]=1;e[11]=160;e[12]=71;e[13]=179;e[14]=255;
 Console.WriteLine(JsonSerializer.Serialize(new{write=Vendor.Write(0x048D,0x5711,0,e,64),bytes=Convert.ToHexString(e.AsSpan(0,20))}));
 Thread.Sleep(100);
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0x20;
 Console.WriteLine(JsonSerializer.Serialize(new{apply=Vendor.Write(0x048D,0x5711,0,a,64),bytes=Convert.ToHexString(a.AsSpan(0,8))}));
 Thread.Sleep(200);
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("calm done");
 return;
}
if(args.Contains("--restore-rainbow")){
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 // Reset all effect slots to wave (GCC's default), brightness 255.
 foreach(int zone in new[]{0,1,2,3,4,5,6,7}){
  var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+zone);
  BitConverter.GetBytes(1u<<zone).CopyTo(e,2);
  e[10]=9;e[11]=255;e[12]=0;e[13]=0;e[14]=0;
  Vendor.Write(0x048D,0x5711,0,e,64);
  Thread.Sleep(20);
 }
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0xFF;
 Vendor.Write(0x048D,0x5711,0,a,64);
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("restore done");
 return;
}
if(args.Contains("--flood-orange")){
 // Rapid-fire writes to win the race against EasyTuneEngineService's refresh.
 var ls=new ReportLengths[32];
 int c=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,ls);
 Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
 if(c!=1)throw new Exception("Connect failed");
 var e=new byte[64];e[0]=0xCC;e[1]=0x25;
 BitConverter.GetBytes(1u<<5).CopyTo(e,2);
 e[10]=1;e[11]=160;e[12]=71;e[13]=179;e[14]=255;
 var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=0x20;
 for(int i=0;i<300;i++){
  Vendor.Write(0x048D,0x5711,0,e,64);
  Vendor.Write(0x048D,0x5711,0,a,64);
  Thread.Sleep(10);
  if(i%50==0)Console.WriteLine($"flood {i}/300");
 }
 Vendor.Disconnect(0x048D,0x5711,0);
 Console.WriteLine("flood done");
 return;
}
if(args.Contains("--gcc-bottom-orange")||args.Contains("--raw204-bottom-orange")){
 bool matrix=args.Contains("--matrix");
 if(matrix){
  // Static effect packet for zone slot 5 (ARGB_V2_1, GCC pin 0), orange.
  byte[] EffectFor(int zone){
   var e=new byte[64];e[0]=0xCC;e[1]=(byte)(0x20+zone);
   BitConverter.GetBytes(1u<<zone).CopyTo(e,2);
   e[10]=1;e[11]=160;e[12]=71;e[13]=179;e[14]=255;
   return e;
  }
  // GCC's Apply(int) writes exactly this 64-byte report with the zone bits.
  byte[] ApplyFor(int zone){
   var a=new byte[64];a[0]=0xCC;a[1]=0x28;a[2]=(byte)zone;
   if(zone>7)a[3]=(byte)(zone>>8);
   return a;
  }
  var effects=new[]{EffectFor(5)};
  var applies=new[]{ApplyFor(1<<5),ApplyFor(0xFF)};
  foreach(int col in new[]{1,2}){
   Console.WriteLine(JsonSerializer.Serialize(new{collection=col,usagePage=col==1?0xFF89:0xFF89,usage=col==1?0xCC:0xCD}));
   var ls=new ReportLengths[32];
   int c=Vendor.Connect(0x048D,0x5711,0xFF89,(ushort)(col==1?0xCC:0xCD),ls);
   Console.WriteLine(JsonSerializer.Serialize(new{connect=c,featureLength=ls[0].Feature}));
   if(c!=1)continue;
   foreach(var e in effects)Console.WriteLine(JsonSerializer.Serialize(new{write=Vendor.Write(0x048D,0x5711,0,e,64),bytes=Convert.ToHexString(e.AsSpan(0,20))}));
   foreach(var a in applies)Console.WriteLine(JsonSerializer.Serialize(new{apply=Vendor.Write(0x048D,0x5711,0,a,64),bytes=Convert.ToHexString(a.AsSpan(0,8))}));
   Vendor.Disconnect(0x048D,0x5711,0);
  }
  return;
 }
 bool raw=args.Contains("--raw204-bottom-orange");
 var lengths=new ReportLengths[32];
 int connected=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,lengths);
 Console.WriteLine(JsonSerializer.Serialize(new{vendorConnect=connected,featureLength=lengths[0].Feature,inputLength=lengths[0].Input,outputLength=lengths[0].Output}));
 if(connected!=1)throw new Exception("Gigabyte HID wrapper did not connect");
 var query=new byte[204];query[0]=0xCC;query[1]=0x60;
 int infoWrite=Vendor.Write(0x048D,0x5711,0,query,(byte)lengths[0].Feature);
 var reply=new byte[204];reply[0]=0xCC;
 int infoRead=Vendor.Read(0x048D,0x5711,0,reply,(byte)lengths[0].Feature);
 Console.WriteLine(JsonSerializer.Serialize(new{informationWrite=infoWrite,informationRead=infoRead,firmware=Convert.ToHexString(reply.AsSpan(4,4)),replyPrefix=Convert.ToHexString(reply.AsSpan(0,16))}));
 Vendor.Disconnect(0x048D,0x5711,0);
 // GCC stores report id 204 and passes feature length 204. Its wrapper
 // rewrote 204 to 0xCC above, so the raw path sends the unmodified report.
 var effect=new byte[204];
 effect[0]=(byte)(raw?204:0xCC);effect[1]=0x25;
 BitConverter.GetBytes(1u<<5).CopyTo(effect,2);
 effect[11]=1;effect[12]=160;
 effect[14]=71;effect[15]=179;effect[16]=255;
 var commit=new byte[204];
 commit[0]=effect[0];commit[1]=0x28;commit[2]=0x20;
 if(raw){
  Hid.Init();
  IntPtr rawHandle=Hid.OpenFusion();
  try{
   Console.WriteLine(JsonSerializer.Serialize(new{bottomOrangeWrite=Raw.SetFeature(rawHandle,effect,204),error=Marshal.GetLastWin32Error(),prefix=Convert.ToHexString(effect.AsSpan(0,20))}));
   Console.WriteLine(JsonSerializer.Serialize(new{bottomCommit=Raw.SetFeature(rawHandle,commit,204),error=Marshal.GetLastWin32Error(),prefix=Convert.ToHexString(commit.AsSpan(0,8))}));
  }finally{Raw.Close(rawHandle);Hid.Exit();}
 }else{
  int reconnected=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,lengths);
  Console.WriteLine(JsonSerializer.Serialize(new{vendorReconnect=reconnected,bottomOrangeWrite=Vendor.Write(0x048D,0x5711,0,effect,(byte)lengths[0].Feature),prefix=Convert.ToHexString(effect.AsSpan(0,20))}));
  Console.WriteLine(JsonSerializer.Serialize(new{bottomCommit=Vendor.Write(0x048D,0x5711,0,commit,(byte)lengths[0].Feature)}));
  Vendor.Disconnect(0x048D,0x5711,0);
 }
 return;
}
if(args.Contains("--vendor-info")||args.Contains("--vendor-orange")){
 var lengths=new ReportLengths[32];int result=Vendor.Connect(0x048D,0x5711,0xFF89,0xCC,lengths);
 Console.WriteLine(JsonSerializer.Serialize(new{vendorConnect=result,reports=lengths.Take(4).Select(x=>new{x.Feature,x.Input,x.Output})}));
 var query=new byte[64];query[0]=0xCC;query[1]=0x60;
 int sent=Vendor.Write(0x048D,0x5711,0,query,64);
 var reply=new byte[64];reply[0]=0xCC;
 int read=Vendor.Read(0x048D,0x5711,0,reply,64);
 Console.WriteLine(JsonSerializer.Serialize(new{vendorInformationWrite=sent,vendorInformationRead=read,firmwareBytes=Convert.ToHexString(reply.AsSpan(4,4))}));
 if(args.Contains("--vendor-orange")){
var enable=new byte[64];enable[0]=0xCC;enable[1]=0x32;Console.WriteLine("Vendor effect enable="+Vendor.Write(0x048D,0x5711,0,enable,64));
foreach(int slot in new[]{5,6,7}){var effect=new byte[64];effect[0]=0xCC;effect[1]=(byte)(0x20+slot);BitConverter.GetBytes(1u<<slot).CopyTo(effect,2);effect[11]=1;effect[12]=160;effect[14]=71;effect[15]=179;effect[16]=255;Console.WriteLine("Vendor ARGB slot "+slot+" write="+Vendor.Write(0x048D,0x5711,0,effect,64));Thread.Sleep(80);}
var commit=new byte[64];commit[0]=0xCC;commit[1]=0x28;commit[2]=0xE0;Console.WriteLine("Vendor commit="+Vendor.Write(0x048D,0x5711,0,commit,64));
}Vendor.Disconnect(0x048D,0x5711,0);return;
}
using var doc=JsonDocument.Parse(File.ReadAllText("work/lighting/devices.json"));
string path=doc.RootElement[0].GetProperty("location").GetString()![5..];
if(!path.Contains("VID_048D&PID_5711",StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected device");
Hid.Init();
if(args.Contains("--enumerate")){
 var list=Hid.Enumerate(0x048D,0x5711);
 try{for(var cur=list;cur!=IntPtr.Zero;){var info=Marshal.PtrToStructure<HidInfo>(cur);Console.WriteLine(JsonSerializer.Serialize(new{path=Marshal.PtrToStringUTF8(info.path),usagePage=info.usagePage,usage=info.usage,usbInterface=info.usbInterface}));cur=info.next;}}
 finally{Hid.FreeEnumeration(list);Hid.Exit();}return;
}
IntPtr handle=Hid.Open(path);
if(handle==IntPtr.Zero)throw new Exception("HID open failed");
try {
 // Official controller information query; no RGB effect or firmware writes.
 byte[] query=new byte[64];query[0]=0xCC;query[1]=0x60;
 int sent=Hid.Send(handle,query,(nuint)query.Length);
 byte[] reply=new byte[64];reply[0]=0xCC;
 int read=Hid.Get(handle,reply,(nuint)reply.Length);
 Console.WriteLine(JsonSerializer.Serialize(new{informationQueryWrite=sent,informationReplyRead=read,deviceNumber=reply[2],stripDetection=reply[3],argbCounts=new[]{reply[8],reply[9],reply[10]},supportFlags=reply[11],error=Marshal.PtrToStringUni(Hid.Error(handle))}));
 if(args.Contains("--test-orange")) {
  // Same documented IT5711 built-in effect and fast commit commands used by
  // OpenRGB's RGBFusion2USBController. Report every hardware write result.
  var lamp=new byte[64];lamp[0]=0xCC;lamp[1]=0x48;
  WriteChecked("select hardware effects instead of LampArray",lamp);
  var beat=new byte[64];beat[0]=0xCC;beat[1]=0x31;
  WriteChecked("disable beat override",beat);
  var enable=new byte[64];enable[0]=0xCC;enable[1]=0x32;
  WriteChecked("enable built-in effect",enable);
  var effect=new byte[64];effect[0]=0xCC;effect[1]=0x20;
  BitConverter.GetBytes(0x07FFu).CopyTo(effect,2);
  effect[11]=1;effect[12]=160;
  // Packed BGR color field: blue, green, red, reserved.
  effect[14]=71;effect[15]=179;effect[16]=255;
  WriteChecked("uniform orange",effect);
  var apply=new byte[64];apply[0]=0xCC;apply[1]=0x28;apply[2]=0xFF;apply[3]=0x07;
  WriteChecked("commit IT5711 effect",apply);
 }
 void WriteChecked(string label,byte[] packet){
  int result=Hid.Send(handle,packet,(nuint)packet.Length);
  Console.WriteLine(JsonSerializer.Serialize(new{operation=label,bytes=result,error=Marshal.PtrToStringUni(Hid.Error(handle))}));
  if(result!=64)throw new Exception("HID write failed");
  Thread.Sleep(80);
 }
} finally{Hid.Close(handle);Hid.Exit();}
static class Hid {
 [DllImport("hidapi",EntryPoint="hid_enumerate")] public static extern IntPtr Enumerate(ushort vid,ushort pid);
 [DllImport("hidapi",EntryPoint="hid_free_enumeration")] public static extern void FreeEnumeration(IntPtr list);
 [DllImport("hidapi",EntryPoint="hid_init")] public static extern int Init();
 [DllImport("hidapi",EntryPoint="hid_exit")] public static extern int Exit();
 [DllImport("hidapi",EntryPoint="hid_open_path")] public static extern IntPtr Open([MarshalAs(UnmanagedType.LPUTF8Str)]string path);
 [DllImport("hidapi",EntryPoint="hid_close")] public static extern void Close(IntPtr h);
 [DllImport("hidapi",EntryPoint="hid_send_feature_report")] public static extern int Send(IntPtr h,byte[] data,nuint length);
 [DllImport("hidapi",EntryPoint="hid_write")] public static extern int Write(IntPtr h,byte[] data,nuint length);
 [DllImport("hidapi",EntryPoint="hid_get_feature_report")] public static extern int Get(IntPtr h,[In,Out]byte[] data,nuint length);
 [DllImport("hidapi",EntryPoint="hid_error")] public static extern IntPtr Error(IntPtr h);
 public static IntPtr OpenFusion(){
  using var doc=JsonDocument.Parse(File.ReadAllText("work/lighting/devices.json"));
  string path=doc.RootElement[0].GetProperty("location").GetString()![5..];
  if(!path.Contains("VID_048D&PID_5711",StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected device");
  IntPtr handle=Open(path);
  if(handle==IntPtr.Zero)throw new Exception("HID open failed");
  return handle;
 }
}
[StructLayout(LayoutKind.Sequential)]struct HidInfo{
 public IntPtr path;public ushort vendor,product;public IntPtr serial;public ushort release;public IntPtr manufacturer,productName;public ushort usagePage,usage;public int usbInterface;public IntPtr next;public int busType;
}
[StructLayout(LayoutKind.Sequential)]struct ReportLengths{public ushort Feature,Input,Output;}
[StructLayout(LayoutKind.Sequential)]struct McuOnDevice{public int McuId;public int McuIndex;}
[StructLayout(LayoutKind.Sequential)]struct ConnectedDevices{public int DevNum;public McuOnDevice Device;}
static class Vendor{
 [DllImport("ghid",EntryPoint="dllexp_ConnectDevice",CallingConvention=CallingConvention.Cdecl)]public static extern int Connect(ushort vid,ushort pid,ushort usagePage,ushort usage,[In,Out]ReportLengths[] reports);
 [DllImport("ghid",EntryPoint="dllexp_DisconnectDevice",CallingConvention=CallingConvention.Cdecl)]public static extern int Disconnect(ushort vid,ushort pid,byte index);
 [DllImport("ghid",EntryPoint="dllexp_WriteDataToDevice",CallingConvention=CallingConvention.Cdecl)]public static extern int Write(ushort vid,ushort pid,byte index,byte[] data,byte count);
 [DllImport("ghid",EntryPoint="dllexp_ReadDataFromDevice",CallingConvention=CallingConvention.Cdecl)]public static extern int Read(ushort vid,ushort pid,byte index,[In,Out]byte[] data,byte count);
 [DllImport("ghid",EntryPoint="dllexp_GetConnectedList",CallingConvention=CallingConvention.Cdecl)]public static extern int GetList(ushort vid,ushort pid,[In,Out]ConnectedDevices[] list);
}
static class Raw{
 [DllImport("hid",EntryPoint="HidD_SetFeature",SetLastError=true)]public static extern bool SetFeature(IntPtr handle,byte[] data,int length);
 [DllImport("kernel32",EntryPoint="CloseHandle")]public static extern bool Close(IntPtr handle);
}
[ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class MMDeviceEnumeratorCom{}
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceEnumerator{
 int EnumAudioEndpoints(int a,int b,out IntPtr c);
 int GetDefaultAudioEndpoint(int a,int b,out IMMDevice c);
 int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string a,out IMMDevice b);
 int RegisterEndpointNotificationCallback(IntPtr a);
 int UnregisterEndpointNotificationCallback(IntPtr a);
}
[Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDevice{
 int Activate(ref Guid iid,int a,IntPtr b,[MarshalAs(UnmanagedType.IUnknown)]out object c);
 int OpenPropertyStore(int a,out IntPtr b);
 int GetId([MarshalAs(UnmanagedType.LPWStr)]out string a);
 int GetState(out int a);
}
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IAudioMeterInformation{
 int GetPeakValue(out float pfPeak);
 int GetMeteringChannelCount(out int a);
 int GetChannelsPeakValues(int a,[In,Out]float[] b);
 int QueryHardwareSupport(out int a);
}
