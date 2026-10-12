using System;
using System.Diagnostics;
using System.Reflection;
class ReleaseMetadataTests {
    static void Main(string[] args) {
        var assembly=Assembly.LoadFrom(args[0]);
        string expected=args[1];
        var info=FileVersionInfo.GetVersionInfo(args[0]);
        if(info.FileVersion!=expected+".0" || info.ProductVersion!=expected) throw new Exception("Executable version diverged from package.json");
        if(assembly.GetName().Version.ToString()!=expected+".0") throw new Exception("Assembly version diverged from package.json");
        var type=assembly.GetType("PinyinVim.Native.ReleaseVersion",true);
        if((string)type.GetField("Display",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue()!="V"+expected)
            throw new Exception("About-page version diverged from package.json");
        Console.WriteLine("actual release metadata: package, assembly, file, product and UI version agree");
    }
}
