using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;

sealed class MainWindow : Window {
    readonly BackupCore core;readonly Grid view;
    string folder,selected,saveFolder,noticeReturn,pendingDelete;bool saving,restored,managing;
    readonly List<Tuple<DeviceBinding,ComboBox>> mappings=new List<Tuple<DeviceBinding,ComboBox>>();
    T El<T>(string name)where T:class{return view.FindName(name)as T;}
    static BitmapImage Picture(string name){using(var s=typeof(Program).Assembly.GetManifestResourceStream(name)){var b=new BitmapImage();b.BeginInit();b.StreamSource=s;b.CacheOption=BitmapCacheOption.OnLoad;b.EndInit();b.Freeze();return b;}}
    public MainWindow(BackupCore c,string storageRoot=null){
        core=c;Title="直播伴侣备份管家";Width=Math.Min(1040,SystemParameters.WorkArea.Width);Height=Math.Min(720,SystemParameters.WorkArea.Height);
        ResizeMode=ResizeMode.CanMinimize;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=18;Foreground=Brushes.White;Icon=Picture("app.ico");
        AppStorage.Initialize(storageRoot??AppDomain.CurrentDomain.BaseDirectory);folder=saveFolder=AppStorage.LoadFolder();
        using(var s=typeof(Program).Assembly.GetManifestResourceStream("ui.xaml"))view=(Grid)XamlReader.Load(s);Content=view;
        El<Image>("Logo").Source=Picture("app.png");El<Image>("CustomerQr").Source=Picture("customer-qr.jpg");
        foreach(var name in new[]{"Home","Save","Library","Restored"})El<Image>("Guide"+name).Source=Picture("guide-"+name.ToLowerInvariant()+".png");
        El<Border>("SaveGlyph").OpacityMask=new ImageBrush(Picture("save.png"));El<Border>("RestoreGlyph").OpacityMask=new ImageBrush(Picture("restore.png"));
        for(int i=0;i<ToolBox.Names.Length;i++){var name=ToolBox.Names[i];El<Button>("Tool"+i).IsEnabled=ToolBox.Available(name);if(!ToolBox.Available(name))El<Button>("Tool"+i).ToolTip="此源码构建未附带第三方工具，可按 README 自行准备。";El<Image>("ToolIcon"+i).Source=Picture("tool"+i+".png");El<Button>("Tool"+i).Click+=(s,e)=>Run(()=>ToolBox.Open(name));}
        El<Border>("TitleBar").MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed)DragMove();};
        El<Button>("CloseApp").Click+=(s,e)=>Close();El<Button>("Minimize").Click+=(s,e)=>WindowState=WindowState.Minimized;
        El<Button>("NavHome").Click+=(s,e)=>Page("Home");El<Button>("NavLibrary").Click+=(s,e)=>OpenPicker(false,true);
        El<Button>("NavTutorial").Click+=(s,e)=>Page("Tutorial");El<Button>("NavContact").Click+=(s,e)=>Page("Contact");El<Button>("NavAbout").Click+=(s,e)=>Page("About");
        El<Button>("Save").Click+=(s,e)=>Run(()=>{core.Idle();OpenPicker(true);});El<Button>("Restore").Click+=(s,e)=>OpenPicker(false);
        El<Button>("Attach").Click+=(s,e)=>Run(()=>{using(var d=new Forms.OpenFileDialog{Filter="直播伴侣官方备份包|*.zip",Multiselect=true})if(d.ShowDialog()==Forms.DialogResult.OK){BundleStore.AddOriginals(d.FileNames);UpdateOriginals();ShowNotice("官方原包已收集","保存时勾选「同时打包官方原包」即可加入备份。",false);}});
        El<Button>("ClosePicker").Click+=(s,e)=>HidePicker();El<Button>("CancelPicker").Click+=(s,e)=>HidePicker();El<Button>("ReadFolder").Click+=(s,e)=>Run(()=>{ApplyLocation();System.Diagnostics.Process.Start("explorer.exe","\""+folder+"\"");});
        El<TextBox>("Location").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;Run(ApplyLocation);}};
        El<Button>("ChangeFolder").Click+=(s,e)=>Run(()=>{using(var d=new Forms.FolderBrowserDialog{Description="选择备份文件夹",SelectedPath=folder,ShowNewFolderButton=true})if(d.ShowDialog()==Forms.DialogResult.OK){El<TextBox>("Location").Text=d.SelectedPath;ApplyLocation();}});
        El<ListBox>("Files").SelectionChanged+=(s,e)=>Preview();
        El<Button>("DeleteBackup").Click+=(s,e)=>{var item=El<ListBox>("Files").SelectedItem as ListBoxItem;if(item==null)return;var path=(string)item.Tag;ShowNotice("删除此备份？",Path.GetFileNameWithoutExtension(path)+"\n删除后无法恢复，请确认。",false);pendingDelete=path;El<Button>("NoticeOk").Content="取消";El<Button>("NoticeOk").Background=new SolidColorBrush(Color.FromRgb(68,68,73));El<Button>("DeleteConfirm").Visibility=Visibility.Visible;};
        El<Button>("ConfirmPicker").Click+=(s,e)=>Run(Confirm);El<Button>("NoticeOk").Click+=(s,e)=>HideNotice();
        El<Button>("DeleteConfirm").Click+=(s,e)=>{var path=pendingDelete;HideNotice();Run(()=>{AppStorage.DeleteBackup(path,folder);ReadFolder(folder);});};
        El<Button>("LaunchCompanion").Click+=(s,e)=>{HideNotice();Run(()=>CompanionLauncher.Start());};
        El<Button>("CancelMapping").Click+=(s,e)=>CloseMapping();
        El<Button>("ConfirmMapping").Click+=(s,e)=>Run(()=>{core.Idle();foreach(var pair in mappings)pair.Item1.Target=pair.Item2.SelectedItem as DeviceChoice;var p=BackupCore.Load(selected);var plan=new MigrationPlan(core,p,mappings.Select(pair=>pair.Item1).ToList());core.Restore(selected,plan);FinishRestore();});
        KeyDown+=(s,e)=>{if(e.Key==Key.Escape){if(El<Grid>("Notice").Visibility==Visibility.Visible)HideNotice();else if(El<Grid>("Mapping").Visibility==Visibility.Visible)CloseMapping();else if(El<Grid>("Overlay").Visibility==Visibility.Visible)HidePicker();}else if(e.Key==Key.Enter&&El<Grid>("Notice").Visibility==Visibility.Visible){HideNotice();e.Handled=true;}};
        UpdateOriginals();
    }
    void Page(string name){foreach(var p in new[]{"Home","Library","Tutorial","Contact","About"}){El<FrameworkElement>(p).Visibility=p==name?Visibility.Visible:Visibility.Collapsed;var b=El<Button>("Nav"+p);b.Background=p==name?(SolidColorBrush)new BrushConverter().ConvertFromString("#3C3037"):Brushes.Transparent;b.Foreground=p==name?(SolidColorBrush)new BrushConverter().ConvertFromString("#FF668E"):Brushes.White;}}
    void UpdateOriginals(){var count=Directory.Exists(BundleStore.Repository)?Directory.GetFiles(BundleStore.Repository,"*.zip").Length:0;El<TextBlock>("OriginalCount").Text=count==0?"可选 · 保存时自行选择是否打包":"已收集 "+count+" 份 · 保存时可选是否打包";}
    void Run(Action action){try{Cursor=Cursors.Wait;action();}catch(Exception ex){ShowNotice("操作未完成",ex.Message,false);}finally{Cursor=null;}}
    void OpenPicker(bool save,bool library=false){
        var content=El<Grid>("PickerContent");var host=El<Grid>("Library");var frame=El<Border>("PickerFrame");frame.Child=null;host.Children.Clear();managing=library;
        if(library){host.Children.Add(content);content.Margin=new Thickness(0);}else{frame.Child=content;content.Margin=new Thickness(26,22,26,22);}
        El<Border>("OriginalPanel").Visibility=library?Visibility.Visible:Visibility.Collapsed;Page(library?"Library":"Home");saving=save;selected=null;El<Grid>("Main").IsEnabled=library;El<Grid>("Overlay").Visibility=library?Visibility.Collapsed:Visibility.Visible;
        El<TextBlock>("PickerTitle").Text=save?"保存参数":library?"管理备份":"选择备份";El<TextBlock>("PickerTitle").FontSize=library?30:25;El<Button>("ClosePicker").Visibility=library?Visibility.Collapsed:Visibility.Visible;El<Button>("CancelPicker").Visibility=library?Visibility.Collapsed:Visibility.Visible;
        El<Button>("DeleteBackup").Visibility=save?Visibility.Collapsed:Visibility.Visible;El<Grid>("NameRow").Visibility=save?Visibility.Visible:Visibility.Collapsed;El<StackPanel>("OfficialOptionRow").Visibility=save?Visibility.Visible:Visibility.Collapsed;El<CheckBox>("IncludeOfficial").IsChecked=false;El<TextBox>("BackupName").Text="参数备份-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
        El<Button>("ConfirmPicker").Content=save?"保存":"恢复备份";Run(()=>ReadFolder(saveFolder));if(library)El<Button>("NavLibrary").Focus();else El<Button>("ClosePicker").Focus();}
    void HidePicker(){El<Grid>("Overlay").Visibility=Visibility.Collapsed;El<Grid>("Main").IsEnabled=true;if(managing){ReadFolder(folder);El<Button>("NavLibrary").Focus();}else El<Button>(saving?"Save":"Restore").Focus();}
    void CloseMapping(){El<Grid>("Mapping").Visibility=Visibility.Collapsed;if(managing){El<Grid>("Main").IsEnabled=true;El<Button>("NavLibrary").Focus();}else El<Grid>("Overlay").Visibility=Visibility.Visible;}
    void ApplyLocation(){var path=El<TextBox>("Location").Text.Trim().Trim('"');if(!Path.IsPathRooted(path))throw new IOException("请填写完整的备份目录路径。");if(!Directory.Exists(path)){if(!saving)throw new IOException("此目录不存在，请检查路径。");Directory.CreateDirectory(path);}ReadFolder(path);AppStorage.SaveFolder(folder);saveFolder=folder;}
    void ReadFolder(string path){folder=Path.GetFullPath(path);var location=El<TextBox>("Location");location.Text=folder;location.ToolTip=folder;location.CaretIndex=folder.Length;location.ScrollToHorizontalOffset(double.MaxValue);selected=null;
        var list=El<ListBox>("Files");list.Items.Clear();El<Button>("ConfirmPicker").IsEnabled=saving;El<Button>("DeleteBackup").IsEnabled=false;
        if(Directory.Exists(folder)){foreach(var f in Directory.GetFiles(folder,"*.mrbackup").OrderByDescending(File.GetLastWriteTime))AddRow(f,false);}
        El<TextBlock>("PickerInfo").Text=saving?"内容会归档到软件文件夹；自定义位置另存一份。":list.Items.Count==0?"暂无备份，可浏览其他文件夹。":"请选择需要恢复的备份。";}
    void AddRow(string path,bool dir){var row=new Grid{Margin=new Thickness(10)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
        row.Children.Add(new TextBlock{Text=(dir?"▸  ":"◈  ")+(dir?Path.GetFileName(path):Path.GetFileNameWithoutExtension(path)),TextTrimming=TextTrimming.CharacterEllipsis});
        var info=new TextBlock{Text=dir?"文件夹":File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"),Foreground=new SolidColorBrush(Color.FromRgb(153,153,163)),FontSize=14,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(info,1);row.Children.Add(info);
        var item=new ListBoxItem{Content=row,Tag=path,ToolTip=path};System.Windows.Automation.AutomationProperties.SetName(item,Path.GetFileName(path));
        El<ListBox>("Files").Items.Add(item);}
    void Preview(){selected=null;El<Button>("ConfirmPicker").IsEnabled=saving;var item=El<ListBox>("Files").SelectedItem as ListBoxItem;El<Button>("DeleteBackup").IsEnabled=!saving&&item!=null;if(item==null||Directory.Exists((string)item.Tag)||saving)return;
        Run(()=>{var p=BackupCore.Load((string)item.Tag);selected=(string)item.Tag;El<Button>("ConfirmPicker").IsEnabled=true;El<Button>("ConfirmPicker").Content=BackupCore.IsLocal(p)?"恢复备份":"匹配设备并导入";El<TextBlock>("PickerInfo").Text="备份时间："+p.Manifest["created"];});}
    void Confirm(){core.Idle();if(saving){var target=AppStorage.BackupPath(El<TextBox>("BackupName").Text,El<TextBox>("Location").Text);bool includeOfficial=El<CheckBox>("IncludeOfficial").IsChecked==true;var result=BundleStore.Save(core,target,includeOfficial);saveFolder=Path.GetDirectoryName(target);
        string detail=!includeOfficial?"":result.OfficialCount>0?"已打包 "+result.OfficialCount+" 份官方原包。":"本次未找到官方原包，参数已保存。";try{AppStorage.SaveFolder(saveFolder);}catch{detail+=" 保存位置未记住。";}
        HidePicker();UpdateOriginals();ShowNotice("参数已保存",detail+(result.Extra.Length>0?"\n"+result.Extra:""),false);
        }else if(selected!=null){var p=BackupCore.Load(selected);if(BackupCore.IsLocal(p)){core.Restore(selected);FinishRestore();}else OpenMapping(p);}}
    void FinishRestore(){string detail="";try{var p=BackupCore.Load(selected);foreach(var entry in p.Data.Where(k=>k.Key.StartsWith("official/",StringComparison.Ordinal))){var temp=Path.Combine(AppStorage.Root,"数据",Guid.NewGuid().ToString("N")+".zip");try{File.WriteAllBytes(temp,entry.Value);BundleStore.AddOriginals(new[]{temp});}finally{if(File.Exists(temp))File.Delete(temp);}}UpdateOriginals();}catch{detail="参数已恢复，部分官方原包未能归档。";}
        El<Grid>("Mapping").Visibility=Visibility.Collapsed;HidePicker();ShowNotice("参数已恢复",detail,true);}
    void OpenMapping(BackupCore.Package p){var bindings=MigrationPlan.ReadBindings(p);var cameras=Devices.Cameras();var inputs=Devices.Audio(true);var outputs=Devices.Audio(false);var panel=El<StackPanel>("MappingRows");panel.Children.Clear();mappings.Clear();
        foreach(var b in bindings){var row=new StackPanel{Margin=new Thickness(0,0,0,18)};row.Children.Add(new TextBlock{Text=(b.Kind=="camera"?"摄像头 · ":b.Kind=="input"?"麦克风 · ":"扬声器 · ")+b.OldName,FontSize=18,Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.Wrap});
            var choices=b.Kind=="camera"?cameras:b.Kind=="input"?inputs:outputs;var combo=new ComboBox{ItemsSource=choices,FontSize=18,Padding=new Thickness(10,8,10,8),MinHeight=42};var best=choices.FirstOrDefault(d=>string.Equals(d.Id,b.OldId,StringComparison.OrdinalIgnoreCase))??choices.FirstOrDefault(d=>string.Equals(d.Name,b.OldName,StringComparison.OrdinalIgnoreCase));if(best!=null)combo.SelectedItem=best;
            row.Children.Add(combo);panel.Children.Add(row);mappings.Add(Tuple.Create(b,combo));}
        El<Grid>("Main").IsEnabled=false;El<Grid>("Overlay").Visibility=Visibility.Collapsed;El<Grid>("Mapping").Visibility=Visibility.Visible;El<Button>("CancelMapping").Focus();}
    void ShowNotice(string title,string detail,bool launch){El<Border>("NoticeBox").Width=detail.Length==0?(launch?380:320):detail.Length>80?480:400;pendingDelete=null;El<Button>("DeleteConfirm").Visibility=Visibility.Collapsed;restored=launch;noticeReturn=El<Grid>("Mapping").Visibility==Visibility.Visible?"Mapping":El<Grid>("Overlay").Visibility==Visibility.Visible?"Overlay":El<Grid>("Library").Visibility==Visibility.Visible?"Library":"";
        El<Grid>("Main").IsEnabled=false;El<Grid>("Overlay").IsEnabled=false;El<Grid>("Mapping").IsEnabled=false;El<TextBlock>("NoticeTitle").Text=title;El<TextBlock>("NoticeDetail").Text=detail;El<TextBlock>("NoticeDetail").TextAlignment=detail.Length>80?TextAlignment.Left:TextAlignment.Center;El<TextBlock>("NoticeDetail").Visibility=detail.Length>0?Visibility.Visible:Visibility.Collapsed;
        El<Button>("NoticeOk").Content=launch?"关闭":"确定";El<Button>("NoticeOk").Background=(SolidColorBrush)new BrushConverter().ConvertFromString(launch?"#444449":"#EF3765");El<Button>("LaunchCompanion").Visibility=launch?Visibility.Visible:Visibility.Collapsed;
        El<Grid>("Notice").Visibility=Visibility.Visible;El<Button>("NoticeOk").Focus();}
    void HideNotice(){El<Grid>("Notice").Visibility=Visibility.Collapsed;El<Grid>("Overlay").IsEnabled=true;El<Grid>("Mapping").IsEnabled=true;El<Grid>("Main").IsEnabled=noticeReturn.Length==0||noticeReturn=="Library";if(noticeReturn=="Library")El<Button>("NavLibrary").Focus();else if(noticeReturn.Length==0)El<Button>(restored?"Restore":"Save").Focus();else El<Button>(noticeReturn=="Mapping"?"CancelMapping":"ClosePicker").Focus();}
}
