using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;

static class AdvancedTests {
    static void Check(bool value,string text){if(!value)throw new Exception(text);}
    static Dictionary<string,object> D(params object[] pairs){var d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]);return d;}
    static void Put(string root,string rel,object obj){var p=Path.Combine(root,rel);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,BackupCore.Json.Serialize(obj),new UTF8Encoding(false));}
    static Dictionary<string,object> Get(string root,string rel){return BackupCore.Parse(File.ReadAllBytes(Path.Combine(root,rel)));}
    internal static void Foreign(string file){using(var z=ZipFile.Open(file,ZipArchiveMode.Update)){var e=z.GetEntry("manifest.json");Dictionary<string,object> m;using(var r=new StreamReader(e.Open()))m=BackupCore.Obj(BackupCore.Json.DeserializeObject(r.ReadToEnd()));m["identity"]="another-computer";e.Delete();e=z.CreateEntry("manifest.json");using(var w=new StreamWriter(e.Open(),new UTF8Encoding(false)))w.Write(BackupCore.Json.Serialize(m));}}
    internal static void Run(string dir){
        var source=Path.Combine(dir,"old-computer");var target=Path.Combine(dir,"new-computer");Tests.Seed(source);Tests.Seed(target);
        var oldSettings=Get(source,"storage/settings.json");oldSettings["audioInputId"]="old-mic";oldSettings["audioOutputId"]="old-speaker";oldSettings["encoderCore"]="NVENC";oldSettings["encDeviceName"]="old-gpu";oldSettings["preset"]="p1";Put(source,"storage/settings.json",oldSettings);
        var newSettings=Get(target,"storage/settings.json");newSettings["bitrate"]=2000;newSettings["encoderCore"]="AMF";newSettings["encDeviceName"]="new-gpu";newSettings["preset"]="target-preset";Put(target,"storage/settings.json",newSettings);
        var oldPayload=D("deviceId","old-camera","name","Old Camera","width",1920,"height",1080,"rate",30,"format",9);
        var newPayload=D("deviceId","new-camera","name","New Camera","width",1280,"height",720,"rate",25,"format",9);
        Put(source,"WBStore/sourceStore.json",D("sourceStore",D("camera",D("type","camera","name","Old Camera","payload",oldPayload,"transform",D("scaleX",0.5,"scaleY",0.5)))));
        Put(target,"WBStore/sourceStore.json",D("sourceStore",D("camera",D("type","camera","name","New Camera","payload",newPayload))));
        Put(source,"storage/camera-payloads.json",D("old-camera",oldPayload));Put(target,"storage/camera-payloads.json",D("new-camera",newPayload));
        Put(source,"storage/live-atom-settings.json",D("VIDEO",D("0",D("videoCoderSetting",D("encoderCore","NVENC","encDeviceName","old-gpu","preset","p1","bitrate",10000)))));
        Put(source,"WBStore/audioStore.json",D("audioStore",D("audioState",D("mic",D("type","input","deviceId","old-mic","name","Old Mic"),"speaker",D("type","output","deviceId","old-speaker","name","Old Speaker")))));
        var core=new BackupCore(source,false);var newCore=new BackupCore(target,false);
        var archive=Path.Combine(dir,"migration.mrbackup");core.Save(archive,new string[0]);Foreign(archive);var p=BackupCore.Load(archive);
        bool blocked=false;try{newCore.Restore(archive);}catch(IOException){blocked=true;}Check(blocked,"换机未匹配就写入");Check(Convert.ToInt32(Get(target,"storage/settings.json")["bitrate"])==2000,"匹配前改动了参数");
        var bindings=MigrationPlan.ReadBindings(p);foreach(var b in bindings)b.Target=new DeviceChoice{Kind=b.Kind,Id=b.Kind=="camera"?"new-camera":b.Kind=="input"?"new-mic":"new-speaker",Name="New "+b.Kind};
        File.Delete(Path.Combine(target,"WBStore/effectFaceStore.json"));
        newCore.Restore(archive,new MigrationPlan(newCore,p,bindings));
        Check(File.Exists(Path.Combine(target,"WBStore/effectFaceStore.json")),"新电脑缺少美颜预设库时未从备份创建");
        Check(Get(target,"WBStore/effectFaceStore.json").Count==Get(source,"WBStore/effectFaceStore.json").Count,"美颜预设库恢复不完整");
        var restored=Get(target,"storage/settings.json");Check(Convert.ToInt32(restored["bitrate"])==10000,"迁移码率失败");Check((string)restored["encDeviceName"]=="new-gpu"&&(string)restored["preset"]=="target-preset","未保留目标编码器");Check((string)restored["audioInputId"]=="new-mic"&&(string)restored["audioOutputId"]=="new-speaker","音频设备匹配失败");
        var camera=BackupCore.Obj(BackupCore.Obj(BackupCore.Obj(Get(target,"WBStore/sourceStore.json")["sourceStore"])["camera"])["payload"]);
        Check((string)camera["deviceId"]=="new-camera"&&Convert.ToInt32(camera["width"])==1280&&Convert.ToInt32(camera["rate"])==25,"摄像头匹配或输入格式错误");
        var transform=BackupCore.Obj(BackupCore.Obj(BackupCore.Obj(Get(target,"WBStore/sourceStore.json")["sourceStore"])["camera"])["transform"]);Check(Math.Abs(Convert.ToDouble(transform["scaleX"])-0.75)<0.0001,"迁移后摄像头画面大小改变");
        Check(!Get(target,"storage/camera-payloads.json").ContainsKey("old-camera"),"旧设备缓存被迁移到新电脑");
        var live=File.ReadAllText(Path.Combine(target,"storage/live-atom-settings.json"));Check(live.Contains("new-gpu")&&!live.Contains("old-gpu"),"编码设置残留旧显卡标识");
        Check(Directory.GetFiles(target,"*.mrbackup",SearchOption.AllDirectories).Length==0,"迁移恢复时自动生成了备份");
        var presetRel="WBStore/effectFaceStore.json";var sourcePreset=File.ReadAllBytes(Path.Combine(source,presetRel));var targetPreset=File.ReadAllBytes(Path.Combine(target,presetRel));
        Put(source,presetRel,D("unrecognized",42));var invalidPresetArchive=Path.Combine(dir,"invalid-preset.mrbackup");core.Save(invalidPresetArchive,new string[0]);Foreign(invalidPresetArchive);File.Delete(Path.Combine(target,presetRel));
        var untouched=BackupCore.Hash(File.ReadAllBytes(Path.Combine(target,"storage/settings.json")));blocked=false;
        try{newCore.Restore(invalidPresetArchive,new MigrationPlan(newCore,BackupCore.Load(invalidPresetArchive),bindings));}catch(InvalidDataException){blocked=true;}
        Check(blocked&&!File.Exists(Path.Combine(target,presetRel)),"未拒绝结构未知的美颜库初始化");Check(untouched==BackupCore.Hash(File.ReadAllBytes(Path.Combine(target,"storage/settings.json"))),"美颜库结构错误时仍写入参数");File.WriteAllBytes(Path.Combine(source,presetRel),sourcePreset);File.WriteAllBytes(Path.Combine(target,presetRel),targetPreset);
        var existingPlan=new MigrationPlan(newCore,p,bindings);var settingsPath=Path.Combine(target,"storage/settings.json");var settingsBytes=File.ReadAllBytes(settingsPath);var sourceHash=BackupCore.Hash(File.ReadAllBytes(Path.Combine(target,"WBStore/sourceStore.json")));File.Delete(settingsPath);
        blocked=false;try{newCore.Restore(archive,existingPlan);}catch(IOException){blocked=true;}Check(blocked,"缺少基础配置时未阻止恢复");Check(sourceHash==BackupCore.Hash(File.ReadAllBytes(Path.Combine(target,"WBStore/sourceStore.json"))),"基础配置缺失时仍写入了其他参数");File.WriteAllBytes(settingsPath,settingsBytes);
        var app=Path.Combine(dir,"bundle-app");AppStorage.Initialize(app);var exports=Path.Combine(dir,"exports");Directory.CreateDirectory(exports);
        for(int i=0;i<9;i++){using(var z=ZipFile.Open(Path.Combine(exports,"直播伴侣摄像头配置-"+i+".zip"),ZipArchiveMode.Create)){var e=z.CreateEntry("config.json");using(var w=new StreamWriter(e.Open()))w.Write(BackupCore.Json.Serialize(D("content","{}","sign","fixture-"+i)));}}
        Put(source,"WBStore/exportImportStore.json",D("exportImportStore",D("resourceFilePath",exports)));
        var custom=Path.Combine(dir,"custom-output","complete.mrbackup");var result=BundleStore.Save(core,custom,true);Check(result.OfficialCount==9,"没有自动收集全部官方原包");Check(File.Exists(result.Path)&&File.Exists(custom),"没有同时归档软件文件夹和自定义位置");Check(BackupCore.Hash(File.ReadAllBytes(result.Path))==BackupCore.Hash(File.ReadAllBytes(custom)),"另存副本不一致");
        var bundle=BackupCore.Load(result.Path);Check(bundle.Data.ContainsKey("official/8.zip"),"原包数量仍限制为八份");
        var original=File.ReadAllBytes(Path.Combine(exports,"直播伴侣摄像头配置-0.zip"));Check(bundle.Data.Where(k=>k.Key.StartsWith("official/")).Any(k=>BackupCore.Hash(k.Value)==BackupCore.Hash(original)),"官方原包内容被改动");
        File.WriteAllText(Path.Combine(source,"WBStore/exportImportStore.json"),"invalid JSON: must not scan when unchecked");
        var sources=Get(source,"WBStore/sourceStore.json");BackupCore.Obj(sources["sourceStore"])["media"]=D("file",Path.Combine(source,"missing-video.mp4"));Put(source,"WBStore/sourceStore.json",sources);
        var light=BundleStore.Save(core,Path.Combine(app,"备份","轻量参数.mrbackup"));var lightPackage=BackupCore.Load(light.Path);
        Check(light.OfficialCount==0&&!lightPackage.Data.Keys.Any(k=>k.StartsWith("official/")),"未勾选时仍扫描或打包了官方原包");Check(lightPackage.Data.Keys.Any(k=>k.EndsWith(".cube"))&&!lightPackage.Data.Keys.Any(k=>k.EndsWith(".mp4")),"轻量备份丢失 LUT 或仍复制了视频素材");Check(Directory.GetFiles(BundleStore.Repository,"*.zip").Length==9,"未勾选时删除了已收集的原包");
        AppStorage.SaveFolder(Path.GetDirectoryName(custom));Check(AppStorage.LoadFolder()==Path.GetDirectoryName(custom),"自定义备份目录未记住");
        blocked=false;try{AppStorage.DeleteBackup(custom,BundleStore.ArchiveFolder);}catch(IOException){blocked=true;}Check(blocked&&File.Exists(custom),"误删目录外备份");
        var sentinel=Path.Combine(BundleStore.ArchiveFolder,"保留.txt");File.WriteAllText(sentinel,"keep");blocked=false;try{AppStorage.DeleteBackup(sentinel,BundleStore.ArchiveFolder);}catch(IOException){blocked=true;}Check(blocked&&File.Exists(sentinel),"误删非备份文件");
        AppStorage.DeleteBackup(light.Path,BundleStore.ArchiveFolder);Check(!File.Exists(light.Path)&&File.Exists(result.Path)&&File.Exists(custom),"删除波及其他备份或副本");
        using(var stream=typeof(ToolBox).Assembly.GetManifestResourceStream("tools.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var name in ToolBox.Names){if(!ToolBox.Available(name)){bool missing=false;try{ToolBox.Prepare(name);}catch(IOException){missing=true;}Check(missing,"未打包的第三方工具仍被解压");continue;}
            var tool=ToolBox.Prepare(name);byte[] toolBytes;using(var input=zip.GetEntry(name).Open())using(var output=new MemoryStream()){input.CopyTo(output);toolBytes=output.ToArray();}
            Check(File.ReadAllBytes(tool).SequenceEqual(toolBytes),"内置工具解压后与原文件不一致");File.WriteAllText(tool,"test corruption");Check(File.ReadAllBytes(ToolBox.Prepare(name)).SequenceEqual(toolBytes),"工具文件损坏后未修复");
        }
        blocked=false;try{ToolBox.Prepare("../outside.exe");}catch(IOException){blocked=true;}Check(blocked,"工具箱接受了非内置程序路径");
    }
    internal static void CheckOriginals(string directory){
        var temp=Path.Combine(Path.GetTempPath(),"BackupManagerOriginalCheck-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{var profile=Path.Combine(temp,"profile");Tests.Seed(profile);AppStorage.Initialize(Path.Combine(temp,"app"));var files=Directory.GetFiles(directory,"*.zip");BundleStore.AddOriginals(files);
            var result=BundleStore.Save(new BackupCore(profile,false),Path.Combine(temp,"app","备份","原包校验.mrbackup"),true);var p=BackupCore.Load(result.Path);
            foreach(var file in files){var hash=BackupCore.Hash(File.ReadAllBytes(file));Check(p.Data.Any(k=>k.Key.StartsWith("official/")&&BackupCore.Hash(k.Value)==hash),"实际官方原包未完整入包");}
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"official-check.txt"),"PASS: "+files.Length+" real official ZIP packages remain byte-identical; archived size "+new FileInfo(result.Path).Length+" bytes.");
        }finally{Directory.Delete(temp,true);}
    }
}
