using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

static class Program {
    [STAThread] static int Main(string[] args) {
        try {
#if UI_DEMO
            args=new[]{"--ui-demo",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixture")};
#endif
            if(args.Length > 0 && args[0] == "--self-test") { Tests.Run(); return 0; }
            if(args.Length==2&&args[0]=="--official-check"){AdvancedTests.CheckOriginals(args[1]);return 0;}
            if(args.Length>0&&args[0]=="--device-check"){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"device-check.json"),BackupCore.Json.Serialize(new{cameras=Devices.Cameras().Count,microphones=Devices.Audio(true).Count,speakers=Devices.Audio(false).Count}));return 0;}
            if(args.Length==2 && args[0]=="--ui-demo"){Tests.Seed(args[1]);new System.Windows.Application().Run(new MainWindow(new BackupCore(args[1],false)));return 0;}
            var core = new BackupCore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "webcast_mate"), true);
            if(args.Length == 2 && args[0] == "--backup") { core.Save(args[1], new string[0]); return 0; }
            if(args.Length == 2 && args[0] == "--restore") { core.Restore(args[1]); return 0; }
            new System.Windows.Application().Run(new MainWindow(core)); return 0;
        } catch(Exception ex) {
            if(args.Length > 0) File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-error.txt"),ex.ToString());
            else Forms.MessageBox.Show(ex.Message,"操作未完成",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Warning);
            return 1;
        }
    }
}

