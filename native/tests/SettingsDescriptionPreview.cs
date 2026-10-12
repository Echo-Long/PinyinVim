using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
class SettingsDescriptionPreview {
    [STAThread] static void Main(string[] args) {
        Environment.SetEnvironmentVariable("PINYINVIM_CAPSULE_TEST_PROFILE",null);
        Directory.CreateDirectory(args[1]);
        var assembly=Assembly.LoadFrom(args[0]);
        var hostType=assembly.GetType("PinyinVim.Native.Host",true);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var host=FormatterServices.GetUninitializedObject(hostType);
        hostType.GetField("root",flags).SetValue(host,Path.GetDirectoryName(args[0]));
        hostType.GetField("profile",flags).SetValue(host,args[1]);
        hostType.GetField("json",flags).SetValue(host,new JavaScriptSerializer());
        hostType.GetField("settings",flags).SetValue(host,hostType.GetMethod("LoadSettings",flags).Invoke(host,null));
        var type=hostType.GetNestedType("PreferencesWindow",BindingFlags.NonPublic);
        var window=(Window)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{host},null);
        int category=args.Length>2?Int32.Parse(args[2]):1;
        type.GetMethod("Select",flags).Invoke(window,new object[]{category});
        var root=(FrameworkElement)window.Content;
        window.Content=null;
        root.Resources=window.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(root,window.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(root,window.FontSize);
        System.Windows.Documents.TextElement.SetForeground(root,window.Foreground);
        ((Grid)root).Background=window.Background;
        foreach(var size in new[]{new Size(900,680),new Size(760,560)}) {
            root.Measure(size); root.Arrange(new Rect(new Point(),size)); root.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(root);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(Path.Combine(args[1],(category==3?"settings-about-":"settings-edit-")+(int)size.Width+".png"))) png.Save(output);
        }
        Console.WriteLine("actual settings tree rendered at 900x680 and minimum 760x560; no window shown");
        window.Close();
    }
}
