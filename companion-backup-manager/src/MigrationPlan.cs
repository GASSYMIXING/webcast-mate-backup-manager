using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

sealed class DeviceBinding {
    internal string Kind,OldId,OldName;
    internal DeviceChoice Target;
}
sealed class MigrationPlan {
    internal readonly List<DeviceBinding> Bindings;
    readonly Dictionary<string,object> targetSettings;
    readonly Dictionary<string,Dictionary<string,object>> cameraFormats;
    readonly Dictionary<string,string> strings=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    readonly bool sameEncoder;
    static readonly string[] EncoderIdentity={"encDeviceName","encoderCore","videoEnc","newVideoEnc","use265","support265BFrame","newEncoderApply"};
    static readonly string[] EncoderAdvanced={"nvencLevel","preset","rateControl","profile","lookAhead","psychoVisualTuning","Bframes","useBFrame","priority"};
    internal static Dictionary<string,object> Clone(Dictionary<string,object> obj){return BackupCore.Obj(BackupCore.Json.DeserializeObject(BackupCore.Json.Serialize(obj)));}
    static string Text(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?Convert.ToString(value):"";}
    internal static void Walk(object obj,Action<Dictionary<string,object>> action){var d=obj as Dictionary<string,object>;if(d!=null){action(d);foreach(var value in d.Values.ToArray())Walk(value,action);}else{var a=obj as object[];if(a!=null)foreach(var item in a)Walk(item,action);}}
    internal static List<DeviceBinding> ReadBindings(BackupCore.Package p){
        var result=new List<DeviceBinding>();
        Action<string,string,string> add=(kind,id,name)=>{if(!result.Any(b=>b.Kind==kind&&string.Equals(b.OldId,id,StringComparison.OrdinalIgnoreCase)))result.Add(new DeviceBinding{Kind=kind,OldId=id,OldName=string.IsNullOrEmpty(name)?(kind=="camera"?"摄像头":kind=="input"?"主麦克风":"主扬声器"):name});};
        Walk(BackupCore.Parse(p.Data["config/WBStore/sourceStore.json"]),d=>{if(Text(d,"type")=="camera"&&d.ContainsKey("payload")){var payload=BackupCore.Obj(d["payload"]);var id=Text(payload,"deviceId");if(id.Length>0)add("camera",id,Text(d,"name"));}});
        byte[] audio;if(p.Data.TryGetValue("config/WBStore/audioStore.json",out audio)){
            var root=BackupCore.Obj(BackupCore.Parse(audio)["audioStore"]);object states;
            if(root.TryGetValue("audioState",out states))foreach(var value in BackupCore.Obj(states).Values){var d=BackupCore.Obj(value);var kind=Text(d,"type");var id=Text(d,"deviceId");if((kind=="input"||kind=="output")&&id.Length>0&&id!="DefaultID")add(kind,id,Text(d,"name"));}
        }
        var settings=BackupCore.Parse(p.Data["config/storage/settings.json"]);
        add("input",Text(settings,"audioInputId"),"主麦克风");add("output",Text(settings,"audioOutputId"),"主扬声器");
        return result;
    }
    internal static Dictionary<string,Dictionary<string,object>> CameraFormats(BackupCore core){
        var found=new Dictionary<string,Dictionary<string,object>>(StringComparer.OrdinalIgnoreCase);
        foreach(var rel in new[]{"WBStore/sourceStore.json","storage/camera-payloads.json"}){
            var file=Path.Combine(core.Root,rel);if(!File.Exists(file))continue;
            var obj=BackupCore.Parse(File.ReadAllBytes(file));
            Walk(obj,d=>{if(d.ContainsKey("deviceId")&&d.ContainsKey("width")&&d.ContainsKey("height"))found[Text(d,"deviceId")]=Clone(d);});
            if(rel.StartsWith("storage/"))foreach(var kv in obj){var value=kv.Value as Dictionary<string,object>;if(value!=null&&value.ContainsKey("width")&&value.ContainsKey("height"))found[kv.Key]=Clone(value);}
        }
        return found;
    }
    internal MigrationPlan(BackupCore core,BackupCore.Package backup,List<DeviceBinding> bindings){
        Bindings=bindings;targetSettings=BackupCore.Parse(File.ReadAllBytes(Path.Combine(core.Root,"storage/settings.json")));cameraFormats=CameraFormats(core);
        var saved=BackupCore.Parse(backup.Data["config/storage/settings.json"]);sameEncoder=Text(saved,"encoderCore")==Text(targetSettings,"encoderCore");
        foreach(var b in bindings){
            if(b.Target==null||b.Target.Kind!=b.Kind)throw new IOException("请为每一项选择对应的目标设备。");
            if(b.Kind=="camera"&&!cameraFormats.ContainsKey(b.Target.Id))throw new IOException("请先在目标电脑的直播伴侣中添加并设置摄像头「"+b.Target.Name+"」，退出伴侣后再导入，以确认输入格式可用。");
            if(b.OldId.Length>0&&b.OldId!="DefaultID")strings[b.OldId]=b.Target.Id;
            strings[b.OldId+"_"+b.Kind]=(b.Target.Id.Length==0?"DefaultID":b.Target.Id)+"_"+b.Kind;
        }
    }
    object Replace(object value){
        if(value is string){string result;return strings.TryGetValue((string)value,out result)?result:value;}
        var d=value as Dictionary<string,object>;if(d!=null){var n=new Dictionary<string,object>();foreach(var kv in d){string key;if(!strings.TryGetValue(kv.Key,out key))key=kv.Key;if(n.ContainsKey(key))throw new IOException("多个旧设备匹配到了同一个目标设备，参数会冲突，请调整匹配。");n[key]=Replace(kv.Value);}return n;}
        var a=value as object[];if(a!=null)return a.Select(Replace).ToArray();return value;
    }
    void Encoder(Dictionary<string,object> d){foreach(var key in EncoderIdentity.Concat(sameEncoder?new string[0]:EncoderAdvanced)){object value;if(targetSettings.TryGetValue(key,out value))d[key]=value;else d.Remove(key);}}
    internal Dictionary<string,object> Transform(string rel,Dictionary<string,object> saved,Dictionary<string,object> current){
        if(rel=="storage/camera-payloads.json")return Clone(current);
        var result=BackupCore.Obj(Replace(saved));
        if(rel=="storage/settings.json"){
            Encoder(result);foreach(var b in Bindings.Where(b=>b.Kind=="input"||b.Kind=="output"))if(Text(saved,b.Kind=="input"?"audioInputId":"audioOutputId")==b.OldId)result[b.Kind=="input"?"audioInputId":"audioOutputId"]=b.Target.Id;
        }
        if(rel=="storage/live-atom-settings.json")Walk(result,d=>{object c;if(d.TryGetValue("videoCoderSetting",out c)&&c is Dictionary<string,object>)Encoder(BackupCore.Obj(c));});
        if(rel=="WBStore/sourceStore.json")Walk(result,d=>{
            if(Text(d,"type")!="camera"||!d.ContainsKey("payload"))return;var payload=BackupCore.Obj(d["payload"]);var id=Text(payload,"deviceId");var b=Bindings.FirstOrDefault(v=>v.Kind=="camera"&&string.Equals(v.Target.Id,id,StringComparison.OrdinalIgnoreCase));if(b==null)return;
            var format=cameraFormats[id];object transform;
            if(d.TryGetValue("transform",out transform)&&transform is Dictionary<string,object>){var t=BackupCore.Obj(transform);foreach(var axis in new[]{new[]{"width","scaleX"},new[]{"height","scaleY"}})if(payload.ContainsKey(axis[0])&&format.ContainsKey(axis[0])&&t.ContainsKey(axis[1])){var oldSize=Convert.ToDouble(payload[axis[0]]);var newSize=Convert.ToDouble(format[axis[0]]);if(oldSize>0&&newSize>0)t[axis[1]]=Convert.ToDouble(t[axis[1]])*oldSize/newSize;}}
            foreach(var key in new[]{"format","width","height","rate","colorSpace","videoRange"}){object value;if(format.TryGetValue(key,out value))payload[key]=value;}payload["name"]=b.Target.Name;
        });
        if(rel=="WBStore/audioStore.json")Walk(result,d=>{
            var kind=Text(d,"type");if(kind!="input"&&kind!="output")return;
            var id=Text(d,"deviceId");var b=Bindings.FirstOrDefault(v=>v.Kind==kind&&(string.Equals(v.Target.Id,id,StringComparison.OrdinalIgnoreCase)||(id=="DefaultID"&&v.OldId.Length==0)));if(b!=null){d["deviceId"]=b.Target.Id.Length==0?"DefaultID":b.Target.Id;d["name"]=b.Target.Name;if(d.ContainsKey("deviceName"))d["deviceName"]=b.Target.Name;if(d.ContainsKey("processName"))d["processName"]=b.Target.Name;}
        });
        return result;
    }
}
