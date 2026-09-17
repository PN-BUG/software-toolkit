Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$validateOnly = $args -contains '-ValidateOnly'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
 Title="定时任务管理器" Width="900" Height="650" MinWidth="760" MinHeight="560" WindowStartupLocation="CenterScreen" FontFamily="Microsoft YaHei UI" Background="#F4F6FA">
 <Window.Resources>
  <Style TargetType="TextBox"><Setter Property="Padding" Value="8,6"/><Setter Property="Margin" Value="0,4,0,10"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Padding" Value="6"/><Setter Property="Margin" Value="0,4,0,10"/></Style>
  <Style TargetType="Button"><Setter Property="Padding" Value="14,7"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Cursor" Value="Hand"/></Style>
  <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#273043"/></Style>
 </Window.Resources>
 <Grid Margin="20"><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
  <StackPanel Grid.Row="0" Margin="0,0,0,14"><TextBlock Text="定时任务管理器" FontSize="24" FontWeight="SemiBold"/><TextBlock Text="任务由 Windows 任务计划程序执行，关闭本工具后仍然有效。" Margin="0,4,0,0" Foreground="#667085"/></StackPanel>
  <Border Grid.Row="1" Background="White" CornerRadius="8" Padding="16" Margin="0,0,0,14"><Grid>
   <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="18"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
   <StackPanel Grid.Column="0">
    <TextBlock Text="使用预设任务" FontWeight="SemiBold"/><ComboBox x:Name="PresetBox" DisplayMemberPath="Name" ToolTip="从已安装工具提供的任务预设中选择"/><TextBlock x:Name="PresetHintText" Text="选择预设可自动填写任务名称、脚本、参数和执行计划。" Foreground="#667085" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,10"/>
    <StackPanel x:Name="ConfigPanel" Visibility="Collapsed" Margin="0,0,0,8"><TextBlock Text="配置文件" FontWeight="SemiBold"/><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBox x:Name="ConfigPathBox" Margin="0,4,8,4" ToolTip="选择该预设运行时使用的配置文件"/><Button x:Name="BrowseConfigButton" Grid.Column="1" Content="浏览…" Margin="0,4,0,4"/></Grid><TextBlock x:Name="ConfigHintText" Foreground="#667085" FontSize="11" TextWrapping="Wrap"/></StackPanel>
    <TextBlock Text="任务名称" FontWeight="SemiBold"/><TextBox x:Name="NameBox" ToolTip="例如：每天备份"/>
    <TextBlock Text="程序或脚本" FontWeight="SemiBold"/><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBox x:Name="ProgramBox" Margin="0,4,8,10" ToolTip="exe、bat、cmd、ps1 或 py 文件"/><Button x:Name="BrowseButton" Grid.Column="1" Content="浏览…" Margin="0,4,0,10"/></Grid>
    <StackPanel x:Name="ArgumentsPanel"><TextBlock Text="参数（可选）" FontWeight="SemiBold"/><TextBox x:Name="ArgumentsBox"/></StackPanel>
   </StackPanel>
   <StackPanel Grid.Column="2">
    <TextBlock Text="执行计划" FontWeight="SemiBold"/><ComboBox x:Name="ScheduleBox" SelectedIndex="0"><ComboBoxItem Content="开机时" Tag="Startup"/><ComboBoxItem Content="用户登录时" Tag="Logon"/><ComboBoxItem Content="每天固定时间" Tag="Daily"/><ComboBoxItem Content="每隔若干分钟" Tag="Interval"/></ComboBox>
    <StackPanel x:Name="DailyPanel" Visibility="Collapsed"><TextBlock Text="每天执行时间（HH:mm）" FontWeight="SemiBold"/><TextBox x:Name="TimeBox" Text="09:00"/></StackPanel>
    <StackPanel x:Name="IntervalPanel" Visibility="Collapsed"><TextBlock Text="间隔分钟数（1–10080）" FontWeight="SemiBold"/><TextBox x:Name="IntervalBox" Text="60"/></StackPanel>
    <CheckBox x:Name="RunNowCheck" Content="创建后立即运行一次" Margin="0,6,0,12"/><Button x:Name="CreateButton" Content="创建任务" Background="#2563EB" Foreground="White" BorderThickness="0" HorizontalAlignment="Left"/>
   </StackPanel>
  </Grid></Border>
  <Border Grid.Row="2" Background="White" CornerRadius="8" Padding="16"><Grid>
   <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
   <DockPanel Margin="0,0,0,10"><Button x:Name="RefreshButton" Content="刷新" DockPanel.Dock="Right" HorizontalAlignment="Right" Margin="8,0,0,0"/><TextBlock Text="SoftwareToolkit 任务" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center"/></DockPanel>
   <DataGrid x:Name="TaskGrid" Grid.Row="1" AutoGenerateColumns="False" IsReadOnly="True" SelectionMode="Single" CanUserAddRows="False" HeadersVisibility="Column" GridLinesVisibility="Horizontal"><DataGrid.Columns><DataGridTextColumn Header="名称" Binding="{Binding Name}" Width="1.5*"/><DataGridTextColumn Header="说明" Binding="{Binding Description}" Width="2*"/><DataGridTextColumn Header="状态" Binding="{Binding State}" Width="*"/><DataGridTextColumn Header="下次运行" Binding="{Binding NextRun}" Width="1.4*"/><DataGridTextColumn Header="上次结果" Binding="{Binding LastResult}" Width="*"/></DataGrid.Columns></DataGrid>
   <DockPanel Grid.Row="2" Margin="0,12,0,0"><StackPanel DockPanel.Dock="Left" Orientation="Horizontal"><Button x:Name="RunButton" Content="立即运行"/><Button x:Name="ToggleButton" Content="启用 / 禁用"/><Button x:Name="LogButton" Content="查看日志" IsEnabled="False"/><Button x:Name="DeleteButton" Content="删除" Foreground="#B42318"/></StackPanel><TextBlock x:Name="TaskLogText" Text="选择任务后可查看运行日志" Foreground="#667085" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" Margin="12,0,0,0"/></DockPanel>
  </Grid></Border>
  <TextBlock x:Name="StatusText" Grid.Row="3" Text="就绪" Foreground="#667085" Margin="2,10,0,0"/>
 </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)
