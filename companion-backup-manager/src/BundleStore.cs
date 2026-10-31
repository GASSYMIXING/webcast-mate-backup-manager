using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;

static class BundleStore {
    internal static string Repository {get{return Path.Combine(AppStorage.Root,"官方原包");}}
    internal static string ArchiveFolder {get{return Path.Combine(AppStorage.Root,"备份");}}
    internal sealed class Result {internal string Path;internal int OfficialCount;internal string Extra="";}
    internal static string Kind(byte[] bytes){
        using(var m=new MemoryStream(bytes))using(var z=new ZipArchive(m,ZipArchiveMode.Read)){
            var entry=z.GetEntry("config.json");if(entry==null||entry.Length>32*1024*1024)throw new InvalidDataException("不是可识别的直播伴侣官方原包。");
            Dictionary<string,object> config;using(var stream=entry.Open())using(var output=new MemoryStream()){stream.CopyTo(output);config=BackupCore.Parse(output.ToArray());}
            if(config.ContainsKey("content")&&config.ContainsKey("sign"))return "美颜";
            if(config.ContainsKey("filterTempList")&&config.ContainsKey("configSign"))return "调色";
            if(z.GetEntry("function.json")!=null)return "整体配置";
            throw new InvalidDataException("不是可识别的美颜、调色或整体配置原包。");
        }
    }
    static byte[] Read(string path){if(new FileInfo(path).Length>256L*1024*1024)throw new IOException("单个官方原包超过 256 MB，请整理后再收集。");return File.ReadAllBytes(path);}
    static void CopyChecked(string file,byte[] data){
        if(File.Exists(file)){if(BackupCore.Hash(File.ReadAllBytes(file))!=BackupCore.Hash(data))throw new IOException("存在不同内容的同名文件："+file);return;}
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        using(var s=new FileStream(file,FileMode.CreateNew,FileAccess.Write))s.Write(data,0,data.Length);
        if(BackupCore.Hash(File.ReadAllBytes(file))!=BackupCore.Hash(data))throw new IOException("原包归档校验失败。");
    }
    internal static void AddOriginals(string[] files){
        foreach(var file in files){var bytes=Read(file);var kind=Kind(bytes);CopyChecked(Path.Combine(Repository,"官方"+kind+"-"+BackupCore.Hash(bytes).Substring(0,16)+".zip"),bytes);}
    }
    internal static string[] Collect(BackupCore core){
        Directory.CreateDirectory(Repository);var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var relative in new[]{"WBStore/exportImportStore.json","WBStore/filterStore.json"}){
            var f=Path.Combine(core.Root,relative);if(!File.Exists(f))continue;
            var obj=BackupCore.Parse(File.ReadAllBytes(f));var key=Path.GetFileNameWithoutExtension(f);object inner;
            if(obj.TryGetValue(key,out inner)){object path;var d=BackupCore.Obj(inner);if(d.TryGetValue("resourceFilePath",out path)&&path is string && Directory.Exists((string)path))folders.Add((string)path);}
        }
        foreach(var folder in folders)AddOriginals(Directory.GetFiles(folder,"*.zip").Where(f=>Path.GetFileName(f).StartsWith("直播伴侣",StringComparison.Ordinal)).ToArray());
        return Directory.GetFiles(Repository,"*.zip").OrderBy(f=>f).ToArray();
    }
    internal static Result Save(BackupCore core,string target,bool includeOfficial=false){
        core.Idle();target=Path.GetFullPath(target);var originals=includeOfficial?Collect(core):new string[0];
        var local=Path.Combine(ArchiveFolder,Path.GetFileName(target));
        if(!string.Equals(target,local,StringComparison.OrdinalIgnoreCase)&&File.Exists(target))throw new IOException("所选位置已有同名备份，请换一个名称。");
        core.Save(local,originals,false);
        var result=new Result{Path=local,OfficialCount=originals.Length};
        if(!string.Equals(target,local,StringComparison.OrdinalIgnoreCase))try{CopyChecked(target,File.ReadAllBytes(local));result.Extra="同时保存至："+target;}catch(Exception e){result.Extra="软件文件夹内已保存；自定义位置未保存成功："+e.Message;}
        return result;
    }
    internal static string ImportPortable(string archive){
        var p=BackupCore.Load(archive);var originals=p.Data.Where(k=>k.Key.StartsWith("official/",StringComparison.Ordinal)).OrderBy(k=>k.Key).ToArray();
        if(originals.Length==0)throw new IOException("这份备份未包含官方原包。请先在旧电脑通过伴侣导出美颜、调色预设，再收集原包并重新保存。");
        var root=Path.Combine(ArchiveFolder,"换机导入-"+BackupCore.Hash(File.ReadAllBytes(archive)).Substring(0,12));
        foreach(var kv in originals){var kind=Kind(kv.Value);CopyChecked(Path.Combine(root,"官方"+kind+"-"+BackupCore.Hash(kv.Value).Substring(0,12)+".zip"),kv.Value);}
        foreach(var kv in p.Data.Where(k=>k.Key.StartsWith("assets/",StringComparison.Ordinal)))CopyChecked(Path.Combine(root,"LUT",Path.GetFileName(kv.Key)),kv.Value);
        CopyChecked(Path.Combine(root,"原始备份.mrbackup"),File.ReadAllBytes(archive));
        return root;
    }
}
