using Mono.Cecil;
string root=@"C:\Program Files\GIGABYTE\Control Center\Lib";
foreach(string f in new[]{@"COMMDLL\RGBFI.dll",@"COMMDLL\RgbCommon.dll",@"GBT_rgbMotherboard_UC\RgbMotherboard.dll",@"GBT_rgbMotherboard_UC\LedIoControl.dll",@"GBT_rgbMotherboard_UC\GBT_rgbMotherboard_UC.dll"}){
 try{using var a=AssemblyDefinition.ReadAssembly(Path.Combine(root,f));Console.WriteLine("ASSEMBLY "+f);
 foreach(var t in a.MainModule.Types){ if(args.Contains("--types") && (t.Name.Contains("HID_Report")||t.Name=="Connected_Devices"||t.Name=="DataFormat_8297")){Console.WriteLine("TYPE "+t.FullName+" layout="+t.Attributes+" pack="+t.PackingSize+" size="+t.ClassSize);foreach(var f2 in t.Fields)Console.WriteLine(f2.FullName+" "+f2.Offset+" "+f2.MarshalInfo);} foreach(var m in t.Methods){
  if(args.Contains("--il") && t.Name=="MCU_8297" && m.HasBody && new[]{".ctor","Apply","SetLedEffect","SetFeature","SendFeature","SendData","write_to_mcu","Enable_DLedStripCtrl"}.Contains(m.Name)){Console.WriteLine("METHOD "+m.FullName);foreach(var i in m.Body.Instructions)Console.WriteLine(i);}
  if(m.IsPInvokeImpl)Console.WriteLine(m.FullName+" IMPORT "+m.PInvokeInfo.Module.Name+"!"+m.PInvokeInfo.EntryPoint+" "+m.PInvokeInfo.Attributes);
  if(args.Contains("--calls")&&m.HasBody)foreach(var i in m.Body.Instructions){
   if(i.Operand is MethodReference r&&(r.DeclaringType.Name.Contains("LedIo")||r.DeclaringType.Name.Contains("RGBFI")||r.Name.Contains("Effect")||r.Name.Contains("Color")||r.Name.Contains("Apply")))Console.WriteLine(m.FullName+" CALL "+r.FullName);
  }
 }
 } }catch(BadImageFormatException){Console.WriteLine("NATIVE "+f);}
}
