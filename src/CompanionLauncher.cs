using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Win32;

static class CompanionLauncher {
    internal static void Start(){
        var roots=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"webcast_mate"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"webcast_mate")};
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})
            try{using(var h=RegistryKey.OpenBaseKey(hive,view))using(var u=h.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")){if(u==null)continue;foreach(var name in u.GetSubKeyNames())using(var k=u.OpenSubKey(name)){if(k==null)continue;var display=k.GetValue("DisplayName") as string;var path=k.GetValue("InstallLocation") as string;if(display!=null&&display.Contains("直播伴侣")&&!string.IsNullOrEmpty(path))roots.Add(path);}}}catch{}
        foreach(var root in roots)foreach(var name in new[]{"直播伴侣 Launcher.exe","直播伴侣.exe"}){var path=Path.Combine(root,name);if(File.Exists(path)){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return;}}
        throw new IOException("未找到直播伴侣，请从桌面快捷方式启动，或检查是否已安装。");
    }
}
