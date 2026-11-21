using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace MiniPlayer;

public sealed partial class Player
{
 const int ExtendedStyle=-20, TransparentToMouse=0x20;
 bool clickThrough;
 Button? clickThroughButton,faceClickThrough;
 Forms.ToolStripMenuItem? restoreMouseItem;
 const string EnablePassThrough="Включить клики насквозь";
 const string DisablePassThrough="Выключить клики насквозь";
 const string PassThroughHelp="Вернуть управление можно через значок плеера в трее.";

 [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]
 static extern int ReadExtendedStyle(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]
 static extern int WriteExtendedStyle(IntPtr hwnd,int index,int style);

 void ToggleClickThrough()=>SetClickThrough(!clickThrough);
 void SetClickThrough(bool enabled,bool persist=true){
  var handle=new WindowInteropHelper(this).Handle;
  if(handle!=IntPtr.Zero){
   int style=ReadExtendedStyle(handle,ExtendedStyle);
   if(style==0&&Marshal.GetLastPInvokeError()!=0){status.Text="Не удалось прочитать режим окна.";return;}
   // AllowsTransparency already gives the player a layered HWND. Only change its mouse transparency.
   int next=enabled?style|TransparentToMouse:style&~TransparentToMouse;
   if(next!=style&&WriteExtendedStyle(handle,ExtendedStyle,next)==0&&Marshal.GetLastPInvokeError()!=0){
    status.Text="Windows не удалось изменить режим кликов.";return;
   }
  }
  clickThrough=enabled;
  UpdateClickThroughButtons();
  if(restoreMouseItem!=null)restoreMouseItem.Visible=enabled;
  if(IsLoaded)UpdateRecoveryArrow();
  if(persist)Save();
 }
 void AddTrayRestore(Forms.ContextMenuStrip menu){
  restoreMouseItem=new Forms.ToolStripMenuItem("Выключить клики насквозь"){Visible=clickThrough};
  restoreMouseItem.Click+=(_,_)=>Dispatcher.Invoke(()=>SetClickThrough(false));
  menu.Items.Add(restoreMouseItem);
 }
 void UpdateClickThroughButtons(){
  string action=clickThrough?DisablePassThrough:EnablePassThrough;
  foreach(var button in new[]{clickThroughButton,faceClickThrough}){
   if(button==null)continue;
   Brush ink=clickThrough?AccentInk(appearance.Accent):button.Foreground;
   button.Content=PlayerIcons.ClickThrough(clickThrough,ink);
   button.Background=clickThrough?BrushOf(appearance.Accent):button==faceClickThrough?UtilitySurface():Brushes.Transparent;
   button.ToolTip=action+". "+PassThroughHelp;
   AutomationProperties.SetName(button,action);
  }
  if(customSurface!=null){
   customSurface.SetButtonIcon("clickthrough",clickThrough?"on":"off");
   var button=customSurface.Elements["clickthrough"];
   button.ToolTip=action+". "+PassThroughHelp;
   AutomationProperties.SetName(button,action);
  }
 }
}
