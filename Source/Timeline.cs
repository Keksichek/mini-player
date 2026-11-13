using System;
using Windows.Media.Control;

namespace MiniPlayer;

public sealed partial class Player
{
 readonly PlaybackClock playbackClock=new();
 double timelineDuration;
 int seekRequest;
 bool seekPending;

 void SynchronizeTimeline(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
  GlobalSystemMediaTransportControlsSessionPlaybackInfo playback){
  playbackClock.Synchronize(timeline.Position.TotalSeconds,timeline.LastUpdatedTime,
   playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,playback.PlaybackRate);
  timelineDuration=Math.Max(0,timeline.EndTime.TotalSeconds);
  if(seeking||seekPending)return;
  updatingSeek=true;
  try{
   seek.Minimum=Math.Max(0,timeline.MinSeekTime.TotalSeconds);
   seek.Maximum=Math.Max(seek.Minimum+1,timeline.MaxSeekTime>TimeSpan.Zero?timeline.MaxSeekTime.TotalSeconds:timelineDuration);
   seek.IsEnabled=playback.Controls.IsPlaybackPositionEnabled&&timelineDuration>0;
  }finally{updatingSeek=false;}
  RenderTimeline();
 }

 void RenderTimeline(){
  if(!playbackClock.HasSnapshot||seeking||seekPending)return;
  updatingSeek=true;
  try{
   double end=timelineDuration>0?Math.Min(timelineDuration,seek.Maximum):seek.Maximum;
   seek.Value=Math.Clamp(playbackClock.Position,seek.Minimum,Math.Max(seek.Minimum,end));
   timeLabel.Text=$"{TimeSpan.FromSeconds(seek.Value):m\\:ss} / {TimeSpan.FromSeconds(timelineDuration):m\\:ss}";
  }finally{updatingSeek=false;}
 }

 void ResetTimeline(){
  playbackClock.Reset();seekRequest++;seekPending=false;timelineDuration=0;
  updatingSeek=true;
  try{seek.Value=seek.Minimum;timeLabel.Text="0:00 / 0:00";}
  finally{updatingSeek=false;}
 }
}
