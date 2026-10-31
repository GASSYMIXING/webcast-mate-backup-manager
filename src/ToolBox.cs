using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;

static class ToolBox {
    internal static readonly string[] Names={"采集卡改名.exe","OBS改名.exe","OBS蒙版.exe","虚拟显示器.exe"};
    internal static bool Available(string name){using(var stream=typeof(ToolBox).Assembly.GetManifestResourceStream("tools.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))return Names.Contains(name)&&zip.GetEntry(name)!=null;}
    internal static string Prepare(string name){
        if(!Names.Contains(name))throw new IOException("未找到此内置工具。");
        byte[] data;using(var stream=typeof(ToolBox).Assembly.GetManifestResourceStream("tools.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
            var entry=zip.GetEntry(name);if(entry==null||entry.Length>32*1024*1024)throw new IOException("内置工具文件不完整。");
            using(var input=entry.Open())using(var output=new MemoryStream()){input.CopyTo(output);data=output.ToArray();}
        }
        var path=Path.Combine(AppStorage.Root,"数据","内置工具",name);BackupCore.NoLinks(path);
        if(!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(data))BackupCore.AtomicWrite(path,data);
        return path;
    }
    internal static void Open(string name){var path=Prepare(name);Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)});}
}
