using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

sealed class DeviceChoice {
    internal string Id,Name,Kind;
    public override string ToString(){return Name;}
}
static class Devices {
    internal static List<DeviceChoice> Cameras(){
        var result=new List<DeviceChoice>();object enumerator=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")));IEnumMoniker list=null;
        try{var category=new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");if(((ICreateDevEnum)enumerator).CreateClassEnumerator(ref category,out list,0)!=0||list==null)return result;
            var items=new IMoniker[1];while(list.Next(1,items,IntPtr.Zero)==0){object bag=null;try{var iid=new Guid("55272A00-42CB-11CE-8135-00AA004BB851");items[0].BindToStorage(null,null,ref iid,out bag);object name,path;var b=(IPropertyBag)bag;if(b.Read("FriendlyName",out name,IntPtr.Zero)==0){var hasPath=b.Read("DevicePath",out path,IntPtr.Zero)==0&&path is string;result.Add(new DeviceChoice{Id=hasPath?(string)path:Convert.ToString(name),Name=Convert.ToString(name),Kind="camera"});}}finally{if(bag!=null)Marshal.ReleaseComObject(bag);Marshal.ReleaseComObject(items[0]);}}
        }finally{if(list!=null)Marshal.ReleaseComObject(list);Marshal.ReleaseComObject(enumerator);}return result;
    }
    internal static List<DeviceChoice> Audio(bool input){
        var result=new List<DeviceChoice>{new DeviceChoice{Id="",Name="系统默认",Kind=input?"input":"output"}};
        var enumerator=(IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));IMMDeviceCollection collection=null;
        try{Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(input?1:0,1,out collection));uint count;Marshal.ThrowExceptionForHR(collection.GetCount(out count));
            for(uint i=0;i<count;i++){IMMDevice device=null;IPropertyStore store=null;try{Marshal.ThrowExceptionForHR(collection.Item(i,out device));string id;Marshal.ThrowExceptionForHR(device.GetId(out id));Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0,out store));var key=new PropertyKey{Format=new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),Id=14};PropVariant value;Marshal.ThrowExceptionForHR(store.GetValue(ref key,out value));try{result.Add(new DeviceChoice{Id=id,Name=value.Type==31?Marshal.PtrToStringUni(value.Pointer):id,Kind=input?"input":"output"});}finally{PropVariantClear(ref value);}}finally{if(store!=null)Marshal.ReleaseComObject(store);if(device!=null)Marshal.ReleaseComObject(device);}}
        }finally{if(collection!=null)Marshal.ReleaseComObject(collection);Marshal.ReleaseComObject(enumerator);}return result;
    }
    [ComImport,Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ICreateDevEnum{[PreserveSig]int CreateClassEnumerator(ref Guid category,out IEnumMoniker list,int flags);}
    [ComImport,Guid("55272A00-42CB-11CE-8135-00AA004BB851"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyBag{[PreserveSig]int Read([MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.Struct)]out object value,IntPtr error);[PreserveSig]int Write([MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.Struct)]ref object value);}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceEnumerator{[PreserveSig]int EnumAudioEndpoints(int flow,int state,out IMMDeviceCollection devices);}
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDeviceCollection{[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IMMDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IMMDevice{[PreserveSig]int Activate(ref Guid iid,int clsctx,IntPtr p,[MarshalAs(UnmanagedType.IUnknown)]out object o);[PreserveSig]int OpenPropertyStore(int mode,out IPropertyStore store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetState(out int state);}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore{[PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint i,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);}
    [StructLayout(LayoutKind.Sequential)]struct PropertyKey{internal Guid Format;internal uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]internal ushort Type;[FieldOffset(8)]internal IntPtr Pointer;}
    [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropVariant value);
}
