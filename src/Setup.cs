using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

static class Setup {
    const string Product="直播伴侣备份管家",AppFile=Product+".exe",Marker="backup-manager.install",Key=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CompanionBackupManager";
    static Window window;static Grid view;static bool finished,uninstall;static string installed;
    static T El<T>(string n) where T:class{return view.FindName(n) as T;}
    [STAThread]static int Main(string[] args){
        try{
            if(args.Length==2 && args[0]=="--install-test"){Install(args[1],false,false);Verify(args[1]);return 0;}
            uninstall=args.Contains("--uninstall");
            window=new Window{Title=Product+(uninstall?" 卸载向导":" 安装向导"),Width=640,Height=470,ResizeMode=ResizeMode.NoResize,WindowStyle=WindowStyle.SingleBorderWindow,Background=SystemColors.ControlBrush,WindowStartupLocation=WindowStartupLocation.CenterScreen,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14};
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.png")){var b=new BitmapImage();b.BeginInit();b.StreamSource=s;b.CacheOption=BitmapCacheOption.OnLoad;b.EndInit();window.Icon=b;}
            view=(Grid)XamlReader.Parse(Xaml);window.Content=view;El<Image>("Logo").Source=window.Icon;
            El<Button>("Close").Click+=(s,e)=>window.Close();
            El<TextBox>("Path").Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Product);
            El<Button>("Browse").Click+=(s,e)=>{using(var d=new Forms.FolderBrowserDialog{Description="选择软件安装文件夹",SelectedPath=El<TextBox>("Path").Text})if(d.ShowDialog()==Forms.DialogResult.OK)El<TextBox>("Path").Text=d.SelectedPath;};
            if(uninstall){El<TextBlock>("Title").Text="卸载"+Product;El<TextBlock>("Description").Text="将移除软件和快捷方式。\n已保存的备份、数据及自定义文件全部保留。";El<StackPanel>("InstallOptions").Visibility=Visibility.Collapsed;El<Button>("Action").Content="确认卸载";}
            El<Button>("Action").Click+=(s,e)=>{
                try{
                    if(finished){if(!uninstall)Process.Start(new ProcessStartInfo(Path.Combine(installed,AppFile)){UseShellExecute=true});window.Close();return;}
                    if(uninstall){RemoveInstalled();El<TextBlock>("Title").Text="卸载完成";El<TextBlock>("Description").Text="软件已卸载，备份和数据已保留。\n关闭本窗口后将清理卸载程序。";El<Button>("Action").Content="完成";El<Button>("Close").Content="关闭";finished=true;return;}
                    installed=Install(El<TextBox>("Path").Text,true,El<CheckBox>("Desktop").IsChecked==true);Verify(installed);
                    El<TextBlock>("Title").Text="安装完成";El<TextBlock>("Description").Text="已创建数据与备份文件夹。\n默认备份位置："+Path.Combine(installed,"备份");El<StackPanel>("InstallOptions").Visibility=Visibility.Collapsed;El<Button>("Action").Content="打开软件";El<Button>("Close").Content="关闭";finished=true;
                }catch(Exception error){El<TextBlock>("Info").Text=error.Message;}
            };
            new Application().Run(window);return 0;
        }catch(Exception error){if(args.Length>0)File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"setup-error.txt"),error.ToString());else MessageBox.Show(error.Message,Product);return 1;}
    }
    static byte[] Resource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var m=new MemoryStream()){if(s==null)throw new IOException("安装包缺少组件。");s.CopyTo(m);return m.ToArray();}}
    static string Validate(string path){
        path=Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
        if(!Path.IsPathRooted(path)||path==Path.GetPathRoot(path).TrimEnd('\\')||path.Length<10)throw new IOException("请选择一个专用软件文件夹。");
        for(var d=new DirectoryInfo(path);d!=null;d=d.Parent)if(d.Exists && (d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("安装位置不能是链接目录。");
        if(Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any()&&(!File.Exists(Path.Combine(path,Marker))||File.ReadAllText(Path.Combine(path,Marker))!="CompanionBackupManager"))throw new IOException("此文件夹已有其他文件，请选择新的专用文件夹。");
        return path;
    }
    static void EnsureClosed(string path){foreach(var p in Process.GetProcessesByName(Product))try{if(string.Equals(p.MainModule.FileName,Path.Combine(path,AppFile),StringComparison.OrdinalIgnoreCase))throw new IOException("请先关闭已安装的软件，再继续。");}catch(System.ComponentModel.Win32Exception){throw new IOException("无法确认软件是否退出，请关闭软件后重试。");}}
    static void Write(string path,byte[] data){if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装文件不能是链接。");File.WriteAllBytes(path,data);}
    static string Install(string path,bool register,bool desktop){
        path=Validate(path);EnsureClosed(path);
        if(register)using(var old=Registry.CurrentUser.OpenSubKey(Key)){var previous=old==null?null:old.GetValue("InstallLocation") as string;if(!string.IsNullOrEmpty(previous)&&Directory.Exists(previous)&&!string.Equals(previous,path,StringComparison.OrdinalIgnoreCase))throw new IOException("已安装在："+previous+"。请选择此位置更新，或先卸载旧版。");}
        Directory.CreateDirectory(path);
        var payload=Resource("application.exe");Write(Path.Combine(path,AppFile),payload);Write(Path.Combine(path,"app.ico"),Resource("app.ico"));
        if(!string.Equals(Assembly.GetExecutingAssembly().Location,Path.Combine(path,"卸载程序.exe"),StringComparison.OrdinalIgnoreCase))Write(Path.Combine(path,"卸载程序.exe"),File.ReadAllBytes(Assembly.GetExecutingAssembly().Location));
        File.WriteAllText(Path.Combine(path,Marker),"CompanionBackupManager");Directory.CreateDirectory(Path.Combine(path,"数据"));Directory.CreateDirectory(Path.Combine(path,"备份"));Verify(path);
        if(register){
            var start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Product);Directory.CreateDirectory(start);
            Shortcut(Path.Combine(start,Product+".lnk"),Path.Combine(path,AppFile),"");Shortcut(Path.Combine(start,"卸载.lnk"),Path.Combine(path,"卸载程序.exe"),"--uninstall");
            if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk"),Path.Combine(path,AppFile),"");
            using(var k=Registry.CurrentUser.CreateSubKey(Key)){k.SetValue("DisplayName",Product);k.SetValue("DisplayVersion","0.4.6");k.SetValue("Publisher",Product);k.SetValue("InstallLocation",path);k.SetValue("DisplayIcon",Path.Combine(path,"app.ico"));k.SetValue("UninstallString","\""+Path.Combine(path,"卸载程序.exe")+"\" --uninstall");k.SetValue("NoModify",1);k.SetValue("NoRepair",1);}
        }
        return path;
    }
    static void Verify(string path){if(!File.ReadAllBytes(Path.Combine(path,AppFile)).SequenceEqual(Resource("application.exe")))throw new IOException("安装文件校验失败。");if(!Directory.Exists(Path.Combine(path,"数据"))||!Directory.Exists(Path.Combine(path,"备份")))throw new IOException("数据文件夹未创建。");}
    static void Shortcut(string file,string target,string args){var t=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(t);object link=t.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{file});var lt=link.GetType();lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});lt.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{Path.Combine(Path.GetDirectoryName(target),"app.ico")+",0"});lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(target)});lt.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{args});lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}
    static string Quote(string value){return "'"+value.Replace("'","''")+"'";}
    static void RemoveInstalled(){
        var root=Validate(AppDomain.CurrentDomain.BaseDirectory);EnsureClosed(root);
        var self=Path.GetFullPath(Assembly.GetExecutingAssembly().Location);if(!string.Equals(self,Path.Combine(root,"卸载程序.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("卸载程序位置不匹配。");
        if(!File.Exists(Path.Combine(root,Marker)))throw new IOException("未找到有效的安装记录。");
        foreach(var name in new[]{AppFile,"app.ico",Marker}){var file=Path.Combine(root,name);if(File.Exists(file)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装文件是链接，已停止卸载。");File.Delete(file);}}
        bool registered;using(var k=Registry.CurrentUser.OpenSubKey(Key)){registered=k!=null&&string.Equals((string)k.GetValue("InstallLocation"),root,StringComparison.OrdinalIgnoreCase);}
        if(registered){var start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),Product);
            foreach(var link in new[]{Path.Combine(start,Product+".lnk"),Path.Combine(start,"卸载.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Product+".lnk")})if(File.Exists(link))File.Delete(link);
            if(Directory.Exists(start)&&!Directory.EnumerateFileSystemEntries(start).Any())Directory.Delete(start);
            Registry.CurrentUser.DeleteSubKeyTree(Key,false);
        }
        var script=Path.Combine(Path.GetTempPath(),"backup-manager-uninstall-"+Guid.NewGuid().ToString("N")+".ps1");
        var code="Wait-Process -Id "+Process.GetCurrentProcess().Id+" -ErrorAction SilentlyContinue\r\nRemove-Item -LiteralPath "+Quote(self)+" -Force -ErrorAction SilentlyContinue\r\nRemove-Item -LiteralPath $PSCommandPath -Force";
        File.WriteAllText(script,code,new UTF8Encoding(true));Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+script+"\""){UseShellExecute=false,CreateNoWindow=true});
    }
    const string Xaml=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' UseLayoutRounding='True' TextOptions.TextFormattingMode='Ideal' TextOptions.TextRenderingMode='Grayscale'>
<Grid.Resources>
 <Style TargetType='TextBlock'><Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}'/><Setter Property='TextWrapping' Value='Wrap'/></Style>
 <Style TargetType='Button'><Setter Property='Padding' Value='16,6'/><Setter Property='MinHeight' Value='32'/><Setter Property='MinWidth' Value='88'/></Style>
</Grid.Resources>
<Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
<Border Background='White' BorderBrush='#D9D9D9' BorderThickness='0,0,0,1' Padding='26,22'>
 <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='64'/></Grid.ColumnDefinitions>
 <StackPanel Margin='0,0,20,0'><TextBlock x:Name='Title' Text='安装直播伴侣备份管家' FontSize='21' FontWeight='Normal'/><TextBlock x:Name='Description' Text='请选择安装位置，然后单击「安装」。' Foreground='#555555' Margin='0,10,0,0'/></StackPanel><Image x:Name='Logo' Grid.Column='1' Width='56' Height='56' VerticalAlignment='Top'/>
 </Grid>
</Border>
<Grid Grid.Row='1' Margin='26,22'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions>
 <StackPanel x:Name='InstallOptions'><TextBlock Text='安装位置' Margin='0,0,0,8'/><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><TextBox x:Name='Path' Padding='7,6' VerticalContentAlignment='Center' AutomationProperties.Name='安装位置'/><Button x:Name='Browse' Grid.Column='2' Content='浏览…'/></Grid><CheckBox x:Name='Desktop' Content='创建桌面快捷方式' IsChecked='True' Margin='0,18,0,0'/><TextBlock Text='安装后自动创建「数据」和「备份」文件夹。' FontSize='13' Foreground='#606060' Margin='0,14,0,0'/></StackPanel>
 <TextBlock Grid.Row='1' x:Name='Info' Foreground='#B42318' FontSize='13' Margin='0,14,0,0'/>
</Grid>
<Border Grid.Row='2' BorderBrush='#D9D9D9' BorderThickness='0,1,0,0' Padding='20,12'><StackPanel Orientation='Horizontal' HorizontalAlignment='Right'><Button x:Name='Action' Content='安装' IsDefault='True' MinWidth='100'/><Button x:Name='Close' Content='取消' Margin='10,0,0,0' IsCancel='True' AutomationProperties.Name='关闭安装向导'/></StackPanel></Border>
</Grid>";
}
