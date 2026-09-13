Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
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
    <TextBlock Text="任务名称" FontWeight="SemiBold"/><TextBox x:Name="NameBox" ToolTip="例如：每天备份"/>
    <TextBlock Text="程序或脚本" FontWeight="SemiBold"/><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBox x:Name="ProgramBox" Margin="0,4,8,10" ToolTip="exe、bat、cmd、ps1 或 py 文件"/><Button x:Name="BrowseButton" Grid.Column="1" Content="浏览…" Margin="0,4,0,10"/></Grid>
    <TextBlock Text="参数（可选）" FontWeight="SemiBold"/><TextBox x:Name="ArgumentsBox"/>
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
   <DockPanel Margin="0,0,0,10"><TextBlock Text="已创建的任务" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center"/><Button x:Name="RefreshButton" Content="刷新" DockPanel.Dock="Right" HorizontalAlignment="Right" Margin="0"/></DockPanel>
   <DataGrid x:Name="TaskGrid" Grid.Row="1" AutoGenerateColumns="False" IsReadOnly="True" SelectionMode="Single" CanUserAddRows="False" HeadersVisibility="Column" GridLinesVisibility="Horizontal"><DataGrid.Columns><DataGridTextColumn Header="名称" Binding="{Binding Name}" Width="2*"/><DataGridTextColumn Header="状态" Binding="{Binding State}" Width="*"/><DataGridTextColumn Header="下次运行" Binding="{Binding NextRun}" Width="1.5*"/><DataGridTextColumn Header="上次结果" Binding="{Binding LastResult}" Width="*"/></DataGrid.Columns></DataGrid>
   <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,12,0,0"><Button x:Name="RunButton" Content="立即运行"/><Button x:Name="ToggleButton" Content="启用 / 禁用"/><Button x:Name="DeleteButton" Content="删除" Foreground="#B42318"/></StackPanel>
  </Grid></Border>
  <TextBlock x:Name="StatusText" Grid.Row="3" Text="就绪" Foreground="#667085" Margin="2,10,0,0"/>
 </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [Windows.Markup.XamlReader]::Load($reader)
$taskPath = '\SoftwareToolkit\'
function Find-Control([string]$name) { $window.FindName($name) }
$nameBox = Find-Control 'NameBox'; $programBox = Find-Control 'ProgramBox'; $argumentsBox = Find-Control 'ArgumentsBox'
$scheduleBox = Find-Control 'ScheduleBox'; $dailyPanel = Find-Control 'DailyPanel'; $intervalPanel = Find-Control 'IntervalPanel'
$timeBox = Find-Control 'TimeBox'; $intervalBox = Find-Control 'IntervalBox'; $runNowCheck = Find-Control 'RunNowCheck'
$taskGrid = Find-Control 'TaskGrid'; $statusText = Find-Control 'StatusText'

function Set-Status([string]$message, [bool]$isError = $false) { $statusText.Text = $message; $statusText.Foreground = if ($isError) { '#B42318' } else { '#667085' } }
function Show-Error([string]$message) { Set-Status $message $true; [System.Windows.MessageBox]::Show($window, $message, '定时任务管理器', 'OK', 'Error') | Out-Null }
function Get-SelectedSchedule { [string]$scheduleBox.SelectedItem.Tag }
function Refresh-Tasks {
 try {
  $items = @(Get-ScheduledTask -TaskPath $taskPath -ErrorAction SilentlyContinue | Sort-Object TaskName | ForEach-Object {
   $info = $_ | Get-ScheduledTaskInfo
   $stateText = switch ([string]$_.State) { 'Ready' {'已启用'} 'Running' {'运行中'} 'Disabled' {'已禁用'} default {[string]$_.State} }
   [pscustomobject]@{ Name=$_.TaskName; State=$stateText; NextRun=$(if ($info.NextRunTime -and $info.NextRunTime.Year -gt 1900) {$info.NextRunTime.ToString('yyyy-MM-dd HH:mm')} else {'-'}); LastResult=$(if ($info.LastTaskResult -eq 0) {'成功 (0)'} else {[string]$info.LastTaskResult}) }
  })
  $taskGrid.ItemsSource = $items; Set-Status "共 $($items.Count) 个 SoftwareToolkit 定时任务"
 } catch { Show-Error "读取任务失败：$($_.Exception.Message)" }
}

$scheduleBox.Add_SelectionChanged({ $kind=Get-SelectedSchedule; $dailyPanel.Visibility=$(if($kind -eq 'Daily'){'Visible'}else{'Collapsed'}); $intervalPanel.Visibility=$(if($kind -eq 'Interval'){'Visible'}else{'Collapsed'}) })
(Find-Control 'BrowseButton').Add_Click({ $dialog=New-Object Microsoft.Win32.OpenFileDialog; $dialog.Filter='可执行或脚本|*.exe;*.bat;*.cmd;*.ps1;*.py|所有文件|*.*'; if($dialog.ShowDialog($window)){$programBox.Text=$dialog.FileName} })
(Find-Control 'CreateButton').Add_Click({
 try {
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
  $task=New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description '由 SoftwareToolkit 定时任务管理器创建'
  Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -InputObject $task -Force | Out-Null
  if($runNowCheck.IsChecked){Start-ScheduledTask -TaskName $taskName -TaskPath $taskPath}
  Set-Status "已创建任务：$taskName"; Refresh-Tasks
 } catch { Show-Error "创建失败：$($_.Exception.Message)" }
})
(Find-Control 'RefreshButton').Add_Click({Refresh-Tasks})
(Find-Control 'RunButton').Add_Click({if(-not$taskGrid.SelectedItem){Set-Status '请先选择一个任务。' $true;return};try{Start-ScheduledTask -TaskName $taskGrid.SelectedItem.Name -TaskPath $taskPath;Set-Status "已启动：$($taskGrid.SelectedItem.Name)"}catch{Show-Error "运行失败：$($_.Exception.Message)"}})
(Find-Control 'ToggleButton').Add_Click({if(-not$taskGrid.SelectedItem){Set-Status '请先选择一个任务。' $true;return};try{$name=$taskGrid.SelectedItem.Name;$task=Get-ScheduledTask -TaskName $name -TaskPath $taskPath;if($task.Settings.Enabled){Disable-ScheduledTask -InputObject $task|Out-Null;Set-Status "已禁用：$name"}else{Enable-ScheduledTask -InputObject $task|Out-Null;Set-Status "已启用：$name"};Refresh-Tasks}catch{Show-Error "操作失败：$($_.Exception.Message)"}})
(Find-Control 'DeleteButton').Add_Click({if(-not$taskGrid.SelectedItem){Set-Status '请先选择一个任务。' $true;return};$name=$taskGrid.SelectedItem.Name;$question='确定删除任务“{0}”吗？' -f $name;if([System.Windows.MessageBox]::Show($window,$question,'确认删除','YesNo','Warning') -ne 'Yes'){return};try{Unregister-ScheduledTask -TaskName $name -TaskPath $taskPath -Confirm:$false;Set-Status "已删除：$name";Refresh-Tasks}catch{Show-Error "删除失败：$($_.Exception.Message)"}})
Refresh-Tasks
$window.ShowDialog() | Out-Null
