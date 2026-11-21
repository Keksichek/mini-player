using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetLauncher;

public sealed partial class LauncherWindow
{
 const int PresetsPerPage=8;
 int savedGalleryPage;

 void RenderSavedPresetGallery(){
  content.Children.Add(Label("Мини-плеер · созданные пресеты",20,TextBrush,new Thickness(0,0,0,14)));
  AddPresetImportActions(content);
  var store=Presets;
  IReadOnlyList<(WidgetPreset Preset,bool Favorite)> saved;
  try{saved=store.ListSaved();}
  catch(Exception ex){content.Children.Add(Label("Не удалось прочитать пресеты: "+ex.Message,12,ColorBrush("#F0A2AC")));return;}
  if(saved.Count==0){content.Children.Add(Label("Сохрани первый вариант в конфигураторе — он появится здесь.",12,MutedBrush));return;}

  var pageBody=new StackPanel();content.Children.Add(pageBody);
  var posters=new Dictionary<string,BitmapSource>();
  void ShowPage(int pageIndex){
   pageBody.Children.Clear();
   int pages=(saved.Count+PresetsPerPage-1)/PresetsPerPage;
   savedGalleryPage=Math.Clamp(pageIndex,0,Math.Max(0,pages-1));
   if(pages>1){
    var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,6,8)};
    var previous=PresetButton("Предыдущая страница",()=>{ShowPage(savedGalleryPage-1);pageScroll.ScrollToTop();});
    var next=PresetButton("Следующая страница",()=>{ShowPage(savedGalleryPage+1);pageScroll.ScrollToTop();});
    previous.Content="←";previous.IsEnabled=savedGalleryPage>0;next.Content="→";next.IsEnabled=savedGalleryPage+1<pages;
    navigation.Children.Add(previous);
    var number=Label($"{savedGalleryPage+1} / {pages}",12,MutedBrush,new Thickness(10,0,10,0));
    number.VerticalAlignment=VerticalAlignment.Center;navigation.Children.Add(number);
    navigation.Children.Add(next);pageBody.Children.Add(navigation);
   }
   var grid=new UniformGrid{Columns=2};pageBody.Children.Add(grid);
   foreach(var (preset,favorite) in saved.Skip(savedGalleryPage*PresetsPerPage).Take(PresetsPerPage)){
    var card=new Grid{Margin=new Thickness(0,0,8,8)};grid.Children.Add(card);
    var stack=new StackPanel();
    var image=new Image{Height=118,Stretch=Stretch.Uniform,IsHitTestVisible=false};stack.Children.Add(image);
    image.Loaded+=(_,_)=>Dispatcher.BeginInvoke((Action)(()=>{
     if(!image.IsLoaded)return;
     if(!posters.TryGetValue(preset.Id,out var thumbnail)){
      thumbnail=RenderPresetThumbnail(manager.Root,preset);
      if(posters.Count>=3*PresetsPerPage)posters.Clear();
      posters[preset.Id]=thumbnail;
     }
     image.Source=thumbnail;
    }),DispatcherPriority.Background);
    var name=Label(preset.Name,14,TextBrush,new Thickness(0,10,34,0));
    name.MinHeight=28;name.VerticalAlignment=VerticalAlignment.Center;
    name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.ToolTip=preset.Name;
    stack.Children.Add(name);
    var open=PresetButton("Мой пресет: "+preset.Name,()=>OpenPreset(preset));
    open.Content=stack;open.Background=PanelBrush;open.Padding=new Thickness(12);
    open.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.Children.Add(open);

    string action=favorite?"Убрать из избранного":"В избранное";
    var star=PresetButton(action+" · "+preset.Name,()=>PresetTry(()=>{
     store.SetFavorite(preset.Id,!favorite);
     var sorted=PresetStore.SortSaved(saved.Select(entry=>entry.Preset.Id==preset.Id?(preset,!favorite):entry));
     saved=sorted;
     ShowPage(Array.FindIndex(sorted,entry=>entry.Preset.Id==preset.Id)/PresetsPerPage);
    }));
    star.ToolTip=action;star.Content=favorite?"★":"☆";star.FontFamily=new FontFamily("Segoe UI Symbol");
    star.FontSize=20;star.Padding=new Thickness(0);star.Width=34;star.Height=32;
    star.Foreground=favorite?AccentBrush:MutedBrush;star.Background=Brushes.Transparent;
    star.VerticalAlignment=VerticalAlignment.Bottom;star.HorizontalAlignment=HorizontalAlignment.Right;
    star.Margin=new Thickness(0,0,8,10);card.Children.Add(star);
   }
  }
  ShowPage(savedGalleryPage);
 }

 static BitmapSource RenderPresetThumbnail(string root,WidgetPreset preset){
  using var surface=new PresetSurface(root,firstFrameOnly:true);
  surface.Configure(preset);
  var view=new Viewbox{Child=surface,Stretch=Stretch.Uniform};
  view.Measure(new Size(320,144));
  var size=view.DesiredSize;view.Arrange(new Rect(size));view.UpdateLayout();
  var image=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
  image.Render(view);image.Freeze();return image;
 }
}
