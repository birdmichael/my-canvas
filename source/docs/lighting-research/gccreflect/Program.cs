using System.Reflection;
using System.IO;

string dir = Path.GetFullPath("work/lighting/gcc");
Assembly Load(string name) {
    var p = Path.Combine(dir, name + ".dll");
    return Assembly.LoadFrom(p);
}

try {
    var asm = Load("GBT_rgbMotherboard_UC");
    foreach (var t in asm.GetExportedTypes()) {
        if (!t.FullName!.Contains("<")) {
            Console.WriteLine("=== " + t.FullName + " ===");
            foreach (var c in t.GetConstructors())
                Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Console.WriteLine("  " + (m.IsStatic ? "static " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")");
        }
    }
} catch (Exception ex) {
    Console.WriteLine("ERR: " + ex.Message);
    if (ex is ReflectionTypeLoadException rtle)
        foreach (var e in rtle.LoaderExceptions) Console.WriteLine("  loader: " + e?.Message);
}
