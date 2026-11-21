using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetLauncher;
public sealed partial class LauncherWindow
{
 string selectedPresetElement="play";
 static readonly string[] PresetElementIds=WidgetPreset.ElementIds;
 static readonly string[] PresetElementNames=PresetElementIds.Select(PresetSurface.Label).ToArray();
 Window? largePresetWindow;
 PresetPreview? largePresetPreview;
 TextBlock? presetScaleLabel;
 StackPanel? presetDetailPanel;
 WrapPanel? presetTabButtons;
 readonly List<Action> syncGeometryFields=new();
 double presetPreviewZoom=1;
 void AddPresetZoomControls(StackPanel panel){
  panel.Children.Add(Label("Масштаб превью",11,MutedBrush,new Thickness(0,10,0,0)));
  var slider=new Slider{Minimum=25,Maximum=150,Value=presetPreviewZoom*100,TickFrequency=5,IsSnapToTickEnabled=true,Height=26};AutomationProperties.SetName(slider,"Масштаб превью");panel.Children.Add(slider);
  slider.ValueChanged+=(_,_)=>{presetPreviewZoom=slider.Value/100;if(presetPreview!=null)presetPreview.Zoom=presetPreviewZoom;RefreshLargePresetPreview();};
  var buttons=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};panel.Children.Add(buttons);
  var actual=PresetButton("100%",()=>slider.Value=100);actual.Margin=new Thickness(0,3,4,0);buttons.Children.Add(actual);
  var fit=PresetButton("Вписать",()=>{if(presetPreview!=null&&presetDraft!=null)slider.Value=Math.Clamp(Math.Min((presetPreview.ActualWidth-40)/presetDraft.Width,(presetPreview.ActualHeight-40)/presetDraft.Height)*100,25,150);});fit.Margin=new Thickness(4,3,0,0);buttons.Children.Add(fit);
 }
 void PresetNumber(Panel panel,string label,double min,Func<double> maximum,Func<double> current,Action<double> write){
  panel.Children.Add(Label(label,12,MutedBrush,new Thickness(0,12,0,6)));
  var input=PresetTextBox(label,current().ToString("0.##",CultureInfo.CurrentCulture));
  panel.Children.Add(input);input.MaxLength=8;
  bool setting=false;
  syncGeometryFields.Add(()=>{
   string value=current().ToString("0.##",CultureInfo.CurrentCulture);
   if(input.Text==value)return;
   setting=true;try{input.Text=value;input.BorderBrush=ColorBrush("#3B3D50");}finally{setting=false;}
  });
  bool TryRead(out double number)=>double.TryParse(input.Text.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number)&&double.IsFinite(number)&&number>=min;
  input.TextChanged+=(_,_)=>{
   if(setting)return;
   if(!TryRead(out double number)){input.BorderBrush=ColorBrush("#F0A2AC");return;}
   double accepted=Math.Min(number,maximum());
   if(accepted!=number){
    setting=true;
    try{input.Text=accepted.ToString("0.##",CultureInfo.CurrentCulture);input.CaretIndex=input.Text.Length;}
    finally{setting=false;}
   }
   input.BorderBrush=ColorBrush("#3B3D50");
   write(accepted);RefreshPresetPreview();
  };
  input.LostKeyboardFocus+=(_,_)=>{if(!TryRead(out _))input.Text=current().ToString("0.##",CultureInfo.CurrentCulture);};
 }
 void PresetElementFields(StackPanel panel,WidgetPreset p){
  p.Normalize();
  syncGeometryFields.Clear();
  PresetChoice(panel,"Элемент",PresetElementIds,PresetElementNames,selectedPresetElement,id=>{selectedPresetElement=id;Render();});
  var e=p.Elements[selectedPresetElement];
  var pinned=new CheckBox{Content="Закрепить элемент",IsChecked=e.Locked,Margin=new Thickness(0,12,0,4)};AutomationProperties.SetName(pinned,"Закрепить элемент");panel.Children.Add(pinned);
  pinned.Click+=(_,_)=>{p.LockElement(selectedPresetElement,pinned.IsChecked==true);RefreshPresetPreview();Render();};
  if(e.Locked)panel.Children.Add(Label("Закреплён: положение, размеры, видимость, цвета и шрифт сохраняются, в том числе при рандомизации.",11,AccentBrush,new Thickness(0,8,0,4)));
  var fields=new StackPanel{IsEnabled=!e.Locked};panel.Children.Add(fields);
  PresetCheck(fields,"Показывать элемент",p.IsElementVisible(selectedPresetElement),x=>{if(e.Locked)return;e.Visible=x;SyncPresetElementVisibility(p,selectedPresetElement,x);});
  PresetNumber(fields,"Положение X",0,()=>p.Width-e.Width,()=>e.X,x=>{if(!e.Locked)e.X=x;});
  PresetNumber(fields,"Положение Y",0,()=>p.Height-e.Height,()=>e.Y,x=>{if(!e.Locked)e.Y=x;});
  PresetNumber(fields,"Ширина элемента",8,()=>p.Width-e.X,()=>e.Width,x=>{if(!e.Locked)e.Width=x;});
  PresetNumber(fields,"Высота элемента",8,()=>p.Height-e.Y,()=>e.Height,x=>{if(!e.Locked)e.Height=x;});
  panel.Children.Add(Label("Перетащи элемент мышью. Потяни за угол выделения, чтобы изменить размер.",11,MutedBrush,new Thickness(0,16,0,0)));
 }
 static void SyncPresetElementVisibility(WidgetPreset p,string id,bool visible){switch(id){case "cover":p.ShowCover=visible;break;case "artist":p.ShowArtist=visible;break;case "time":p.ShowTime=visible;break;case "progress":p.ShowProgress=visible;break;case "play":case "previous":case "next":case "repeat":if(visible)p.ShowControls=true;break;}}
 void AttachPresetDragging(PresetPreview previewControl){
  var layer=previewControl.EditLayer;
  SelectInPreview(previewControl);
  string? dragId=null;Point anchor=default;Rect initial=Rect.Empty;int corner=-1;
  layer.MouseLeftButtonDown+=(_,e)=>{
   if(busy||updatePending||page!="customize"||customizationStep!="editor"||presetDraft==null)return;
   anchor=e.GetPosition(layer);
   corner=e.OriginalSource is Border {Tag:int index}?index:-1;
   string? id=corner>=0?selectedPresetElement:previewControl.HitElement(anchor);
   if(id==null)return;
   SelectPresetElement(id);
   var element=presetDraft.Elements[id];
   if(!element.Locked&&layer.CaptureMouse()){
    dragId=id;initial=new Rect(element.X,element.Y,element.Width,element.Height);
   }
   e.Handled=true;
  };
  layer.MouseMove+=(_,e)=>{
   if(dragId==null||e.LeftButton!=MouseButtonState.Pressed||presetDraft==null)return;
   var delta=e.GetPosition(layer)-anchor;
   if(corner<0)MovePresetElement(dragId,initial.X+delta.X,initial.Y+delta.Y);
   else SetPresetBounds(dragId,ResizeFromCorner(initial,corner,delta,new Size(presetDraft.Width,presetDraft.Height)));
   e.Handled=true;
  };
  layer.MouseLeftButtonUp+=(_,e)=>{if(dragId==null)return;dragId=null;layer.ReleaseMouseCapture();e.Handled=true;};
  layer.LostMouseCapture+=(_,_)=>dragId=null;
 }
 void SelectInPreview(PresetPreview control){
  bool visible=presetTab=="Детали"&&presetDraft!=null;
  control.SelectElement(visible?selectedPresetElement:null,visible&&presetDraft!.Elements[selectedPresetElement].Locked);
 }
 void SelectPresetElement(string id){
  if(presetDraft==null||presetDetailPanel==null||presetTabButtons==null)return;
  if(presetTab!="Детали"||id!=selectedPresetElement){
   presetTab="Детали";selectedPresetElement=id;
   foreach(Button tab in presetTabButtons.Children){
    bool active=Equals(tab.Content,presetTab);
    tab.Background=active?ColorBrush("#30283F"):PanelBrush;tab.Foreground=active?AccentBrush:MutedBrush;
   }
   presetDetailPanel.Children.Clear();PresetElementFields(presetDetailPanel,presetDraft);
  }
  if(presetPreview!=null)SelectInPreview(presetPreview);
  if(largePresetPreview!=null)SelectInPreview(largePresetPreview);
 }
 internal static Rect ResizeFromCorner(Rect box,int corner,Vector delta,Size canvas){
  double left=box.Left,right=box.Right,top=box.Top,bottom=box.Bottom;
  if((corner&1)==0)left=Math.Clamp(left+delta.X,0,right-8);
  else right=Math.Clamp(right+delta.X,left+8,canvas.Width);
  if(corner<2)top=Math.Clamp(top+delta.Y,0,bottom-8);
  else bottom=Math.Clamp(bottom+delta.Y,top+8,canvas.Height);
  return new Rect(new Point(left,top),new Point(right,bottom));
 }
 void MovePresetElement(string id,double x,double y){
  if(busy||updatePending||presetDraft==null)return;
  var element=presetDraft.Elements[id];
  if(element.Locked)return;
  x=Math.Clamp(x,0,presetDraft.Width-element.Width);
  y=Math.Clamp(y,0,presetDraft.Height-element.Height);
  SetPresetBounds(id,new Rect(x,y,element.Width,element.Height));
 }
 void SetPresetBounds(string id,Rect box){
  if(busy||updatePending||presetDraft==null)return;
  var element=presetDraft.Elements[id];
  if(element.Locked||new Rect(element.X,element.Y,element.Width,element.Height)==box)return;
  element.X=box.X;element.Y=box.Y;element.Width=box.Width;element.Height=box.Height;
  void Update(PresetPreview? preview){
   if(preview==null)return;
   preview.Surface.MoveElement(id,box.X,box.Y);preview.Surface.SizeElement(id,box.Width,box.Height);
   preview.RefreshSelection();
  }
  Update(presetPreview);Update(largePresetPreview);
  if(presetTab=="Детали"&&selectedPresetElement==id)foreach(var refresh in syncGeometryFields)refresh();
  QueuePresetDraft();
 }
 void ShowLargePresetPreview(){
  if(largePresetWindow!=null){largePresetWindow.Activate();return;}
  var dialog=new Window{Owner=this,Title="Превью мини-плеера · реальный размер",Width=Math.Min(900,SystemParameters.WorkArea.Width-60),Height=Math.Min(620,SystemParameters.WorkArea.Height-60),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=BackgroundBrush,Foreground=TextBrush,FontFamily=FontFamily,FontWeight=FontWeight,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize};
  foreach(System.Collections.DictionaryEntry resource in Resources)dialog.Resources[resource.Key]=resource.Value;
  var layout=new DockPanel{Margin=new Thickness(18)};dialog.Content=new Border{Background=BackgroundBrush,BorderBrush=ColorBrush("#3B3D50"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=layout};
  var bar=new DockPanel{Margin=new Thickness(0,0,0,14)};DockPanel.SetDock(bar,Dock.Top);layout.Children.Add(bar);var close=PresetButton("Закрыть превью",dialog.Close);DockPanel.SetDock(close,Dock.Right);bar.Children.Add(close);var caption=Label("Превью · масштаб 100%",16);caption.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed)dialog.DragMove();};bar.Children.Add(caption);
  largePresetPreview=new PresetPreview(manager.Root){FitToView=false,Width=presetDraft!.Width+44,Height=presetDraft.Height+44};largePresetPreview.Update(presetDraft);AttachPresetDragging(largePresetPreview);
  layout.Children.Add(new ScrollViewer{Content=largePresetPreview,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});largePresetWindow=dialog;
  dialog.Closed+=(_,_)=>{largePresetPreview?.Dispose();largePresetPreview=null;largePresetWindow=null;};dialog.Show();
 }
 void RefreshLargePresetPreview(){
  if(presetDraft==null)return;
  if(largePresetPreview!=null){
   largePresetPreview.Width=presetDraft.Width+44;largePresetPreview.Height=presetDraft.Height+44;
   largePresetPreview.Update(presetDraft);SelectInPreview(largePresetPreview);
  }
  if(presetScaleLabel!=null)presetScaleLabel.Text=$"{presetDraft.Width:0} × {presetDraft.Height:0} px · масштаб {presetPreviewZoom*100:0}%\nНажми на элемент, чтобы переместить его или изменить размер.";
 }
 async Task ApplyCurrentPresetAsync(){
  if(presetDraft==null)return;
  var widget=catalog.Single(x=>x.Id=="mini-player");
  if(!manager.IsInstalled(widget))await manager.InstallAsync(widget);
  await PresetWidgetUpgrade.EnsureAsync(manager,widget);
  string appliedPath=Path.Combine(manager.DataDirectory(widget),"preset-applied.json");WidgetManager.RejectLinks(appliedPath);
  string active=Presets.ActivePath;WidgetManager.RejectLinks(active);byte[]? previous=File.Exists(active)?File.ReadAllBytes(active):null;
  var draft=presetDraft;
  var store=Presets;
  var preset=draft.Copy();
  preset.Normalize();
  if(preset.Id.Length==0)preset.Id=Guid.NewGuid().ToString("N");
  bool changed=false;
  try{
   if(File.Exists(appliedPath))File.Delete(appliedPath);
   Presets.Apply(preset);changed=true;string expected=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(active)));
   if(manager.IsRunning(widget)){
    if(!EventWaitHandle.TryOpenExisting(manager.InstanceName(widget)+".ApplyPreset",out var signal))throw new IOException("Эта копия мини-плеера ещё не поддерживает пресеты. Обнови её из локального пакета обновления и повтори применение.");
    using(signal)signal.Set();
   }else await manager.LaunchAsync(widget);
   var deadline=DateTime.UtcNow.AddSeconds(10);
   while(DateTime.UtcNow<deadline){
    if(ReadPresetConfirmation(appliedPath,expected)){
     await manager.LaunchAsync(widget);
     store.SaveDraft(preset);
     store.Save(preset);
     draft.Id=preset.Id;
     return;
    }
    await Task.Delay(100);
   }
   throw new IOException("Плеер не подтвердил применение пресета. Предыдущее оформление восстановлено.");
  }catch{
   if(!changed)throw;
   if(previous==null)Presets.ClearActive();else{string temporary=active+".restore-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temporary,previous);File.Move(temporary,active,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
   if(EventWaitHandle.TryOpenExisting(manager.InstanceName(widget)+".ApplyPreset",out var retry))using(retry)retry.Set();throw;
  }
 }
 static bool ReadPresetConfirmation(string path,string expectedHash){
  if(!File.Exists(path))return false;
  using var acknowledgement=ReadAcknowledgement();
  if(acknowledgement==null)return false;
  var data=acknowledgement.RootElement;
  if(!string.Equals(data.GetProperty("Sha256").GetString(),expectedHash,StringComparison.OrdinalIgnoreCase))return false;
  if(!data.GetProperty("Success").GetBoolean())throw new IOException(data.GetProperty("Error").GetString());
  return true;

  JsonDocument? ReadAcknowledgement(){
   try{return JsonDocument.Parse(File.ReadAllText(path));}
   catch(IOException){return null;}
   catch(JsonException){return null;}
  }
 }
}
