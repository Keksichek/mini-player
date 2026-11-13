using System;

namespace MiniPlayer;

// Windows reports a position snapshot. Advance it locally until the next snapshot arrives.
sealed class PlaybackClock
{
 readonly TimeProvider time;
 double position,rate=1,reportedPosition;
 long anchoredAt;
 DateTimeOffset reportedAt;
 bool playing;
 public bool HasSnapshot {get;private set;}

 public PlaybackClock(TimeProvider? time=null){this.time=time??TimeProvider.System;}

 public double Position=>HasSnapshot
  ? Math.Max(0,position+(playing?time.GetElapsedTime(anchoredAt).TotalSeconds*rate:0))
  : 0;

 public void Synchronize(double reported,DateTimeOffset updatedAt,bool isPlaying,double? playbackRate){
  double nextRate=playbackRate is double value&&double.IsFinite(value)?value:1;
  if(!HasSnapshot||reported!=reportedPosition||updatedAt!=reportedAt){
   position=Math.Max(0,reported);
   if(isPlaying&&updatedAt>DateTimeOffset.UnixEpoch&&(!HasSnapshot||playing))
    position+=Math.Max(0,(time.GetUtcNow()-updatedAt).TotalSeconds)*nextRate;
  }else position=Position;
  reportedPosition=reported;reportedAt=updatedAt;
  playing=isPlaying;rate=nextRate;anchoredAt=time.GetTimestamp();HasSnapshot=true;
 }

 public void Seek(double seconds){position=Math.Max(0,seconds);anchoredAt=time.GetTimestamp();}
 public void Reset(){HasSnapshot=false;playing=false;position=0;}
}
