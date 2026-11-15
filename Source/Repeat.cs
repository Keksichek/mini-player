using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Media;
using Windows.Media.Control;

namespace MiniPlayer;

public sealed partial class Player
{
 readonly Button repeat=new(){Width=30,Height=26,Padding=new Thickness(5),IsEnabled=false,Cursor=Cursors.Hand};
 MediaPlaybackAutoRepeatMode repeatMode=MediaPlaybackAutoRepeatMode.None;
 bool repeatPending,repeatSupported,repeatKnown;
 int repeatRequest;
 GlobalSystemMediaTransportControlsSession? repeatSession;
 MediaPlaybackAutoRepeatMode? repeatEchoMode;
 long repeatEchoUntil;
 string repeatError="";

 void InitializeRepeat(){repeat.Click+=async(_,_)=>await CycleRepeatAsync();UpdateRepeatControl();}

 static MediaPlaybackAutoRepeatMode NextRepeatMode(MediaPlaybackAutoRepeatMode mode)=>mode switch{
  MediaPlaybackAutoRepeatMode.None=>MediaPlaybackAutoRepeatMode.List,
  MediaPlaybackAutoRepeatMode.List=>MediaPlaybackAutoRepeatMode.Track,
  _=>MediaPlaybackAutoRepeatMode.None
 };

 void ResetRepeat(){
  ++repeatRequest;repeatPending=false;repeatSupported=false;repeatKnown=false;
  repeatMode=MediaPlaybackAutoRepeatMode.None;repeatSession=session;repeatEchoMode=null;repeatError="";
  UpdateRepeatControl();
 }

 void SynchronizeRepeat(GlobalSystemMediaTransportControlsSessionPlaybackInfo playback){
  if(repeatSession!=session)ResetRepeat();
  var mode=playback.AutoRepeatMode;
  repeatKnown=mode.HasValue&&Enum.IsDefined(mode.Value);
  repeatSupported=playback.Controls.IsRepeatEnabled&&repeatKnown;
  if(!repeatPending){
   if(!repeatSupported||mode==repeatEchoMode||Environment.TickCount64>=repeatEchoUntil)repeatEchoMode=null;
   repeatMode=repeatEchoMode??(repeatKnown?mode!.Value:MediaPlaybackAutoRepeatMode.None);
  }
  UpdateRepeatControl();
 }

 void UpdateRepeatControl(){
  repeat.IsEnabled=session!=null&&repeatSupported&&!repeatPending;
  repeat.Tag=repeatMode;
  repeat.Content=PlayerIcons.Repeat(repeatMode,repeat.Foreground);
  string label=repeatMode switch{
   MediaPlaybackAutoRepeatMode.List=>"Повтор альбома / плейлиста",
   MediaPlaybackAutoRepeatMode.Track=>"Повтор одного трека",
   _=>"Без повтора"
  };
  repeat.ToolTip=repeatPending?"Меняем режим повтора…":!string.IsNullOrEmpty(repeatError)?repeatError:
   session==null?"Выберите источник в настройках, чтобы управлять повтором.":
   !repeatSupported?"Источник не поддерживает управление повтором. Измените повтор в музыкальном приложении.":
   label+". Нажмите: "+(repeatMode switch{
    MediaPlaybackAutoRepeatMode.None=>"повторять альбом / плейлист.",
    MediaPlaybackAutoRepeatMode.List=>"повторять один трек.",
    _=>"выключить повтор."
   });
  System.Windows.Automation.AutomationProperties.SetName(repeat,label);
  ToolTipService.SetShowOnDisabled(repeat,true);
  UpdateFaceRepeat();
 }

 async Task CycleRepeatAsync(){
  var selected=session;if(selected==null||repeatPending)return;
  try{SynchronizeRepeat(selected.GetPlaybackInfo());}
  catch{repeatError="Не удалось получить режим повтора. Источник мог закрыться.";repeatSupported=false;UpdateRepeatControl();return;}
  if(!repeatSupported)return;
  var requested=NextRepeatMode(repeatMode);int request=++repeatRequest;
  repeatPending=true;repeatError="";UpdateRepeatControl();
  try{
   bool accepted=await selected.TryChangeAutoRepeatModeAsync(requested);
   if(request!=repeatRequest||selected!=session||closing)return;
   if(accepted){
    // Sources can publish their new state shortly after acknowledging a command.
    // Keep the accepted value briefly, then reconcile it with subsequent snapshots.
    repeatMode=requested;repeatEchoMode=requested;repeatEchoUntil=Environment.TickCount64+1800;
   }else{
    repeatError="Источник отклонил изменение повтора. Измените режим в музыкальном приложении.";
    status.Text=repeatError;
   }
  }catch{
   if(request==repeatRequest&&selected==session&&!closing){repeatError="Не удалось изменить повтор. Источник мог закрыться.";status.Text=repeatError;}
  }finally{
   if(request==repeatRequest&&selected==session){repeatPending=false;UpdateRepeatControl();}
  }
 }
}
