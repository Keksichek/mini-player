using System.Windows;
using System.Windows.Media;

namespace MiniPlayer;

public sealed partial class Player
{
 bool movementLocked;
 bool CanDragPlayer=>!movementLocked;
 void ToggleMovementLock(){movementLocked=!movementLocked;UpdateMovementLock();Save();}
 void UpdateMovementLock(){
  UpdateFaceLock();
  if(lockButton==null)return;
  lockButton.Content=PlayerIcons.MovementLock(movementLocked,Brushes.SlateGray);
  lockButton.Background=movementLocked?BrushOf(appearance.Accent):Brushes.Transparent;
  lockButton.ToolTip=movementLocked?"Перемещение заблокировано — разрешить":"Перемещение разрешено — заблокировать";
  System.Windows.Automation.AutomationProperties.SetName(lockButton,(string)lockButton.ToolTip);
  System.Windows.Automation.AutomationProperties.SetHelpText(lockButton,"Блокировка перемещения не меняет отображение поверх окон.");
 }
}
