using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PinyinVim.Native
{
    internal sealed partial class Host
    {
        PreferencesWindow preferences;
        string capsuleIconFont;
        bool capsuleIconFontLoaded;

        void LoadCapsuleIconFont()
        {
            capsuleIconFont = Path.Combine(root, "fonts", "PinyinVimIcons.ttf");
            if (File.Exists(capsuleIconFont)) {
                capsuleIconFontLoaded = Win.AddFontResourceEx(capsuleIconFont, 0, IntPtr.Zero) > 0;
                if (capsuleIconFontLoaded) Win.BroadcastFontChange();
            }
        }

        bool IsPreferencesForeground()
        {
            return preferences != null && new System.Windows.Interop.WindowInteropHelper(preferences).Handle == Win.GetForegroundWindow();
        }

        void BindPreferencesOwner()
        {
            if (preferences == null || editorWindow == IntPtr.Zero || !Win.IsWindow(editorWindow)) return;
            var interop = new System.Windows.Interop.WindowInteropHelper(preferences);
            if (interop.Owner != editorWindow) interop.Owner = editorWindow;
            preferences.ShowInTaskbar = false;
        }

        void OpenPreferencesWindow()
        {
            if (!CapsuleNative.IsTestInstance && editorWindow != IntPtr.Zero)
                CapsuleNative.GrantSettingsForeground(editorWindow);
            if (preferences == null) {
                preferences = new PreferencesWindow(this);
                preferences.Closed += (s,e) => {
                    preferences = null;
                    CapsuleNative.SetNumber(Handle,"PinyinCapsules.SettingsOpen",0);
                };
                System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(preferences);
            }
            BindPreferencesOwner();
            CapsuleNative.SetNumber(Handle,"PinyinCapsules.SettingsOpen",1);
            if (CapsuleNative.IsTestInstance) preferences.TestSnapshot();
            else {
                // The click is received in Neovide, whereas WPF lives in the
                // host process. Transfer foreground permission before Show().
                if (preferences.WindowState == System.Windows.WindowState.Minimized) preferences.WindowState = System.Windows.WindowState.Normal;
                preferences.ShowActivated = true;
                preferences.Show();
                preferences.Activate();
                Win.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(preferences).Handle);
            }
        }

        void ClosePreferencesWindow()
        {
            if (preferences != null) preferences.Close();
            if (capsuleIconFontLoaded) {
                Win.RemoveFontResourceEx(capsuleIconFont,0,IntPtr.Zero); capsuleIconFontLoaded = false;
            }
        }

        bool SaveCapsulePreferences(Dictionary<string,object> candidate, out string error)
        {
            var next = new Dictionary<string,object>(candidate);
            foreach (string name in new[] {"notifications","pinyin","words","ime","dark","glass","clearOnClose"}) {
                object value;
                if (!next.TryGetValue(name,out value) || !(value is bool)) { error = "设置值无效：" + name; return false; }
            }
            int font;
            if (!Int32.TryParse(GetString(next,"fontSize"),out font) || font < 10 || font > 28) { error = "字号范围为 10–28"; return false; }
            string path = Path.Combine(profile,"settings.json"), pending = path + ".pending";
            var previous = new Dictionary<string,object>(settings);
            try {
                // Prepare durable storage before replacing the working hotkey.
                File.WriteAllText(pending,json.Serialize(next),new UTF8Encoding(false));
                if (!ApplyCapsuleSettings(next,out error)) { File.Delete(pending); return false; }
                File.WriteAllText(pending,json.Serialize(next),new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(pending,path,null); else File.Move(pending,path);
            } catch (Exception ex) {
                string ignored; ApplyCapsuleSettings(previous,out ignored);
                error = "设置未保存：" + ex.Message; return false;
            }
            settings = next;
            if (preferences != null) preferences.SyncValues();
            imeEnabled = GetBool(settings,"ime");
            if (!imeEnabled) ResetImeState();
            // Do not call Open(): settings must not capture or replace the paste target.
            Send(new Dictionary<string,object> {{"kind","settings"},{"settings",settings}});
            ApplyWindowTheme();
            error = "";
            CapsuleNative.SetNumber(Handle,"PinyinCapsules.SettingsRevision",CapsuleNative.GetNumber(Handle,"PinyinCapsules.SettingsRevision") + 1);
            return true;
        }

        internal sealed class PreferencesWindow : Window
        {
            readonly Host host;
            readonly StackPanel content = new StackPanel();
            readonly TextBlock status = new TextBlock();
            readonly Dictionary<string,Control> editors = new Dictionary<string,Control>();
            readonly Dictionary<string,object> pending = new Dictionary<string,object>();
            readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            readonly List<Button> navigation = new List<Button>();
            readonly string identity = Guid.NewGuid().ToString("N");
            static readonly string[] ThemeIds = { "mist-blue", "lemon", "lavender", "mint", "peach", "rose", "sky", "sand", "forest", "graphite" };
            int category;
            bool building;
            static Brush Color(string hex) { return new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)); }

            internal PreferencesWindow(Host owner)
            {
                host = owner; Title = "设置 · 拼音浮笺";
                Width = 900; Height = 680; MinWidth = 760; MinHeight = 560;
                FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
                Foreground = Color("#263447"); Background = Color("#F5F7FA");
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.CanResize;
                WindowChrome.SetWindowChrome(this, new WindowChrome {
                    CaptionHeight=44, ResizeBorderThickness=new Thickness(6),
                    GlassFrameThickness=new Thickness(0), CornerRadius=new CornerRadius(0), UseAeroCaptionButtons=false
                });
                SourceInitialized += (s,e) => CapsuleNative.ApplySettingsFrame(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Resources = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'><Setter Property='Background' Value='#E4EAF2'/><Setter Property='Foreground' Value='#263447'/><Setter Property='Padding' Value='14,9'/><Setter Property='Cursor' Value='Hand'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#D4E3EE'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#BED2E1'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#6C8CA8'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ToggleButton'><Setter Property='Width' Value='44'/><Setter Property='Height' Value='25'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'><Border x:Name='track' Background='#CED6E1' CornerRadius='13'><Ellipse x:Name='knob' Width='17' Height='17' Margin='4' Fill='White' HorizontalAlignment='Left'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#6489A6'/><Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='track' Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='track' Property='BorderThickness' Value='1'/><Setter TargetName='track' Property='BorderBrush' Value='#263447'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='Slider'><Setter Property='Height' Value='26'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid><Border Background='#DFE7EF' Height='4' CornerRadius='2' VerticalAlignment='Center'/><Track x:Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='#93AEC6' Height='4' CornerRadius='2' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='15' Height='15' Cursor='Hand'><Thumb.Template><ControlTemplate><Border x:Name='dot' Background='#6489A6' CornerRadius='8'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='dot' Property='Background' Value='#476F8E'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='dot' Property='BorderBrush' Value='#263447'/><Setter TargetName='dot' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>");
                var root = new Grid {Background=Background};
                root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(44)}); root.RowDefinitions.Add(new RowDefinition());
                root.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(174)}); root.ColumnDefinitions.Add(new ColumnDefinition());
                var header = new Grid {Background=Background}; Grid.SetColumnSpan(header,2);
                header.Children.Add(new TextBlock {Text=Title,FontSize=13,Foreground=Color("#718096"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
                var captionButtons = new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,4,6,4)};
                captionButtons.Children.Add(CaptionButton("最小化", "M 1,6 L 11,6", () => SystemCommands.MinimizeWindow(this)));
                var maximize=CaptionButton("最大化", "M 1,1 L 11,1 11,11 1,11 Z", () => {
                    if(WindowState==System.Windows.WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this);
                });
                StateChanged += (s,e) => {
                    bool expanded=WindowState==System.Windows.WindowState.Maximized;
                    maximize.ToolTip=expanded?"还原":"最大化";
                    ((System.Windows.Shapes.Path)maximize.Content).Data=Geometry.Parse(expanded?"M 1,3 L 9,3 9,11 1,11 Z M 3,3 L 3,1 11,1 11,9 9,9":"M 1,1 L 11,1 11,11 1,11 Z");
                };
                captionButtons.Children.Add(maximize);
                captionButtons.Children.Add(CaptionButton("关闭设置", "M 1,1 L 11,11 M 11,1 L 1,11", () => Close()));
                header.Children.Add(captionButtons); root.Children.Add(header);
                var side = new StackPanel {Margin=new Thickness(16,26,12,16)};
                side.Children.Add(new TextBlock {Text="拼音浮笺",FontSize=19,FontWeight=FontWeights.SemiBold,Margin=new Thickness(12,0,0,28)});
                string[] sections = {"外观","编辑与草稿","快捷键","关于"};
                for (int i=0;i<sections.Length;i++) {
                    int index=i;
                    var button = new Button {Content=sections[i],Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Left};
                    button.Click += (s,e) => Select(index); navigation.Add(button); side.Children.Add(button);
                }
                var sidebar = new Border {Background=Color("#EDF1F6"),Child=side}; Grid.SetRow(sidebar,1); root.Children.Add(sidebar);
                var right = new Grid {Margin=new Thickness(28,26,28,20)};
                right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); Grid.SetColumn(right,1);
                var scroll = new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}; right.Children.Add(scroll);
                status.Foreground = Color("#718096"); status.FontSize=12; status.TextWrapping=TextWrapping.Wrap; status.Margin=new Thickness(0,14,0,0); Grid.SetRow(status,1); right.Children.Add(status);
                Grid.SetRow(right,1); root.Children.Add(right); Content=root;
                debounce.Tick += (s,e) => Flush();
                Closing += (s,e) => {Flush(); debounce.Stop();};
                Select(0);
            }

            Button CaptionButton(string label,string geometry,Action action)
            {
                var icon=new System.Windows.Shapes.Path {Data=Geometry.Parse(geometry),Stroke=Foreground,StrokeThickness=1.1,Width=12,Height=12,Stretch=Stretch.None,SnapsToDevicePixels=true};
                var button=new Button {Content=icon,ToolTip=label,Width=42,Height=36,Padding=new Thickness(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Center,Focusable=false};
                System.Windows.Automation.AutomationProperties.SetName(button,label);
                WindowChrome.SetIsHitTestVisibleInChrome(button,true);
                button.Click += (s,e) => action();
                return button;
            }

            void Select(int index)
            {
                Flush(); building=true; category=index; content.Children.Clear(); editors.Clear();
                for (int i=0;i<navigation.Count;i++) navigation[i].Background=Color(i==index?"#D9E5EF":"#EDF1F6");
                string[] titles={"外观","编辑与草稿","快捷键","关于"};
                content.Children.Add(new TextBlock {Text=titles[index],FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)});
                content.Children.Add(new TextBlock {Text=index==3?"版本与使用说明":index==2?"点击快捷键后按下组合键。修改成功后立即生效。":"修改自动保存，并立即应用到编辑器。",Foreground=Color("#7C899A"),Margin=new Thickness(0,0,0,24),TextWrapping=TextWrapping.Wrap});
                if (index==0) {
                    Toggle("dark","深色主题","切换编辑器配色");
                    Row("主题色","只改变外壳和胶囊的配色；背景图片只替换外壳底色",ThemePicker());
                    Toggle("glass","背景模糊","模糊窗口后方的内容，文字保持清晰");
                    Range("backgroundTransparency","背景透明度","控制外壳的透出程度",0,100,"%");
                    Range("capsuleTransparency","胶囊透明度","统一调节编辑区、消息区和四个小胶囊",0,100,"%");
                    Range("blurRadius","高斯模糊","拖动释放后重建一次合成效果",0,24," px",true);
                    BackgroundImage();
                    Range("fontSize","编辑字号","也可以使用 Ctrl + 滚轮调整",10,28,"");
                    var resetAppearance = new Button {Content="恢复默认外观",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,2,0,8)};
                    resetAppearance.Click += (s,e) => {ResetAppearance(); Select(0);}; content.Children.Add(resetAppearance);
                } else if(index==1) {
                    Toggle("pinyin","拼音首字母定位","使用拼音首字母定位中文");
                    Toggle("words","中文分词导览","按中文词语移动与编辑");
                    Toggle("notifications","系统通知","显示复制、提交结果和启动状态的系统通知。关闭后不弹出这些通知；复制、粘贴和草稿保存照常执行，编辑器内的提示仍会显示。");
                    Toggle("ime","自动切换中英文","自动记录 Insert（插入）模式下的输入法状态；进入 Normal（普通）或 Visual（选择）模式时强制英文，再次进入 Insert 时恢复上次状态。关闭后不自动切换，仍可手动使用中文输入法。");
                    Toggle("clearOnClose","退出时清空草稿","仅从托盘菜单选择“退出”时清空草稿。Esc、呼出快捷键、Alt+F4 或最小化都只是收起，草稿会保留；编辑期间仍自动保存。关闭此选项后，托盘退出也保留草稿。");
                } else if(index==2) {
                    Shortcut("showHotkey","呼出 / 切换","全局快捷键；其他应用占用时会提示");
                    Shortcut("hideHotkey","收起","仅在 Normal 模式生效");
                    Shortcut("submitHotkey","提交","编辑器内生效");
                    Shortcut("settingsHotkey","设置","编辑器内打开此窗口");
                    Shortcut("copyHotkey","复制","Normal / Insert 复制草稿；Visual 复制选区");
                    Shortcut("pasteHotkey","粘贴","仅在 Insert 模式生效");
                    var reset = new Button {Content="恢复默认快捷键",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};
                    reset.Click += (s,e) => {ResetShortcuts(); Select(2);}; content.Children.Add(reset);
                } else {
                    content.Children.Add(new TextBlock {Text="拼音浮笺  " + ReleaseVersion.Display,FontSize=18,Margin=new Thickness(0,8,0,16)});
                    content.Children.Add(new TextBlock {Text="为中文编辑准备的轻量浮动草稿。\n\nNeovim " + ReleaseVersion.Neovim + " · Neovide " + ReleaseVersion.Neovide + "\n\n关闭编辑器会收起到托盘。需要退出时，请使用托盘菜单。",TextWrapping=TextWrapping.Wrap,LineHeight=26});
                }
                building=false; status.Foreground=Color("#718096"); status.Text="已保存的设置会在下次启动时恢复";
                if(CapsuleNative.IsTestInstance) TestSnapshot();
            }

            void Row(string title,string description,FrameworkElement control)
            {
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
                var text = new StackPanel {Margin=new Thickness(0,0,18,0)};
                text.Children.Add(new TextBlock {Text=title,FontSize=14,FontWeight=FontWeights.Medium});
                text.Children.Add(new TextBlock {Text=description,FontSize=12,Foreground=Color("#7C899A"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
                grid.Children.Add(text); Grid.SetColumn(control,1); control.VerticalAlignment=VerticalAlignment.Center; grid.Children.Add(control);
                content.Children.Add(new Border {Child=grid,Background=Brushes.White,CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(0,0,8,10)});
            }

            void Toggle(string key,string title,string description)
            {
                var toggle = new ToggleButton {IsChecked=GetBool(host.settings,key),ToolTip=title}; editors[key]=toggle;
                toggle.Click += (s,e) => Change(key,toggle.IsChecked==true,false); Row(title,description,toggle);
            }
            void Range(string key,string title,string description,int min,int max,string suffix)
            { Range(key,title,description,min,max,suffix,false); }
            void Range(string key,string title,string description,int min,int max,string suffix,bool releaseOnly)
            {
                var holder=new StackPanel {Orientation=Orientation.Horizontal};
                var number=new TextBlock {Width=46,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
                var slider=new Slider {Minimum=min,Maximum=max,TickFrequency=1,IsSnapToTickEnabled=true,Width=150,Value=Convert.ToDouble(host.settings[key]),ToolTip=title,VerticalAlignment=VerticalAlignment.Center};
                number.Text=((int)slider.Value)+suffix;
                slider.ValueChanged += (s,e) => {number.Text=((int)slider.Value)+suffix; if(!releaseOnly) Change(key,(int)slider.Value,true);};
                if (releaseOnly) {
                    slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((s,e) => Change(key,(int)slider.Value,false)));
                    slider.KeyUp += (s,e) => Change(key,(int)slider.Value,false);
                }
                editors[key]=slider; holder.Children.Add(slider); holder.Children.Add(number); Row(title,description,holder);
            }
            void BackgroundImage()
            {
                var holder = new StackPanel {Orientation=Orientation.Horizontal};
                var box = new TextBox {Text=GetString(host.settings,"backgroundImagePath"), IsReadOnly=true, Width=210, Padding=new Thickness(8,6,8,6), Background=Color("#F5F7FA"), BorderBrush=Color("#DEE5EE"), BorderThickness=new Thickness(1), ToolTip="可选 PNG、JPG 或 WEBP；失效时自动回退为无背景图"};
                var choose = new Button {Content="选择", Margin=new Thickness(8,0,0,0), Padding=new Thickness(12,5,12,5)};
                var clear = new Button {Content="清除", Margin=new Thickness(6,0,0,0), Padding=new Thickness(12,5,12,5)};
                choose.Click += (s,e) => {
                    var dialog = new OpenFileDialog {Title="选择背景图",Filter="图片文件|*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*",CheckFileExists=true,Multiselect=false};
                    if (dialog.ShowDialog(this)==true) { box.Text=dialog.FileName; Change("backgroundImagePath",dialog.FileName,false); }
                };
                clear.Click += (s,e) => { box.Text=""; Change("backgroundImagePath","",false); };
                editors["backgroundImagePath"]=box; holder.Children.Add(box); holder.Children.Add(choose); holder.Children.Add(clear);
                Row("背景图片","图片会铺满窗口并遵循背景透明度；失效时自动回退为透明/模糊背景",holder);
            }
            ComboBox ThemePicker()
            {
                var box = new ComboBox { Width=170, Padding=new Thickness(8,5,8,5), ToolTip="主题色" };
                foreach (string id in ThemeIds) box.Items.Add(new ComboBoxItem { Content=Host.CapsuleThemeNames[id], Tag=id });
                box.SelectedValuePath = "Tag";
                box.SelectedValue = GetString(host.settings,"theme");
                box.SelectionChanged += (s,e) => {
                    var item=box.SelectedItem as ComboBoxItem;
                    if (item != null && item.Tag != null) Change("theme",Convert.ToString(item.Tag),false);
                };
                editors["theme"]=box;
                return box;
            }
            void Shortcut(string key,string title,string description)
            {
                var box = new TextBox {Text=GetString(host.settings,key),IsReadOnly=true,Width=170,Padding=new Thickness(9,7,9,7),Background=Color("#F5F7FA"),BorderBrush=Color("#DEE5EE"),BorderThickness=new Thickness(1),ToolTip="点击后按下组合键；Esc 取消"};
                InputMethod.SetIsInputMethodEnabled(box,false);
                box.GotKeyboardFocus += (s,e) => box.Text="请按快捷键…";
                box.LostKeyboardFocus += (s,e) => box.Text=GetString(host.settings,key);
                box.PreviewKeyDown += (s,e) => {
                    var keyCode=e.Key==Key.System?e.SystemKey:e.Key;
                    if(keyCode==Key.Tab) return;
                    e.Handled=true;
                    if(keyCode==Key.Escape) {box.Text=GetString(host.settings,key); Keyboard.ClearFocus(); return;}
                    if(keyCode==Key.LeftCtrl||keyCode==Key.RightCtrl||keyCode==Key.LeftAlt||keyCode==Key.RightAlt||keyCode==Key.LeftShift||keyCode==Key.RightShift||keyCode==Key.LWin||keyCode==Key.RWin) return;
                    var modifiers=Keyboard.Modifiers;
                    string name=((modifiers&System.Windows.Input.ModifierKeys.Control)!=0?"Ctrl+":"")+((modifiers&System.Windows.Input.ModifierKeys.Alt)!=0?"Alt+":"")+((modifiers&System.Windows.Input.ModifierKeys.Shift)!=0?"Shift+":"");
                    int virtualKey=KeyInterop.VirtualKeyFromKey(keyCode);
                    if((modifiers&System.Windows.Input.ModifierKeys.Windows)!=0) {status.Text="请选择不含 Win 的组合键"; return;}
                    name+=keyCode==Key.Return?"Enter":(virtualKey>=48&&virtualKey<=57?((char)virtualKey).ToString():((System.Windows.Forms.Keys)virtualKey).ToString());
                    Change(key,name,false); box.Text=GetString(host.settings,key);
                };
                editors[key]=box; Row(title,description,box);
            }
            void Change(string key,object value,bool delayed)
            {
                if(building) return;
                pending[key]=value;
                if(delayed) {debounce.Stop(); debounce.Start();} else Flush();
            }
            internal void SyncValues()
            {
                building=true;
                foreach(var pair in editors) {
                    var toggle=pair.Value as ToggleButton;
                    if(toggle!=null) toggle.IsChecked=GetBool(host.settings,pair.Key);
                    var slider=pair.Value as Slider;
                    if(slider!=null && !pending.ContainsKey(pair.Key)) slider.Value=Convert.ToDouble(host.settings[pair.Key]);
                    var box=pair.Value as TextBox;
                    if(box!=null && !box.IsKeyboardFocused) box.Text=GetString(host.settings,pair.Key);
                    var combo=pair.Value as ComboBox;
                    if(combo!=null && !pending.ContainsKey(pair.Key)) combo.SelectedValue=GetString(host.settings,pair.Key);
                }
                building=false;
            }
            void Flush()
            {
                debounce.Stop(); if(pending.Count==0) return;
                var next=new Dictionary<string,object>(host.settings);
                foreach(var pair in pending) next[pair.Key]=pair.Value; pending.Clear();
                string error; bool saved=host.SaveCapsulePreferences(next,out error);
                status.Text=saved?"已保存":error; status.Foreground=Color(saved?"#718096":"#A33E44");
                if(!saved) { foreach(var pair in editors) { var toggle=pair.Value as ToggleButton; if(toggle!=null) toggle.IsChecked=GetBool(host.settings,pair.Key); var slider=pair.Value as Slider; if(slider!=null) {building=true; slider.Value=Convert.ToDouble(host.settings[pair.Key]); building=false;} } }
                if(CapsuleNative.IsTestInstance) TestSnapshot();
            }
            void ResetShortcuts()
            {
                pending["showHotkey"]="Ctrl+Shift+X"; pending["hideHotkey"]="Esc";
                foreach(var pair in ShortcutDefaults) pending[pair.Key+"Hotkey"]=pair.Value; Flush();
            }
            void ResetAppearance()
            {
                pending["dark"]=false; pending["theme"]="mist-blue"; pending["glass"]=true; pending["backgroundTransparency"]=50;
                pending["capsuleTransparency"]=20; pending["blurRadius"]=5; pending["backgroundImagePath"]=""; pending["fontSize"]=14;
                Flush();
            }
            internal void TestAction(int action)
            {
                if(action>=1&&action<=4) Select(action-1);
                else if(action==5) {ResetShortcuts(); Select(2);}
                else if(action==6) {
                    Select(0); ((Slider)editors["backgroundTransparency"]).Value=70; ((Slider)editors["capsuleTransparency"]).Value=45;
                    Flush();
                } else if(action==9 || action==10) {Select(0); var toggle=(ToggleButton)editors["glass"]; toggle.IsChecked=action==10; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
                else if(action==7) {Select(2); Change("showHotkey","Ctrl+Alt+F9",false); Change("submitHotkey","Ctrl+Alt+Enter",false);}
                else if(action==8) Change("settingsHotkey",GetString(host.settings,"submitHotkey"),false);
                TestSnapshot();
            }
            internal void TestSnapshot()
            {
                // Render the actual WPF tree without showing or focusing any HWND.
                var interop = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr hiddenWindow = interop.EnsureHandle();
                var visual=(FrameworkElement)Content;
                Content=null; // A hidden Window otherwise collapses its entire visual tree.
                visual.Resources=Resources;
                System.Windows.Documents.TextElement.SetFontFamily(visual,FontFamily);
                System.Windows.Documents.TextElement.SetFontSize(visual,FontSize);
                System.Windows.Documents.TextElement.SetForeground(visual,Foreground);
                ((Grid)visual).Background=Background;
                RenderTargetBitmap bitmap;
                try {
                    visual.Measure(new Size(Width,Height)); visual.Arrange(new Rect(0,0,Width,Height)); visual.UpdateLayout();
                    bitmap=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
                } finally {Content=visual;}
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var stream=File.Create(Path.Combine(host.profile,"settings-"+category+".png"))) png.Save(stream);
                File.WriteAllText(Path.Combine(host.profile,"settings-window-state.json"),host.json.Serialize(new {instance=identity,category=category,status=status.Text,settings=host.settings,editors=new List<string>(editors.Keys),chrome="integrated",headerBackground=Background.ToString(),captionHeight=WindowChrome.GetWindowChrome(this).CaptionHeight,owner=CapsuleNative.GetWindow(hiddenWindow,4).ToInt64(),showInTaskbar=ShowInTaskbar,nativeHidden=!CapsuleNative.IsWindowVisible(hiddenWindow)}),new UTF8Encoding(false));
            }
        }
    }
}

