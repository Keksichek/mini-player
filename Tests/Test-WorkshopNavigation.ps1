using namespace System.Windows.Automation
param([Parameter(Mandatory=$true)][string]$InstallDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes

$root=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($InstallDirectory))
$marker=Get-Content -LiteralPath (Join-Path $root 'launcher-install.json') -Raw | ConvertFrom-Json
function PathHash([string]$path){
 [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($path.ToUpperInvariant()))).Substring(0,16)
}
if($marker.TestInstallation -ne $true -or $marker.ProductId -ne 'MiniPlayer.WidgetLauncher' -or $marker.InstallPath -ine $root -or $marker.InstallId -ine (PathHash $root)){throw 'Use an isolated test installation.'}
$runtime=Join-Path $root 'Runtime\dotnet.exe'
$playerDll=Join-Path $root 'Widgets\mini-player\App\MiniPlayer.dll'
if(!(Test-Path -LiteralPath $playerDll)){throw 'Install the test widget first.'}
$data=Join-Path $root 'Data\mini-player'
$presets=Join-Path $root 'Data\Customization\mini-player'
$hubEvent='Local\WidgetLauncher.'+(PathHash $root)
$playerEvent='Local\MiniPlayer.'+(PathHash $data)
foreach($identity in @($hubEvent,$playerEvent)){
 $mutex=$null
 if([Threading.Mutex]::TryOpenExisting($identity,[ref]$mutex)){$mutex.Dispose();throw 'Close the test applications first.'}
}

function Signal([string]$eventName){
 $handle=$null
 if([Threading.EventWaitHandle]::TryOpenExisting($eventName,[ref]$handle)){
  try{[void]$handle.Set()}finally{$handle.Dispose()}
 }
}
function WaitFor([scriptblock]$read,[string]$label){
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 do{
  try{$value=& $read;if($value){return $value}}catch [ElementNotAvailableException]{}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $deadline)
 throw ('Timed out: '+$label)
}
function FindWindow([int]$processId,[string]$title){
 $id=[PropertyCondition]::new([AutomationElement]::ProcessIdProperty,$processId)
 $name=[PropertyCondition]::new([AutomationElement]::NameProperty,$title)
 [AutomationElement]::RootElement.FindFirst([TreeScope]::Children,[AndCondition]::new($id,$name))
}
function Control($window,[string]$label,$type){
 if(!$window){return}
 $name=[PropertyCondition]::new([AutomationElement]::NameProperty,$label)
 $kind=[PropertyCondition]::new([AutomationElement]::ControlTypeProperty,$type)
 $window.FindFirst([TreeScope]::Descendants,[AndCondition]::new($name,$kind))
}
function Click($button){$button.GetCurrentPattern([InvokePattern]::Pattern).Invoke()}
function FixtureHub{
 foreach($process in [Diagnostics.Process]::GetProcessesByName('dotnet')){
  try{
   if($process.Id -ne $player.Id -and $process.MainModule.FileName -ieq $runtime){
    $window=FindWindow $process.Id 'VibeWidgetsHub alpha'
    if($window){return $window}
   }
  }catch [ComponentModel.Win32Exception]{}catch [InvalidOperationException]{}finally{$process.Dispose()}
 }
}
function AppearanceWindow{
 Signal ($playerEvent+'.Customize')
 WaitFor {FindWindow $player.Id 'Mini Player · 0.1 · Оформление'} 'player appearance window'
}
function OpenFromPlayer{
 $window=AppearanceWindow
 $button=WaitFor {Control $window 'Открыть в мастерской' ([ControlType]::Button)} 'player workshop button'
 Click $button
}
function WaitEditor([string]$expected){
 WaitFor {
  $window=FixtureHub
  $field=Control $window 'Название пресета' ([ControlType]::Edit)
  if($field -and $field.GetCurrentPattern([ValuePattern]::Pattern).Current.Value -ceq $expected){$window}
 } ('visible workshop editing '+$expected)
}