sealed class BackupCore {
    internal static readonly string[] Configs = {
        "storage/settings.json", "storage/live-atom-settings.json", "storage/camera-payloads.json", "storage/audio-filter.json",
        "WBStore/sourceStore.json", "WBStore/audioStore.json", "WBStore/filterStore.json",
        "WBStore/effectConfigStore.json", "WBStore/effectFaceStore.json", "WBStore/mattingStore.json"
    };
    // shortcut: 仅恢复已核对的配置文件；新增存储格式时应先适配，不能扩大到账号和缓存目录。
    internal readonly string Root;
    readonly bool live;
    internal bool IsLive {get{return live;}}
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength=32*1024*1024, RecursionLimit=128 };
    const long MaxFile=256*1024*1024, MaxTotal=512*1024*1024;
    static readonly string[] AssetExtensions={".cube",".png",".jpg",".jpeg",".bmp",".gif",".webp",".mp4",".mov",".mkv",".avi",".mp3",".wav",".flac"};
    internal BackupCore(string root, bool isLive) { Root=Path.GetFullPath(root); live=isLive; }
    internal static string Hash(byte[] b) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    internal static Dictionary<string,object> Obj(object o) { var d=o as Dictionary<string,object>; if(d==null) throw new InvalidDataException("配置结构不符合预期。"); return d; }
    internal static Dictionary<string,object> Parse(byte[] b) { return Obj(Json.DeserializeObject(Encoding.UTF8.GetString(b).TrimStart('\ufeff'))); }
    static byte[] Encode(object o) { return new UTF8Encoding(false).GetBytes(Json.Serialize(o)); }
    static string Identity() { return Hash(Encoding.UTF8.GetBytes(Environment.MachineName+"|"+Environment.UserDomainName+"|"+Environment.UserName)); }
    internal void Idle() {
        if(live && Process.GetProcesses().Any(p => { try { return p.ProcessName=="直播伴侣" || p.ProcessName=="webcast_mate"; } catch { return false; } }))
            throw new IOException("请先关闭直播伴侣，再保存或恢复参数。");
        if(!Directory.Exists(Root)) throw new IOException("未找到直播伴侣配置，请先在本机运行并设置直播伴侣。");
    }
    internal static void NoLinks(string path) {
        for(var d=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))); d!=null; d=d.Parent)
            if(d.Exists && (d.Attributes & FileAttributes.ReparsePoint)!=0) throw new IOException("配置路径包含链接目录，已停止写入。");
        if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("配置文件是链接，已停止操作。");
    }
    string Target(string rel) {
        if(!Configs.Contains(rel,StringComparer.Ordinal)) throw new InvalidDataException("不允许写入此配置路径。");
        var p=Path.Combine(Root,rel.Replace('/',Path.DirectorySeparatorChar)); NoLinks(p); return p;
    }
    static byte[] Read(string path) {
        if(new FileInfo(path).Length > MaxFile) throw new InvalidDataException("单个文件过大，已停止操作。");
        return File.ReadAllBytes(path);
    }
    static string Version() {
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"webcast_mate");
        if(!Directory.Exists(dir)) return "未知";
        return Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n=>n.Length>0 && char.IsDigit(n[0])).OrderByDescending(n=>n).FirstOrDefault() ?? "未知";
    }
    static void Strings(object value, Action<string> action) {
        if(value is string) { action((string)value); return; }
        var d=value as Dictionary<string,object>;
        if(d!=null) { foreach(var v in d.Values) Strings(v,action); return; }
        var a=value as object[]; if(a!=null) foreach(var v in a) Strings(v,action);
    }
    static object Rewrite(object value, Dictionary<string,string> map) {
        if(value is string) { string result; return map.TryGetValue((string)value,out result)? result:value; }
        var d=value as Dictionary<string,object>;
        if(d!=null) { foreach(var k in d.Keys.ToArray()) d[k]=Rewrite(d[k],map); }
        var a=value as object[]; if(a!=null) for(int i=0;i<a.Length;i++) a[i]=Rewrite(a[i],map);
        return value;
    }
    internal void Save(string output, string[] official,bool includeMedia=true) {
        Idle(); output=Path.GetFullPath(output);
        if(File.Exists(output)) throw new IOException("备份文件已存在，请换一个名称，原备份不会被覆盖。");
        var data=new Dictionary<string,byte[]>(); var resources=new List<object>();
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var rel in Configs) {
            var file=Target(rel); if(!File.Exists(file)) { if(rel==Configs[0] || rel==Configs[4] || rel==Configs[7] || rel==Configs[6]) throw new IOException("缺少关键配置："+rel); continue; }
            var b=Read(file); var obj=Parse(b); ValidateConfig(rel,obj); data.Add("config/"+rel,b);
            Strings(obj,s=>{ if(s.IndexOfAny(Path.GetInvalidPathChars())<0 && (includeMedia?AssetExtensions.Any(ext=>s.EndsWith(ext,StringComparison.OrdinalIgnoreCase)):s.EndsWith(".cube",StringComparison.OrdinalIgnoreCase)) && Path.IsPathRooted(s)) paths.Add(s); });
        }
        foreach(var path in paths) {
            if(!File.Exists(path)) throw new IOException("引用的资源文件缺失，请先修复后再保存："+path);
            var b=Read(path); var name="assets/"+Hash(b)+Path.GetExtension(path).ToLowerInvariant(); data[name]=b;
            resources.Add(new Dictionary<string,object>{{"original",path},{"entry",name}});
        }
        if(official.Length>128) throw new IOException("官方原包超过 128 份，请整理后再保存。");
        for(int i=0;i<official.Length;i++) {
            if(!official[i].EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) throw new IOException("官方预设必须是 ZIP 文件。");
            var b=Read(official[i]);
            using(var ms=new MemoryStream(b)) using(var z=new ZipArchive(ms,ZipArchiveMode.Read))
                if(!z.Entries.Any(e=>e.FullName=="config.json")) throw new InvalidDataException("附加文件不是含 config.json 的官方预设包。");
            data.Add("official/"+i+".zip",b);
        }
        var files=data.Select(kv=>(object)new Dictionary<string,object>{{"path",kv.Key},{"sha256",Hash(kv.Value)},{"length",kv.Value.Length}}).ToArray();
        var manifest=new Dictionary<string,object>{{"format","companion-backup-manager"},{"schema",1},{"created",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")},
            {"identity",Identity()},{"version",Version()},{"files",files},{"resources",resources.ToArray()},
            {"officialNames",official.Select(Path.GetFileName).ToArray()}};
        data.Add("manifest.json",Encode(manifest));
        if(data.Sum(kv=>(long)kv.Value.Length)>MaxTotal) throw new IOException("备份内容超过大小上限。");
        Directory.CreateDirectory(Path.GetDirectoryName(output)); var temp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var z=ZipFile.Open(temp,ZipArchiveMode.Create)) foreach(var kv in data) { var e=z.CreateEntry(kv.Key,CompressionLevel.Optimal); using(var s=e.Open()) s.Write(kv.Value,0,kv.Value.Length); }
            Load(temp); Idle(); File.Move(temp,output);
        } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    static void ValidateConfig(string rel, Dictionary<string,object> obj) {
        if(rel.StartsWith("WBStore/") && rel!="WBStore/effectFaceStore.json") {
            var key=Path.GetFileNameWithoutExtension(rel); if(!obj.ContainsKey(key)) throw new InvalidDataException("配置缺少结构："+key); Obj(obj[key]);
        } else if(rel=="storage/settings.json") {
            foreach(var k in new[]{"resolution","fps","bitrate","audioInputId"}) if(!obj.ContainsKey(k)) throw new InvalidDataException("设置文件缺少 "+k);
        }
    }
    internal sealed class Package {
        internal Dictionary<string,object> Manifest;
        internal Dictionary<string,byte[]> Data;
    }
    internal static Package Load(string path) {
        var data=new Dictionary<string,byte[]>(StringComparer.Ordinal); long total=0;
        using(var z=ZipFile.OpenRead(path)) {
            if(z.Entries.Count>300) throw new InvalidDataException("备份条目过多。");
            foreach(var e in z.Entries) {
                if(e.Length>MaxFile || (total+=e.Length)>MaxTotal || data.ContainsKey(e.FullName)) throw new InvalidDataException("备份大小或条目异常。");
                bool allowed=e.FullName=="manifest.json" || Configs.Any(r=>e.FullName=="config/"+r) ||
                    System.Text.RegularExpressions.Regex.IsMatch(e.FullName,@"\Aassets/[a-f0-9]{64}\.(?:cube|png|jpg|jpeg|bmp|gif|webp|mp4|mov|mkv|avi|mp3|wav|flac)\z") ||
                    System.Text.RegularExpressions.Regex.IsMatch(e.FullName,@"\Aofficial/(?:[0-9]|[1-9][0-9]|1[01][0-9]|12[0-7])\.zip\z");
                if(!allowed) throw new InvalidDataException("备份包含不允许的路径。");
                using(var ms=new MemoryStream()) using(var s=e.Open()) { s.CopyTo(ms); data.Add(e.FullName,ms.ToArray()); }
            }
        }
        if(!data.ContainsKey("manifest.json")) throw new InvalidDataException("缺少备份清单。");
        var m=Parse(data["manifest.json"]);
        if(((string)m["format"]!="companion-backup-manager" && (string)m["format"]!="mingrun-companion-backup") || Convert.ToInt32(m["schema"])!=1) throw new InvalidDataException("不支持此备份格式。");
        var listed=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in (object[])m["files"]) {
            var f=Obj(item); var name=(string)f["path"]; byte[] b;
            if(name=="manifest.json" || !listed.Add(name) || !data.TryGetValue(name,out b) || Hash(b)!=(string)f["sha256"] || b.Length!=Convert.ToInt64(f["length"])) throw new InvalidDataException("备份校验失败，未执行恢复。");
        }
        if(listed.Count!=data.Count-1) throw new InvalidDataException("备份清单与内容不一致。");
        foreach(var rel in Configs) if(data.ContainsKey("config/"+rel)) ValidateConfig(rel,Parse(data["config/"+rel]));
        foreach(var rel in new[]{Configs[0],Configs[4],Configs[6],Configs[7]}) if(!data.ContainsKey("config/"+rel)) throw new InvalidDataException("备份缺少关键参数。");
        foreach(var item in (object[])m["resources"]) {
            var r=Obj(item); var entry=(string)r["entry"];
            if(!entry.StartsWith("assets/",StringComparison.Ordinal) || !data.ContainsKey(entry) || entry!="assets/"+Hash(data[entry])+Path.GetExtension(entry)) throw new InvalidDataException("资源校验失败。");
            if(!Path.IsPathRooted((string)r["original"]) || !AssetExtensions.Contains(Path.GetExtension((string)r["original"]).ToLowerInvariant()) || !string.Equals(Path.GetExtension(entry),Path.GetExtension((string)r["original"]),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源引用无效。");
        }
        return new Package{Manifest=m,Data=data};
    }
    static bool Compatible(object a, object b) {
        if(a==null || b==null) return true;
        if(a is Dictionary<string,object>) return b is Dictionary<string,object>;
        if(a is object[]) return b is object[];
        if(a is string) return b is string;
        if(a is bool) return b is bool;
        return a is IConvertible && b is IConvertible && !(b is bool) && !(b is string);
    }
    // 只合并已保存的一级参数，保留更新后新增的字段；场景和效果集合按快照恢复。
    static void Merge(Dictionary<string,object> current, Dictionary<string,object> saved) {
        foreach(var kv in saved) {
            if(current.ContainsKey(kv.Key) && !Compatible(current[kv.Key],kv.Value)) throw new InvalidDataException("参数结构已变化："+kv.Key+"。未执行恢复。");
            current[kv.Key]=kv.Value;
        }
    }
    static Dictionary<string,object> MissingCurrent(string rel,Dictionary<string,object> saved){
        // 美颜预设库可在首次保存预设后才生成；仅对已核对的这一种结构允许从备份初始化。
        if(rel=="WBStore/effectFaceStore.json"){
            foreach(var value in saved.Values){var preset=value as Dictionary<string,object>;object temps;if(preset==null||!preset.TryGetValue("temps",out temps)||(!(temps is Dictionary<string,object>)&&!(temps is object[])))throw new InvalidDataException("美颜预设库结构无法识别，未执行恢复。");}
            return new Dictionary<string,object>();
        }
        throw new IOException("目标电脑缺少基础配置："+rel+"。请先打开直播伴侣完成初始设置，再退出后重试。");
    }
    internal void Restore(string archive,MigrationPlan migration=null) {
        Idle(); var p=Load(archive);
        if(!IsLocal(p)&&migration==null) throw new IOException("请先匹配当前电脑的设备，再导入这份备份。");
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var planned=new Dictionary<string,byte[]>();
        foreach(var item in (object[])p.Manifest["resources"]) {
            var r=Obj(item); var entry=(string)r["entry"];
            var target=Path.Combine(Root,"BackupManagerAssets",Path.GetFileName(entry)); NoLinks(target);
            map[(string)r["original"]]=target; planned[target]=p.Data[entry];
        }
        foreach(var rel in Configs) {
            byte[] b; if(!p.Data.TryGetValue("config/"+rel,out b)) continue;
            var target=Target(rel);var saved=Parse(b);
            var current=File.Exists(target)?Parse(Read(target)):MissingCurrent(rel,saved);ValidateConfig(rel,current);
            if(migration!=null)saved=migration.Transform(rel,saved,current);
            if(rel.StartsWith("WBStore/") && rel!="WBStore/effectFaceStore.json") { var key=Path.GetFileNameWithoutExtension(rel); Merge(Obj(current[key]),Obj(saved[key])); }
            else Merge(current,saved);
            planned[target]=Encode(Rewrite(current,map));
        }
        Idle();
        var originals=planned.Keys.ToDictionary(k=>k,k=>File.Exists(k)?Read(k):null); var changed=new List<string>();
        try {
            foreach(var kv in planned) { Idle(); NoLinks(kv.Key); changed.Add(kv.Key); AtomicWrite(kv.Key,kv.Value); }
            foreach(var kv in planned) if(Hash(Read(kv.Key))!=Hash(kv.Value)) throw new IOException("写入后校验未通过。");
        } catch(Exception error) {
            var failures=new List<string>();
            foreach(var path in changed.AsEnumerable().Reverse()) try { if(originals[path]==null) File.Delete(path); else AtomicWrite(path,originals[path]); } catch { failures.Add(path); }
            if(failures.Count>0) throw new IOException("恢复中断，部分文件回退失败。请保持伴侣关闭，使用此前主动保存的备份重试。",error);
            throw new IOException("恢复未完成，已回退到操作前。",error);
        }
    }
    internal static bool IsLocal(Package package){return (string)package.Manifest["identity"]==Identity();}
    internal static void AtomicWrite(string path, byte[] b) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllBytes(temp,b); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}

