using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 const string ExchangeFilter="Пресет VibeWidgetsHub (*.vibepreset)|*.vibepreset";

 void AddPresetExportActions(Panel panel){
  var copy=PresetButton("Сохранить как копию",()=>PresetTry(()=>{
   if(presetDraft==null||string.IsNullOrWhiteSpace(presetDraft.Name))throw new IOException("Укажи название пресета.");
   EditSavedCopy(Presets.SaveCopy(presetDraft));
   message="Копия сохранена. Теперь ты редактируешь её.";
   Render();
  }));
  copy.Margin=new Thickness(0,8,0,0);
  panel.Children.Add(copy);
  var row=new WrapPanel{Margin=new Thickness(0,8,0,0)};
  panel.Children.Add(row);
  var file=PresetButton("Экспорт в файл",ChoosePresetExportFile);
  file.Margin=new Thickness(0,0,6,4);
  row.Children.Add(file);
  var code=PresetButton("Скопировать код",CopyPresetCode);
  code.ToolTip="Скопировать настройки и фон пресета в буфер обмена";
  row.Children.Add(code);
 }

 void AddPresetImportActions(Panel panel){
  var row=new WrapPanel{Margin=new Thickness(0,0,0,10)};
  panel.Children.Add(row);
  var file=PresetButton("Импорт из файла",ChoosePresetImportFile);
  file.Margin=new Thickness(0,0,6,4);
  row.Children.Add(file);
  var code=PresetButton("Вставить код",PastePresetCode);
  code.ToolTip="Импортировать код пресета из буфера обмена";
  row.Children.Add(code);
 }

 void EditSavedCopy(WidgetPreset preset){
  presetDraftTimer.Stop();
  presetDraft=preset;
  presetDraftDirty=true;
  SavePresetDraft();
  presetHistory.Clear();
  customizationMode="saved";
  customizationStep="editor";
 }

 void ChoosePresetExportFile(){
  if(presetDraft==null)return;
  var preset=presetDraft.Copy();
  string fileName=new string(preset.Name.Where(c=>!Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
  var picker=new SaveFileDialog{Title="Экспорт пресета",Filter=ExchangeFilter,DefaultExt=PresetExchange.Extension,AddExtension=true,FileName="Vibe - "+(fileName.Length==0?"Пресет":fileName)};
  if(picker.ShowDialog(this)!=true)return;
  var store=Presets;
  RunAction(()=>Task.Run(()=>PresetExchange.ExportFile(store,preset,picker.FileName)),"Пресет экспортирован вместе с его оформлением.");
 }

 void CopyPresetCode(){
  if(presetDraft==null)return;
  var preset=presetDraft.Copy();
  var store=Presets;
  RunAction(async()=>{
   string code=await Task.Run(()=>PresetExchange.ExportCode(store,preset));
   Clipboard.SetText(code);
  },"Код пресета скопирован. Для импорта открой «Созданные пресеты» и нажми «Вставить код».");
 }

 void ChoosePresetImportFile(){
  var picker=new OpenFileDialog{Title="Импорт пресета",Filter=ExchangeFilter};
  if(picker.ShowDialog(this)==true)ImportSharedPreset(store=>PresetExchange.ImportFile(store,picker.FileName));
 }

 void PastePresetCode(){
  try{
   string code=Clipboard.GetText();
   if(string.IsNullOrWhiteSpace(code))throw new InvalidDataException("Скопируй код пресета, затем нажми «Вставить код».");
   ImportSharedPreset(store=>PresetExchange.ImportCode(store,code));
  }catch(Exception ex){message=ex.Message;Render();}
 }

 void ImportSharedPreset(Func<PresetStore,WidgetPreset> read){
  var store=Presets;
  SavePresetDraft();
  RunAction(async()=>{
   var imported=await Task.Run(()=>read(store));
   EditSavedCopy(imported);
  },"Пресет добавлен в «Созданные пресеты». Нажми «Применить», чтобы использовать его в плеере.");
 }
}