$taskPath = '\SoftwareToolkit\'
function Find-Control([string]$name) { $window.FindName($name) }
$presetBox = Find-Control 'PresetBox'; $presetHintText = Find-Control 'PresetHintText'; $configPanel = Find-Control 'ConfigPanel'; $configPathBox = Find-Control 'ConfigPathBox'; $browseConfigButton = Find-Control 'BrowseConfigButton'; $configHintText = Find-Control 'ConfigHintText'; $nameBox = Find-Control 'NameBox'; $programBox = Find-Control 'ProgramBox'; $argumentsPanel = Find-Control 'ArgumentsPanel'; $argumentsBox = Find-Control 'ArgumentsBox'
$scheduleBox = Find-Control 'ScheduleBox'; $dailyPanel = Find-Control 'DailyPanel'; $intervalPanel = Find-Control 'IntervalPanel'
$timeBox = Find-Control 'TimeBox'; $intervalBox = Find-Control 'IntervalBox'; $runNowCheck = Find-Control 'RunNowCheck'
$taskGrid = Find-Control 'TaskGrid'; $logButton = Find-Control 'LogButton'; $taskLogText = Find-Control 'TaskLogText'; $statusText = Find-Control 'StatusText'
$script:presets = @()

function Set-Status([string]$message, [bool]$isError = $false) { $statusText.Text = $message; $statusText.Foreground = if ($isError) { '#B42318' } else { '#667085' } }
function Show-Error([string]$message) { Set-Status $message $true; [System.Windows.MessageBox]::Show($window, $message, '定时任务管理器', 'OK', 'Error') | Out-Null }
function Get-SelectedSchedule { [string]$scheduleBox.SelectedItem.Tag }
function Get-SelectedTask { $taskGrid.SelectedItem }
function Get-ObjectValue($object,[string]$name,$default='') { $property=$object.PSObject.Properties[$name];if($property){$property.Value}else{$default} }
function Load-Presets {
 $presets=@([pscustomobject]@{Name='自定义任务';IsCustom=$true;Description='手动填写任务信息';TaskName='';Program='';Arguments='';ArgumentsTemplate='';ConfigPath='';ConfigFilter='';Schedule='Startup';DailyTime='09:00';IntervalMinutes='60';RequiredPath='';LogPath=''})
 $toolsRoot=Split-Path -Parent $PSScriptRoot
 foreach($manifestPath in Get-ChildItem -LiteralPath $toolsRoot -Filter manifest.json -Recurse -File -ErrorAction SilentlyContinue){
  try{
   $manifest=Get-Content -Raw -LiteralPath $manifestPath.FullName -Encoding UTF8|ConvertFrom-Json
   if(-not$manifest.PSObject.Properties['schedulePresets']){continue}
   foreach($preset in @($manifest.schedulePresets)){
    $program=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'program' ''))
    if($program -and -not[IO.Path]::IsPathRooted($program)){$program=[IO.Path]::GetFullPath((Join-Path $manifestPath.DirectoryName $program))}
    $requiredPath=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'requiredPath' ''))
    $presets += [pscustomobject]@{
     Name=[string](Get-ObjectValue $preset 'name' '未命名预设');IsCustom=$false
     Description=[string](Get-ObjectValue $preset 'description' '')
     TaskName=[string](Get-ObjectValue $preset 'taskName' '')
     Program=$program
     Arguments=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'arguments' ''))
     ArgumentsTemplate=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'argumentsTemplate' ''))
     ConfigPath=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'configPath' $requiredPath))
     ConfigFilter=[string](Get-ObjectValue $preset 'configFilter' 'JSON 配置 (*.json)|*.json|所有文件|*.*')
     Schedule=[string](Get-ObjectValue $preset 'schedule' 'Daily')
     DailyTime=[string](Get-ObjectValue $preset 'dailyTime' '09:00')
     IntervalMinutes=[string](Get-ObjectValue $preset 'intervalMinutes' '60')
     RequiredPath=$requiredPath
     LogPath=[Environment]::ExpandEnvironmentVariables([string](Get-ObjectValue $preset 'logPath' ''))
    }
   }
  }catch{[Diagnostics.Debug]::WriteLine("读取任务预设失败：$($manifestPath.FullName) - $($_.Exception.Message)")}
 }
 $script:presets=$presets;$presetBox.ItemsSource=$presets;$presetBox.SelectedIndex=0
}
function Update-PresetArguments {
 $preset=$presetBox.SelectedItem
 if(-not$preset -or $preset.IsCustom -or -not$preset.ConfigPath){return}
 $configPath=$configPathBox.Text.Trim()
 if($preset.ArgumentsTemplate){$argumentsBox.Text=$preset.ArgumentsTemplate.Replace('{configPath}',$configPath)}
 if([string]::IsNullOrWhiteSpace($configPath)){$configHintText.Text='请选择配置文件。';$configHintText.Foreground='#B42318';return}
 if(-not(Test-Path -LiteralPath $configPath -PathType Leaf)){$configHintText.Text='文件不存在，请点击“浏览…”选择配置。';$configHintText.Foreground='#B42318';return}
 try{
  $config=Get-Content -Raw -LiteralPath $configPath -Encoding UTF8|ConvertFrom-Json
  $targets=Get-ObjectValue $config 'targets' $null
  if(-not(Get-ObjectValue $config 'email' '') -or -not$targets -or @($targets).Count -eq 0){throw '缺少邮箱或项目列表'}
  if(Get-ObjectValue $config 'passwordProtected' ''){$configHintText.Text='已选择 SoftwareToolkit 的 DPAPI 加密配置。';$configHintText.Foreground='#067647'}
  elseif(Get-ObjectValue $config 'password' ''){$configHintText.Text='已选择原版明文配置；可以运行，建议之后在保活工具中导入并保存为加密配置。';$configHintText.Foreground='#B54708'}
  else{throw '缺少密码或加密密码'}
 }catch{$configHintText.Text="配置不可用：$($_.Exception.Message)";$configHintText.Foreground='#B42318'}
}
function Apply-Preset($preset){
 if(-not$preset){return}
 if($preset.IsCustom){$configPanel.Visibility='Collapsed';$argumentsPanel.Visibility='Visible';$presetHintText.Text='手动填写任务名称、程序、参数和执行计划。';return}
 $nameBox.Text=$preset.TaskName;$programBox.Text=$preset.Program;$argumentsBox.Text=$preset.Arguments
 for($index=0;$index -lt $scheduleBox.Items.Count;$index++){if([string]$scheduleBox.Items[$index].Tag -eq $preset.Schedule){$scheduleBox.SelectedIndex=$index;break}}
 $timeBox.Text=$preset.DailyTime;$intervalBox.Text=$preset.IntervalMinutes;$presetHintText.Text=$preset.Description
 if($preset.ConfigPath){$configPanel.Visibility='Visible';$argumentsPanel.Visibility='Collapsed';$configPathBox.Text=$preset.ConfigPath;Update-PresetArguments}else{$configPanel.Visibility='Collapsed';$argumentsPanel.Visibility='Visible'}
 if($preset.RequiredPath -and -not(Test-Path -LiteralPath $preset.RequiredPath -PathType Leaf)){Set-Status "预设已载入；请选择要使用的配置文件：$($preset.Name)" $true}else{Set-Status "已载入预设：$($preset.Name)"}
}
function Get-PresetForTask($task){
 if(-not$task){return $null}
 $actionText=(@($task.Actions)|ForEach-Object{"$($_.Execute) $($_.Arguments)"}) -join ' '
 foreach($preset in @($script:presets|Where-Object{-not$_.IsCustom})){
  if([string]::Equals([string]$task.TaskName,[string]$preset.TaskName,[StringComparison]::OrdinalIgnoreCase)){return $preset}
  if($preset.Program -and $actionText.IndexOf([string]$preset.Program,[StringComparison]::OrdinalIgnoreCase) -ge 0){return $preset}
 }
 return $null
}
function Update-TaskLog {
 $selected=Get-SelectedTask
 if(-not$selected){$logButton.IsEnabled=$false;$taskLogText.Text='选择任务后可查看运行日志';return}
 $logPath=[string]$selected.LogPath
 if(-not$logPath){$logButton.IsEnabled=$false;$taskLogText.Text='该任务未提供日志路径';return}
 $exists=Test-Path -LiteralPath $logPath -PathType Leaf;$logButton.IsEnabled=$exists
 $taskLogText.Text=if($exists){Get-Content -LiteralPath $logPath -Tail 1 -Encoding UTF8}else{"尚无日志：$logPath"}
}
function Refresh-Tasks {
 try {
  $selectedName=if($taskGrid.SelectedItem){[string]$taskGrid.SelectedItem.Name}else{$null}
  $items = @(Get-ScheduledTask -TaskPath $taskPath -ErrorAction SilentlyContinue | Sort-Object TaskName | ForEach-Object {
   $info = $_ | Get-ScheduledTaskInfo
   $stateText = switch ([string]$_.State) { 'Ready' {'已启用'} 'Running' {'运行中'} 'Disabled' {'已禁用'} default {[string]$_.State} }
   $preset=Get-PresetForTask $_
   [pscustomobject]@{ Name=$_.TaskName; Description=$_.Description; State=$stateText; NextRun=$(if ($info.NextRunTime -and $info.NextRunTime.Year -gt 1900) {$info.NextRunTime.ToString('yyyy-MM-dd HH:mm')} else {'-'}); LastResult=$(if ($info.LastTaskResult -eq 0) {'成功 (0)'} else {[string]$info.LastTaskResult}); LogPath=$(if($preset){$preset.LogPath}else{''}) }
  })
  $taskGrid.ItemsSource=$items
  $selected=$items|Where-Object{$_.Name -eq $selectedName}|Select-Object -First 1
  if(-not$selected -and $items.Count -gt 0){$selected=$items[0]}
  $taskGrid.SelectedItem=$selected
  Update-TaskLog
  Set-Status "共 $($items.Count) 个 SoftwareToolkit 定时任务"
 } catch { Show-Error "读取任务失败：$($_.Exception.Message)" }
}

