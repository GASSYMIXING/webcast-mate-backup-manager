using System;
using System.IO;
using System.Text;
using System.Linq;

static class AppStorage {
    internal static string Root;
    internal static void Initialize(string root){Root=Path.GetFullPath(root);Directory.CreateDirectory(Path.Combine(Root,"数据"));Directory.CreateDirectory(Path.Combine(Root,"备份"));}
    internal static string LoadFolder(){try{var f=File.ReadAllText(Path.Combine(Root,"数据","保存位置.txt")).Trim();if(Path.IsPathRooted(f))return Path.GetFullPath(f);}catch{}return Path.Combine(Root,"备份");}
    internal static void SaveFolder(string folder){File.WriteAllText(Path.Combine(Root,"数据","保存位置.txt"),Path.GetFullPath(folder),new UTF8Encoding(false));}
    internal static void DeleteBackup(string path,string folder){
        path=Path.GetFullPath(path);folder=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if(!string.Equals(Path.GetDirectoryName(path),folder,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetExtension(path),".mrbackup",StringComparison.OrdinalIgnoreCase))throw new IOException("只能删除当前目录中的备份文件。");
        if(!File.Exists(path))throw new IOException("备份文件已不存在，请刷新目录。");
        File.Delete(path);
    }
    internal static string BackupPath(string name,string folder){
        name=name.Trim();folder=folder.Trim().Trim('"');
        if(name.EndsWith(".mrbackup",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-9);
        if(name.Length==0 || name.Length>120 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith(".") || name.EndsWith(" "))throw new IOException("请填写有效的备份名称，不要包含路径或特殊字符。");
        var stem=name.Split('.')[0].ToUpperInvariant();
        if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem))throw new IOException("此备份名称为系统保留名称，请换一个。");
        if(!Path.IsPathRooted(folder))throw new IOException("请填写完整的保存文件夹路径。");
        return Path.Combine(Path.GetFullPath(folder),name+".mrbackup");
    }
}
