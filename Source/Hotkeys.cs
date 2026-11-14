using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;

namespace MiniPlayer;

public sealed partial class Player
{
 enum HotkeyAction { Pause,Play,Next,Previous,Pin,Unpin }
 sealed record HotkeyBinding(ModifierKeys Modifiers,Key Key);
 readonly Dictionary<HotkeyAction,HotkeyBinding> hotkeyBindings=new();
 readonly Dictionary<HotkeyAction,int> registeredHotkeys=new();
 readonly Dictionary<HotkeyAction,string> hotkeyErrors=new();
 HwndSource? hotkeySource;
 bool capturingHotkey;
 long captureEndedAt=-1;
 const int HotkeyMessage=0x0312;
 const uint NoHotkeyRepeat=0x4000;
 [DllImport("user32.dll",SetLastError=true)]
 static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll",SetLastError=true)]
 static extern bool UnregisterHotKey(IntPtr window,int id);
 [DllImport("user32.dll")]
 static extern int GetMessageTime();

 string HotkeysPath=>Path.Combine(Path.GetDirectoryName(settingsPath)!,"hotkeys.json");
 string HotkeyWarning=>string.Join("\n",hotkeyErrors.Select(pair=>HotkeyActionName(pair.Key)+" — "+pair.Value));
 static string HotkeyActionName(HotkeyAction action)=>action switch{
  HotkeyAction.Pause=>"Пауза",HotkeyAction.Play=>"Воспроизведение",HotkeyAction.Next=>"Следующий трек",
  HotkeyAction.Previous=>"Предыдущий трек",HotkeyAction.Pin=>"Закрепить",_=>"Открепить"
 };
 static string HotkeyLabel(HotkeyBinding binding){
  var parts=new List<string>();
  if(binding.Modifiers.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");
  if(binding.Modifiers.HasFlag(ModifierKeys.Alt))parts.Add("Alt");
  if(binding.Modifiers.HasFlag(ModifierKeys.Shift))parts.Add("Shift");
  if(binding.Modifiers.HasFlag(ModifierKeys.Windows))parts.Add("Win");
  parts.Add(new KeyConverter().ConvertToInvariantString(binding.Key)??binding.Key.ToString());
  return string.Join(" + ",parts);
 }
 static bool ValidHotkey(HotkeyBinding binding,out string error){
  error="";
  if(!Enum.IsDefined(binding.Key)||binding.Key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
   or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
   or Key.LWin or Key.RWin or Key.F12||KeyInterop.VirtualKeyFromKey(binding.Key)==0
   ||(binding.Modifiers&~(ModifierKeys.Control|ModifierKeys.Alt|ModifierKeys.Shift|ModifierKeys.Windows))!=0){
   error=binding.Key==Key.F12?"F12 зарезервирована Windows. Выберите другое сочетание.":"Выберите клавишу вместе с модификаторами.";
   return false;
  }
  bool standalone=binding.Key is >=Key.F1 and <=Key.F24 or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop;
  if(!standalone&&(binding.Modifiers&(ModifierKeys.Control|ModifierKeys.Alt|ModifierKeys.Windows))==0){
   error="Добавьте Ctrl, Alt или Win. Функциональные и медиаклавиши можно назначать отдельно.";
   return false;
  }
  return true;
 }

 void InitializeHotkeys(){
  if(hotkeySource!=null)return;
  hotkeySource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
  if(hotkeySource==null)return;
  hotkeySource.AddHook(HandleHotkeyMessage);
  try{
   var saved=JsonSerializer.Deserialize<Dictionary<HotkeyAction,HotkeyBinding>>(File.ReadAllText(HotkeysPath));
   if(saved!=null)foreach(var pair in saved){
    if(Enum.IsDefined(pair.Key)&&pair.Value!=null&&ValidHotkey(pair.Value,out _)
     &&!hotkeyBindings.Values.Contains(pair.Value))hotkeyBindings[pair.Key]=pair.Value;
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}
  RegisterConfiguredHotkeys();
 }
 bool RegisterBinding(HotkeyAction action,HotkeyBinding binding,out int id,out string error){
  int first=0x6000+(int)action*2;
  id=registeredHotkeys.GetValueOrDefault(action)==first?first+1:first;
  uint modifiers=(uint)binding.Modifiers|NoHotkeyRepeat;
  if(RegisterHotKey(hotkeySource!.Handle,id,modifiers,(uint)KeyInterop.VirtualKeyFromKey(binding.Key))){error="";return true;}
  int code=Marshal.GetLastWin32Error();
  error=code==1409?"Сочетание уже используется Windows или другим приложением.":"Windows не разрешила назначить это сочетание. Выберите другое.";
  return false;
 }
 void RegisterConfiguredHotkeys(){
  if(closing||capturingHotkey||hotkeySource==null)return;
  foreach(var pair in hotkeyBindings){
   if(registeredHotkeys.ContainsKey(pair.Key))continue;
   if(RegisterBinding(pair.Key,pair.Value,out int id,out string error)){
    registeredHotkeys[pair.Key]=id;hotkeyErrors.Remove(pair.Key);
   }else hotkeyErrors[pair.Key]=error;
  }
 }
 void WriteHotkeys(Dictionary<HotkeyAction,HotkeyBinding> bindings){
  string folder=Path.GetDirectoryName(HotkeysPath)!;
  Directory.CreateDirectory(folder);
  string temporary=Path.Combine(folder,"hotkeys."+Guid.NewGuid().ToString("N")+".tmp");
  try{
   File.WriteAllText(temporary,JsonSerializer.Serialize(bindings));
   File.Move(temporary,HotkeysPath,true);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 bool TryAssignHotkey(HotkeyAction action,HotkeyBinding? binding,out string error){
  error="";
  if(!Enum.IsDefined(action)){error="Неизвестное действие.";return false;}
  if(binding!=null&&!ValidHotkey(binding,out error))return false;
  new WindowInteropHelper(this).EnsureHandle();
  InitializeHotkeys();
  if(binding!=null&&hotkeyBindings.Any(pair=>pair.Key!=action&&pair.Value==binding)){
   error="Это сочетание уже назначено другому действию плеера.";return false;
  }
  if(binding!=null&&hotkeyBindings.GetValueOrDefault(action)==binding&&registeredHotkeys.ContainsKey(action))return true;
  int newId=0;
  if(binding!=null&&!RegisterBinding(action,binding,out newId,out error))return false;
  var candidate=new Dictionary<HotkeyAction,HotkeyBinding>(hotkeyBindings);
  if(binding==null)candidate.Remove(action);else candidate[action]=binding;
  try{WriteHotkeys(candidate);}
  catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){
   if(binding!=null)UnregisterHotKey(hotkeySource!.Handle,newId);
   error="Не удалось сохранить сочетание. "+ex.Message;return false;
  }
  if(registeredHotkeys.Remove(action,out int oldId))UnregisterHotKey(hotkeySource!.Handle,oldId);
  if(binding!=null)registeredHotkeys[action]=newId;
  hotkeyBindings.Clear();foreach(var pair in candidate)hotkeyBindings.Add(pair.Key,pair.Value);
  hotkeyErrors.Remove(action);
  return true;
 }
 void BeginHotkeyCapture(){
  new WindowInteropHelper(this).EnsureHandle();InitializeHotkeys();
  capturingHotkey=true;
  ReleaseHotkeyRegistrations();
 }
 void EndHotkeyCapture(){
  capturingHotkey=false;captureEndedAt=Environment.TickCount64;
  RegisterConfiguredHotkeys();
 }
 void ReleaseHotkeyRegistrations(){
  if(hotkeySource!=null)foreach(int id in registeredHotkeys.Values)UnregisterHotKey(hotkeySource.Handle,id);
  registeredHotkeys.Clear();
 }
 void DisposeHotkeys(){
  ReleaseHotkeyRegistrations();
  hotkeySource?.RemoveHook(HandleHotkeyMessage);hotkeySource=null;
 }
 IntPtr HandleHotkeyMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message!=HotkeyMessage)return IntPtr.Zero;
  var registered=registeredHotkeys.FirstOrDefault(pair=>pair.Value==wParam.ToInt32());
  if(registered.Value==0)return IntPtr.Zero;
  handled=true;
  uint age=unchecked((uint)(Environment.TickCount-GetMessageTime()));
  long queuedAt=Environment.TickCount64-age;
  if(!closing&&!capturingHotkey&&queuedAt>captureEndedAt)_=ExecuteHotkey(registered.Key);
  return IntPtr.Zero;
 }
 async Task ExecuteHotkey(HotkeyAction action){
  if(closing||capturingHotkey)return;
  if(action is HotkeyAction.Pin or HotkeyAction.Unpin){
   Topmost=action==HotkeyAction.Pin;UpdatePin();Save();return;
  }
  await Command(action switch{HotkeyAction.Previous=>0,HotkeyAction.Next=>2,HotkeyAction.Pause=>3,_=>4});
  await Refresh();
 }
}