$scheduleBox.Add_SelectionChanged({ $kind=Get-SelectedSchedule; $dailyPanel.Visibility=$(if($kind -eq 'Daily'){'Visible'}else{'Collapsed'}); $intervalPanel.Visibility=$(if($kind -eq 'Interval'){'Visible'}else{'Collapsed'}) })
$presetBox.Add_SelectionChanged({Apply-Preset $presetBox.SelectedItem})
$configPathBox.Add_TextChanged({Update-PresetArguments})
$taskGrid.Add_SelectionChanged({Update-TaskLog})
(Find-Control 'BrowseButton').Add_Click({ $dialog=New-Object Microsoft.Win32.OpenFileDialog; $dialog.Filter='可执行或脚本|*.exe;*.bat;*.cmd;*.ps1;*.py|所有文件|*.*'; if($dialog.ShowDialog($window)){$programBox.Text=$dialog.FileName} })
$browseConfigButton.Add_Click({
 $dialog=New-Object Microsoft.Win32.OpenFileDialog
 $preset=$presetBox.SelectedItem;$dialog.Filter=if($preset -and $preset.ConfigFilter){$preset.ConfigFilter}else{'JSON 配置 (*.json)|*.json|所有文件|*.*'}
 $currentPath=$configPathBox.Text.Trim();if(Test-Path -LiteralPath $currentPath -PathType Leaf){$dialog.InitialDirectory=Split-Path -Parent $currentPath;$dialog.FileName=Split-Path -Leaf $currentPath}
 if($dialog.ShowDialog($window)){$configPathBox.Text=$dialog.FileName;Update-PresetArguments}
})
(Find-Control 'CreateButton').Add_Click({
 try {
  $selectedPreset=$presetBox.SelectedItem
  if($selectedPreset -and -not$selectedPreset.IsCustom -and $selectedPreset.ConfigPath){
   $selectedConfig=$configPathBox.Text.Trim()
   if([string]::IsNullOrWhiteSpace($selectedConfig) -or -not(Test-Path -LiteralPath $selectedConfig -PathType Leaf)){throw '请选择存在的配置文件。'}
   try{$configCheck=Get-Content -Raw -LiteralPath $selectedConfig -Encoding UTF8|ConvertFrom-Json}catch{throw "配置文件不是有效的 JSON：$($_.Exception.Message)"}
   $configTargets=Get-ObjectValue $configCheck 'targets' $null
   if(-not(Get-ObjectValue $configCheck 'email' '') -or -not$configTargets -or @($configTargets).Count -eq 0 -or (-not(Get-ObjectValue $configCheck 'passwordProtected' '') -and -not(Get-ObjectValue $configCheck 'password' ''))){throw '配置文件缺少邮箱、密码或项目列表。'}
   Update-PresetArguments
  }
  $taskName=$nameBox.Text.Trim(); $sourceProgram=[Environment]::ExpandEnvironmentVariables($programBox.Text.Trim()); $program=$sourceProgram
  if([string]::IsNullOrWhiteSpace($taskName)){throw '请输入任务名称。'}
  if($taskName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $taskName.Contains('\')){throw '任务名称不能包含文件名非法字符或反斜杠。'}
  if([string]::IsNullOrWhiteSpace($program)){throw '请选择要执行的程序或脚本。'}
  if(-not(Test-Path -LiteralPath $program -PathType Leaf) -and -not(Get-Command $program -ErrorAction SilentlyContinue)){throw "找不到程序或脚本：$program"}
  $extension=[IO.Path]::GetExtension($program).ToLowerInvariant(); $actionArgs=$argumentsBox.Text.Trim()
  if($extension -eq '.ps1'){$actionArgs="-NoProfile -ExecutionPolicy Bypass -File `"$program`" $actionArgs".Trim(); $program="$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"}
  elseif($extension -in @('.bat','.cmd')){$actionArgs="/d /c `"`"$program`" $actionArgs`"".Trim(); $program="$env:SystemRoot\System32\cmd.exe"}
  $workingDir=Split-Path -Parent $sourceProgram; $action=New-ScheduledTaskAction -Execute $program -Argument $actionArgs -WorkingDirectory $workingDir; $kind=Get-SelectedSchedule
  switch($kind){
   'Startup'{$trigger=New-ScheduledTaskTrigger -AtStartup}
   'Logon'{$trigger=New-ScheduledTaskTrigger -AtLogOn -User ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)}
   'Daily'{$time=[datetime]::MinValue; if(-not [datetime]::TryParseExact($timeBox.Text.Trim(),'HH:mm',$null,'None',[ref]$time)){throw '时间格式应为 HH:mm，例如 09:30。'}; $trigger=New-ScheduledTaskTrigger -Daily -At $time}
   'Interval'{$minutes=0; if(-not [int]::TryParse($intervalBox.Text.Trim(),[ref]$minutes) -or $minutes -lt 1 -or $minutes -gt 10080){throw '间隔分钟数必须是 1 到 10080 的整数。'}; $trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $minutes) -RepetitionDuration (New-TimeSpan -Days 3650)}
  }
  $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
  if($kind -eq 'Startup'){$principal=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest}else{$principal=New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest}
  $description=if($selectedPreset -and -not$selectedPreset.IsCustom){$selectedPreset.Description}else{'由 SoftwareToolkit 定时任务管理器创建'}
  $task=New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description $description
  Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -InputObject $task -Force | Out-Null
  if($runNowCheck.IsChecked){Start-ScheduledTask -TaskName $taskName -TaskPath $taskPath}
  Set-Status "已创建任务：$taskName"; Refresh-Tasks
 } catch { Show-Error "创建失败：$($_.Exception.Message)" }
})
(Find-Control 'RefreshButton').Add_Click({Refresh-Tasks})
(Find-Control 'RunButton').Add_Click({$selected=Get-SelectedTask;if(-not$selected){Set-Status '请先在任务表格中选择一个任务。' $true;return};try{Start-ScheduledTask -TaskName $selected.Name -TaskPath $taskPath;Set-Status "已启动：$($selected.Name)"}catch{Show-Error "运行失败：$($_.Exception.Message)"}})
(Find-Control 'ToggleButton').Add_Click({$selected=Get-SelectedTask;if(-not$selected){Set-Status '请先在任务表格中选择一个任务。' $true;return};try{$name=$selected.Name;$task=Get-ScheduledTask -TaskName $name -TaskPath $taskPath;if($task.Settings.Enabled){Disable-ScheduledTask -InputObject $task|Out-Null;Set-Status "已禁用：$name"}else{Enable-ScheduledTask -InputObject $task|Out-Null;Set-Status "已启用：$name"};Refresh-Tasks}catch{Show-Error "操作失败：$($_.Exception.Message)"}})
(Find-Control 'LogButton').Add_Click({$selected=Get-SelectedTask;if(-not$selected -or -not$selected.LogPath){Set-Status '所选任务没有可用日志。' $true;return};try{if(-not(Test-Path -LiteralPath $selected.LogPath -PathType Leaf)){throw "日志文件不存在：$($selected.LogPath)"};Start-Process notepad.exe -ArgumentList "`"$($selected.LogPath)`"";Set-Status "已打开日志：$($selected.Name)"}catch{Show-Error "打开日志失败：$($_.Exception.Message)"}})
(Find-Control 'DeleteButton').Add_Click({$selected=Get-SelectedTask;if(-not$selected){Set-Status '请先在任务表格中选择一个任务。' $true;return};$name=$selected.Name;$question='确定删除任务“{0}”吗？' -f $name;if([System.Windows.MessageBox]::Show($window,$question,'确认删除','YesNo','Warning') -ne 'Yes'){return};try{Unregister-ScheduledTask -TaskName $name -TaskPath $taskPath -Confirm:$false;Set-Status "已删除：$name";Refresh-Tasks}catch{Show-Error "删除失败：$($_.Exception.Message)"}})
Load-Presets
if($validateOnly){
 $supabasePreset=@($presetBox.ItemsSource|Where-Object{$_.Name -eq 'Supabase 保活'})
 if($supabasePreset.Count -eq 1){
  $presetBox.SelectedItem=$supabasePreset[0]
  if($nameBox.Text -ne 'Supabase KeepAlive' -or -not$programBox.Text.EndsWith('SupabaseKeepAliveWorker.ps1') -or -not(Test-Path -LiteralPath $programBox.Text -PathType Leaf) -or (Get-SelectedSchedule) -ne 'Daily' -or -not$supabasePreset[0].LogPath.EndsWith('worker.log') -or $configPanel.Visibility -ne 'Visible' -or $argumentsPanel.Visibility -ne 'Collapsed' -or -not$configPathBox.Text.EndsWith('config.json') -or $argumentsBox.Text.IndexOf($configPathBox.Text,[StringComparison]::OrdinalIgnoreCase) -lt 0){throw 'Supabase 保活任务预设未正确应用。'}
 }
 if(@($presetBox.ItemsSource|Where-Object{$_.IsCustom}).Count -ne 1){throw '独立自定义任务预设不可用。'}
 $window.Close();exit 0
}
Refresh-Tasks
$window.ShowDialog() | Out-Null
