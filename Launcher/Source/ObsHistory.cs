using System;
using System.Collections.Generic;
using System.Text.Json;
namespace WidgetLauncher;
// History belongs to the editor draft; applying or undoing never writes live OBS implicitly.
public sealed class ObsHistory
{
 readonly List<string> states=new();readonly int limit;int cursor;
 public ObsHistory(ObsAppearance initial,int limit=100){this.limit=Math.Max(1,limit);states.Add(JsonSerializer.Serialize(initial.Copy()));}
 public bool CanUndo=>cursor>0;public bool CanRedo=>cursor<states.Count-1;
 public bool IsCurrent(ObsAppearance value)=>states[cursor]==JsonSerializer.Serialize(value);
 public void Record(ObsAppearance value){string state=JsonSerializer.Serialize(value.Copy());if(states[cursor]==state)return;if(CanRedo)states.RemoveRange(cursor+1,states.Count-cursor-1);states.Add(state);cursor++;if(states.Count>limit+1){states.RemoveAt(0);cursor--;}}
 public ObsAppearance? Undo()=>CanUndo?Restore(--cursor):null;
 public ObsAppearance? Redo()=>CanRedo?Restore(++cursor):null;
 ObsAppearance Restore(int index)=>JsonSerializer.Deserialize<ObsAppearance>(states[index])!.Normalize();
}
