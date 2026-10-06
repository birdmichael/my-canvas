using System;
using System.IO;
using System.IO.Ports;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
class DualProbe {
 [StructLayout(LayoutKind.Sequential)] struct Resolution {public int W,H,R;}
 [StructLayout(LayoutKind.Sequential)] struct Timing {public uint Vic,Polarity,HTotal,VTotal,HActive,VActive,PixelClock,VerticalFrequency,HOffset,VOffset,HSyncWidth,VSyncWidth;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct Edid {public int Mode;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Replacement; public int Count;[MarshalAs(UnmanagedType.ByValArray,SizeConst=16)]public Timing[] Timings;}
 [StructLayout(LayoutKind.Sequential)] struct Picture {public int W,H;public IntPtr Data;}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Attach(uint h,IntPtr p,int n);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Detach(uint h);
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern int Wrraper_MSDisplayStart(int l,IntPtr e);
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern int Wrraper_MSDisplayStop();
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern void Wrraper_MSDisplayRegisterCallback(IntPtr a,IntPtr d);
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern int Wrraper_MSDisplaySetVideoParam(uint h,IntPtr r);
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern int Wrraper_MSDisplaySendPicture(uint h,IntPtr p,bool b);
 [DllImport("MSDISPLAYSDKWRRAPER.dll",CallingConvention=CallingConvention.Cdecl)] static extern int Wrraper_MSDisplayReadSN(uint h,IntPtr p);
 static Attach a;static Detach d;static uint handle;static readonly ManualResetEvent ready=new ManualResetEvent(false);static StreamWriter log;
 static void Log(string s){lock(log){log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff")+" "+s);log.Flush();Console.WriteLine(s);}}
 static Bitmap Frame(int w,int h,string title,int count,Color color){var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(b)){g.Clear(color);using(var pen=new Pen(Color.White,6))g.DrawRectangle(pen,12,12,w-25,h-25);using(var f=new Font("Arial",w>1000?60:38,FontStyle.Bold))g.DrawString(title, f,Brushes.White,30,45);using(var f=new Font("Arial",w>1000?100:65,FontStyle.Bold))g.DrawString(count.ToString("D3"),f,Brushes.White,30,h/2);using(var f=new Font("Arial",22))g.DrawString(w+" x "+h,f,Brushes.White,w>1000?1200:30,h-65);}return b;}
 static int SendSquare(Bitmap b){b.RotateFlip(RotateFlipType.Rotate90FlipNone);var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Picture)));try{Marshal.StructureToPtr(new Picture{W=b.Width,H=b.Height,Data=data.Scan0},p,false);return Wrraper_MSDisplaySendPicture(handle,p,false);}finally{Marshal.FreeHGlobal(p);b.UnlockBits(data);}}
 static int SendLong(SerialPort port,Bitmap b){b.RotateFlip(RotateFlipType.Rotate270FlipNone);using(var ms=new MemoryStream()){var encoder=Array.Find(ImageCodecInfo.GetImageEncoders(),x=>x.MimeType=="image/jpeg");using(var ep=new EncoderParameters(1)){ep.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,85L);b.Save(ms,encoder,ep);}byte[] bytes=ms.ToArray();port.BaseStream.Write(bytes,0,bytes.Length);port.BaseStream.Flush();return bytes.Length;}}
 static void Main(string[] args){log=new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dual-test.log"),false);bool started=false;SerialPort port=null;IntPtr ep=IntPtr.Zero;try{
 a=(h,p,n)=>{try{Log("USB_ATTACH handle="+h);IntPtr sn=Marshal.AllocHGlobal(128);try{for(int i=0;i<128;i++)Marshal.WriteByte(sn,i,0);Log("USB_SN result="+Wrraper_MSDisplayReadSN(h,sn)+" value="+Marshal.PtrToStringAnsi(sn));}finally{Marshal.FreeHGlobal(sn);}for(int i=0;i<n;i++){var r=(Resolution)Marshal.PtrToStructure(IntPtr.Add(p,12*i),typeof(Resolution));Log("USB_MODE="+r.W+"x"+r.H+" @"+r.R);if(r.W==480&&r.H==480){IntPtr rp=Marshal.AllocHGlobal(12);try{Marshal.StructureToPtr(r,rp,false);int result=Wrraper_MSDisplaySetVideoParam(h,rp);Log("USB_SET="+result);if(result==0){handle=h;ready.Set();}}finally{Marshal.FreeHGlobal(rp);}}}}catch(Exception ex){Log("CALLBACK_ERROR "+ex);}};
 d=h=>Log("USB_DETACH="+h);
 var edid=new Edid{Mode=2,Replacement="",Count=1,Timings=new Timing[16]};edid.Timings[0]=new Timing{Vic=143,Polarity=7,HTotal=600,VTotal=490,HActive=480,VActive=480,PixelClock=1764,VerticalFrequency=6000,HOffset=100,VOffset=8,HSyncWidth=50,VSyncWidth=4};ep=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Edid)));Marshal.StructureToPtr(edid,ep,false);
 int start=Wrraper_MSDisplayStart(0,ep);Log("USB_START="+start);if(start!=0)throw new Exception("SDK start failed");started=true;Wrraper_MSDisplayRegisterCallback(Marshal.GetFunctionPointerForDelegate(a),Marshal.GetFunctionPointerForDelegate(d));if(!ready.WaitOne(10000))throw new Exception("480x480 mode not connected");
 port=new SerialPort("COM3",9600,Parity.None,8,StopBits.One){Handshake=Handshake.None,ReadTimeout=500,WriteTimeout=3000};port.Open();Log("SERIAL_OPEN=COM3");int squareCount=0,longCount=0;
 int seconds=args.Length>0?int.Parse(args[0]):90;int phaseSeconds=args.Length>1?int.Parse(args[1]):15;for(int step=0;step<seconds;step++){string phase=step<phaseSeconds?"SQUARE ONLY":step<2*phaseSeconds?"LONG ONLY":"BOTH";if(step==0||step>=phaseSeconds){using(var b=Frame(1920,462,"LONG SCREEN / "+phase,longCount++,Color.FromArgb(14,112,68))){Log("LONG_SEND step="+step+" bytes="+SendLong(port,b));}}if(step<phaseSeconds||step>=2*phaseSeconds||step==phaseSeconds){using(var b=Frame(480,480,"SQUARE",squareCount++,Color.FromArgb(24,66,156))){int result=SendSquare(b);Log("SQUARE_SEND step="+step+" result="+result);if(result!=0)throw new Exception("Square send failed "+result);}}Thread.Sleep(1000);}Log("TEST_TRANSPORT_COMPLETE");
 }catch(Exception ex){Log("ERROR "+ex);Environment.ExitCode=1;}finally{if(port!=null)port.Dispose();if(started)Log("USB_STOP="+Wrraper_MSDisplayStop());if(ep!=IntPtr.Zero)Marshal.FreeHGlobal(ep);GC.KeepAlive(a);GC.KeepAlive(d);log.Dispose();}}
}