$active=Join-Path $presets 'active.json'
$draft=Join-Path $presets 'draft.json'
$settings=Join-Path $root 'Data\hub-settings.json'
$appearance=Join-Path $data 'appearance.json'
$id=[Guid]::NewGuid().ToString('N')
$library=Join-Path $presets ('Presets\'+$id+'.json')
$original=@{}
foreach($path in @($active,$draft,$settings,$appearance,(Join-Path $data 'settings.json'),(Join-Path $data 'hotkeys.json'),(Join-Path $data 'preset-applied.json'),$library)){
 $original[$path]=if([IO.File]::Exists($path)){,([IO.File]::ReadAllBytes($path))}else{$null}
}
$player=$null
try{
 [void][IO.Directory]::CreateDirectory((Split-Path $library))
 [void][IO.Directory]::CreateDirectory($data)
 $fixture=@{Name='Workshop navigation fixture';Id=$id;Theme='Лес'}|ConvertTo-Json
 [IO.File]::WriteAllText($library,$fixture)
 [IO.File]::Delete($active)
 [IO.File]::Delete($draft)
 [IO.File]::WriteAllText($appearance,'{"Theme":"Кофе","Accent":"#F0AA60"}')
 [IO.File]::WriteAllText($settings,'{"CloseMode":1,"AutoUpdateEnabled":false}')
 [IO.File]::WriteAllText((Join-Path $data 'settings.json'),'{"Auto":false,"Pin":false}')
 [IO.File]::WriteAllText((Join-Path $data 'hotkeys.json'),'{}')
 $start=[Diagnostics.ProcessStartInfo]::new($runtime)
 $start.WorkingDirectory=$root
 $start.UseShellExecute=$false
 $start.CreateNoWindow=$true
 $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 foreach($argValue in @($playerDll,'--launcher-root',$root,'--customize')){$start.ArgumentList.Add($argValue)}
 $player=[Diagnostics.Process]::Start($start)
 $preferences=AppearanceWindow
 $choices=WaitFor {Control $preferences 'Сохранённые пресеты' ([ControlType]::ComboBox)} 'saved presets list'
 $choices.GetCurrentPattern([ExpandCollapsePattern]::Pattern).Expand()
 $item=WaitFor {Control $choices 'Workshop navigation fixture' ([ControlType]::ListItem)} 'saved preset in dropdown'
 $item.GetCurrentPattern([SelectionItemPattern]::Pattern).Select()
 [void](WaitFor {[IO.File]::Exists($active) -and (Get-Content -LiteralPath $active -Raw | ConvertFrom-Json).Id -eq $id} 'widget applied selected preset')
 Write-Output 'PASS Choosing a saved preset in the real player immediately changes its active selection'
 OpenFromPlayer
 $hub=WaitEditor 'Workshop navigation fixture'
 $hubPid=$hub.Current.ProcessId
 Write-Output 'PASS The actual widget button starts the closed launcher on its active preset'
 Click (Control $hub 'Закрыть' ([ControlType]::Button))
 [void](WaitFor {!(FindWindow $hubPid 'VibeWidgetsHub alpha')} 'hub hidden to tray')

 $preferences=AppearanceWindow
 Click (Control $preferences 'Темы' ([ControlType]::Button))
 Click (WaitFor {Control $preferences 'Кофе' ([ControlType]::Button)} 'classic Coffee card')
 Click (Control $preferences 'Сохранить' ([ControlType]::Button))
 [void](WaitFor {!(FindWindow $player.Id 'Mini Player · 0.1 · Оформление')} 'classic theme saved')
 OpenFromPlayer
 $restored=WaitEditor 'Кофе'
 if($restored.Current.ProcessId -ne $hubPid){throw 'The handoff started a second launcher'}
 Write-Output 'PASS The actual widget button restores the same launcher from tray on the current classic theme'
 if([IO.File]::Exists($active) -or [IO.File]::ReadAllText($library) -cne $fixture){throw 'Opening the workshop changed the player or the saved preset.'}
 Write-Output 'PASS Opening the workshop leaves the active selection and library untouched'
}finally{
 Signal ($hubEvent+'.Stop')
 Signal ($playerEvent+'.Stop')
 if($player){try{if(!$player.WaitForExit(8000)){throw 'Test player did not close'}}finally{$player.Dispose()}}
 [void](WaitFor {
  $mutex=$null
  if(![Threading.Mutex]::TryOpenExisting($hubEvent,[ref]$mutex)){return $true}
  $mutex.Dispose()
 } 'test launcher shutdown')
 foreach($entry in $original.GetEnumerator()){
  if($null -eq $entry.Value){[IO.File]::Delete($entry.Key)}
  else{[IO.File]::WriteAllBytes($entry.Key,$entry.Value)}
 }
}