static class Tests {
    static void Assert(bool b,string message) {if(!b)throw new Exception(message);}
    internal static void Seed(string root){
            Directory.CreateDirectory(root);var lut=Path.Combine(root,"sample.cube");File.WriteAllText(lut,"LUT_3D_SIZE 2\n0 0 0");
            foreach(var rel in BackupCore.Configs) {
                var f=Path.Combine(root,rel);Directory.CreateDirectory(Path.GetDirectoryName(f));
                object obj;
                if(rel=="WBStore/effectFaceStore.json") obj=new Dictionary<string,object>{{"sample-id",new Dictionary<string,object>{{"temps",new object[0]}}}};
                else if(rel.StartsWith("WBStore/")) obj=new Dictionary<string,object>{{Path.GetFileNameWithoutExtension(rel),new Dictionary<string,object>{{"sample",1},{"lut",lut}}}};
                else obj=new Dictionary<string,object>{{"resolution",new[]{1080,1920}},{"fps",30},{"bitrate",10000},{"audioInputId","test"},{"embeddedJson","{\"value\":1}"}};
                File.WriteAllText(f,BackupCore.Json.Serialize(obj));
            }
    }
    internal static void Run() {
        var dir=Path.Combine(Path.GetTempPath(),"BackupManagerTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var root=Path.Combine(dir,"profile");var c=new BackupCore(root,false);
        try {
            Seed(root);
            AppStorage.Initialize(Path.Combine(dir,"app"));Assert(AppStorage.LoadFolder()==Path.Combine(dir,"app","备份"),"默认备份位置错误");
            AppStorage.SaveFolder(Path.Combine(dir,"custom"));Assert(AppStorage.LoadFolder()==Path.Combine(dir,"custom"),"未记住自定义保存位置");
            Assert(AppStorage.BackupPath("测试.mrbackup",dir)==Path.Combine(dir,"测试.mrbackup"),"备份后缀重复");
            foreach(var invalid in new[]{"../escape","","CON","a:b"}){bool failed=false;try{AppStorage.BackupPath(invalid,dir);}catch{failed=true;}Assert(failed,"未拒绝无效备份名称");}
            var backup=Path.Combine(dir,"test.mrbackup");c.Save(backup,new string[0]);var p=BackupCore.Load(backup);
            Assert(p.Data.Count==12,"配置和 LUT 未全部收集");
            var settings=Path.Combine(root,BackupCore.Configs[0]);var changed=BackupCore.Parse(File.ReadAllBytes(settings));changed["bitrate"]=2000;changed["newField"]=true;File.WriteAllText(settings,BackupCore.Json.Serialize(changed));
            var packageHash=BackupCore.Hash(File.ReadAllBytes(backup));
            c.Restore(backup);var actual=BackupCore.Parse(File.ReadAllBytes(settings));
            Assert(Convert.ToInt32(actual["bitrate"])==10000,"码率未恢复");Assert((bool)actual["newField"],"新版本字段丢失");
            Assert(Directory.GetFiles(dir,"*.mrbackup",SearchOption.AllDirectories).Length==1,"恢复时不应自动保存备份");
            Assert(BackupCore.Hash(File.ReadAllBytes(backup))==packageHash,"恢复修改了原备份");
            var filter=BackupCore.Parse(File.ReadAllBytes(Path.Combine(root,"WBStore/filterStore.json")));var path=(string)BackupCore.Obj(filter["filterStore"])["lut"];Assert(File.Exists(path)&&path!=Path.Combine(root,"sample.cube"),"LUT 未改为自带资源");
            var bad=Path.Combine(dir,"bad.mrbackup");File.Copy(backup,bad);using(var z=ZipFile.Open(bad,ZipArchiveMode.Update)){z.GetEntry("config/storage/settings.json").Delete();var e=z.CreateEntry("config/storage/settings.json");using(var w=new StreamWriter(e.Open()))w.Write("{}");}
            bool rejected=false;try{c.Restore(bad);}catch{rejected=true;}Assert(rejected,"损坏包未拒绝");Assert(Convert.ToInt32(BackupCore.Parse(File.ReadAllBytes(settings))["bitrate"])==10000,"校验失败后仍写入");
            using(var z=ZipFile.Open(bad,ZipArchiveMode.Update)){z.CreateEntry("../outside.json");}rejected=false;try{BackupCore.Load(bad);}catch{rejected=true;}Assert(rejected,"路径穿越未拒绝");
            AdvancedTests.Run(dir);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-result.txt"),"PASS: native restore, no automatic backup, integrity/path validation, device remapping, target camera format and encoder preservation, original ZIP collection (9), app-folder archive and custom copy, optional embedded tool availability, extraction and repair.");
        } finally {Directory.Delete(dir,true);}
    }
}
