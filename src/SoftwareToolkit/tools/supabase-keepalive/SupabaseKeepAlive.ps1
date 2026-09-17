param([switch]$ValidateOnly)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -AssemblyName System.Windows.Forms

$appDir = Join-Path $env:LOCALAPPDATA 'SoftwareToolkit\SupabaseKeepAlive'
$configPath = Join-Path $appDir 'config.json'
$workerSource = Join-Path $PSScriptRoot 'SupabaseKeepAliveWorker.ps1'
$logPath = Join-Path $appDir 'worker.log'
$taskName = 'Supabase KeepAlive'
$taskPath = '\SoftwareToolkit\'
$legacyTaskName = 'SoftwareToolkit Supabase KeepAlive'
$taskManagerPath = Join-Path $PSScriptRoot '..\task-scheduler\task-scheduler.ps1'
$script:runningProcess = $null

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
 Title="Supabase 保活" Width="980" Height="760" MinWidth="820" MinHeight="650" WindowStartupLocation="CenterScreen"
 Background="#F6F9F8" Foreground="#243B39" FontFamily="Microsoft YaHei UI" FontSize="13">
 <Window.Resources>
  <Style TargetType="TextBox"><Setter Property="Height" Value="34"/><Setter Property="Padding" Value="9,5"/><Setter Property="BorderBrush" Value="#CBDAD4"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
  <Style TargetType="PasswordBox"><Setter Property="Height" Value="34"/><Setter Property="Padding" Value="9,5"/><Setter Property="BorderBrush" Value="#CBDAD4"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Height" Value="34"/><Setter Property="Padding" Value="7,4"/><Setter Property="BorderBrush" Value="#CBDAD4"/></Style>
  <Style TargetType="Button"><Setter Property="Height" Value="34"/><Setter Property="Padding" Value="14,0"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#CBDAD4"/><Setter Property="Cursor" Value="Hand"/></Style>
  <Style x:Key="PrimaryButton" TargetType="Button"><Setter Property="Height" Value="36"/><Setter Property="Padding" Value="18,0"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Background" Value="#167D6B"/><Setter Property="Foreground" Value="White"/><Setter Property="BorderBrush" Value="#167D6B"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="Cursor" Value="Hand"/></Style>
  <Style x:Key="Label" TargetType="TextBlock"><Setter Property="Foreground" Value="#60776E"/><Setter Property="Margin" Value="0,0,0,6"/></Style>
  <Style x:Key="Panel" TargetType="Border"><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#DCE7E2"/><Setter Property="BorderThickness" Value="1"/><Setter Property="CornerRadius" Value="10"/><Setter Property="Padding" Value="18"/></Style>
 </Window.Resources>
 <Grid Margin="22">
  <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
  <Grid>
   <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
   <StackPanel><TextBlock Text="Supabase KeepAlive" FontSize="25" FontWeight="SemiBold"/><TextBlock Text="多项目保活 · 本机加密配置 · Windows 计划任务" Foreground="#72867E" Margin="0,6,0,0"/></StackPanel>
   <Border Grid.Column="1" Background="#E6F5EF" CornerRadius="16" Padding="13,7" VerticalAlignment="Center"><TextBlock x:Name="TaskStateText" Text="读取状态…" Foreground="#167D6B" FontWeight="SemiBold"/></Border>
  </Grid>

  <Border Grid.Row="1" Style="{StaticResource Panel}" Margin="0,18,0,12">
   <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width="1.2*"/><ColumnDefinition Width="1.2*"/><ColumnDefinition Width="0.8*"/><ColumnDefinition Width="0.65*"/></Grid.ColumnDefinitions>
    <StackPanel Margin="0,0,12,0"><TextBlock Text="专用用户邮箱" Style="{StaticResource Label}"/><TextBox x:Name="EmailBox"/></StackPanel>
    <StackPanel Grid.Column="1" Margin="0,0,12,0"><TextBlock Text="用户密码（使用 DPAPI 加密）" Style="{StaticResource Label}"/><PasswordBox x:Name="PasswordBox"/></StackPanel>
    <StackPanel Grid.Column="2" Margin="0,0,12,0"><TextBlock Text="保活表名" Style="{StaticResource Label}"/><TextBox x:Name="TableBox" Text="test"/></StackPanel>
    <StackPanel Grid.Column="3"><TextBlock Text="超时（秒）" Style="{StaticResource Label}"/><TextBox x:Name="TimeoutBox" Text="12"/></StackPanel>
   </Grid>
  </Border>

  <Border Grid.Row="2" Style="{StaticResource Panel}">
   <Grid>
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <DockPanel Margin="0,0,0,10"><TextBlock Text="项目列表" FontSize="16" FontWeight="SemiBold" VerticalAlignment="Center"/><StackPanel DockPanel.Dock="Right" Orientation="Horizontal"><Button x:Name="ImportButton" Content="导入旧 JSON"/><Button x:Name="AddButton" Content="＋ 添加"/><Button x:Name="RemoveButton" Content="删除所选" Margin="0"/></StackPanel></DockPanel>
    <DataGrid x:Name="TargetGrid" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False" HeadersVisibility="Column" GridLinesVisibility="Horizontal" SelectionMode="Single" RowHeight="38">
     <DataGrid.Columns>
      <DataGridTextColumn Header="名称" Binding="{Binding Name, UpdateSourceTrigger=PropertyChanged}" Width="1*"/>
      <DataGridTextColumn Header="项目 URL（HTTPS）" Binding="{Binding Url, UpdateSourceTrigger=PropertyChanged}" Width="1.65*"/>
      <DataGridTextColumn Header="Publishable / anon key" Binding="{Binding ApiKey, UpdateSourceTrigger=PropertyChanged}" Width="2*"/>
     </DataGrid.Columns>
    </DataGrid>
    <TextBlock Grid.Row="2" Margin="0,10,0,0" Foreground="#778981" Text="仅使用 publishable/anon key；保活表应开启 RLS，并只允许该专用用户 INSERT。"/>
   </Grid>
  </Border>

  <Border Grid.Row="3" Style="{StaticResource Panel}" Margin="0,12,0,12">
   <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="170"/><ColumnDefinition Width="145"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
    <CheckBox x:Name="ScheduleEnabledCheck" Content="启用自动保活" VerticalAlignment="Center" Margin="0,0,20,0"/>
    <ComboBox x:Name="ScheduleModeBox" Grid.Column="1" Margin="0,0,12,0"><ComboBoxItem Content="每天固定时间" Tag="daily"/><ComboBoxItem Content="固定小时间隔" Tag="interval"/></ComboBox>
    <TextBox x:Name="ScheduleValueBox" Grid.Column="2" Margin="0,0,12,0"/>
    <TextBlock x:Name="ScheduleHintText" Grid.Column="3" VerticalAlignment="Center" Foreground="#778981"/>
   </Grid>
  </Border>

  <Grid Grid.Row="4">
   <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
   <StackPanel><TextBlock x:Name="StatusText" Text="就绪" Foreground="#60776E"/><TextBlock x:Name="LastLogText" Text="尚无运行日志" Foreground="#879990" Margin="0,5,0,0" TextTrimming="CharacterEllipsis" MaxWidth="570" HorizontalAlignment="Left"/></StackPanel>
   <StackPanel Grid.Column="1" Orientation="Horizontal"><Button x:Name="OpenLogButton" Content="日志"/><Button x:Name="TaskManagerButton" Content="任务管理器"/><Button x:Name="UninstallButton" Content="停用"/><Button x:Name="InstallButton" Content="应用计划"/><Button x:Name="RunButton" Content="立即执行" Style="{StaticResource PrimaryButton}" Margin="0"/></StackPanel>
  </Grid>
 </Grid>
</Window>
'@

$reader = [System.Xml.XmlNodeReader]::new($xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)
function Get-Control([string]$Name) { $window.FindName($Name) }

$emailBox = Get-Control 'EmailBox'
$passwordBox = Get-Control 'PasswordBox'
$tableBox = Get-Control 'TableBox'
$timeoutBox = Get-Control 'TimeoutBox'
$targetGrid = Get-Control 'TargetGrid'
$scheduleEnabled = Get-Control 'ScheduleEnabledCheck'
$scheduleMode = Get-Control 'ScheduleModeBox'
$scheduleValue = Get-Control 'ScheduleValueBox'
$scheduleHint = Get-Control 'ScheduleHintText'
$statusText = Get-Control 'StatusText'
$lastLogText = Get-Control 'LastLogText'
$taskStateText = Get-Control 'TaskStateText'
$targets = [Collections.ObjectModel.ObservableCollection[object]]::new()
$targetGrid.ItemsSource = $targets

function Set-Status([string]$Message, [bool]$Error = $false) {
    $statusText.Text = $Message
    $statusText.Foreground = if ($Error) { '#B42318' } else { '#60776E' }
}

function Show-Error([string]$Message) {
    Set-Status $Message $true
    [System.Windows.MessageBox]::Show($window, $Message, 'Supabase 保活', 'OK', 'Error') | Out-Null
}

function New-Target($Name = '', $Url = '', $ApiKey = '') {
    [pscustomobject]@{ Name = [string]$Name; Url = [string]$Url; ApiKey = [string]$ApiKey }
}

function Test-PrivilegedKey([string]$Key) {
    if ($Key -like 'sb_secret_*') { return $true }
    $parts = $Key.Split('.')
    if ($parts.Count -ne 3) { return $false }
    try {
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        switch ($payload.Length % 4) { 2 { $payload += '==' } 3 { $payload += '=' } }
        $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        return [string]$json.role -eq 'service_role'
    }
    catch { return $false }
}

function Get-ScheduleMode { if ($scheduleMode.SelectedItem) { [string]$scheduleMode.SelectedItem.Tag } else { 'daily' } }

function Update-ScheduleUi {
    $daily = (Get-ScheduleMode) -eq 'daily'
    $scheduleHint.Text = if ($daily) { '24 小时制，例如 09:00' } else { '间隔小时数，建议 24–72' }
    if ([string]::IsNullOrWhiteSpace($scheduleValue.Text)) { $scheduleValue.Text = if ($daily) { '09:00' } else { '48' } }
}

function Read-UiConfig {
    [void]$targetGrid.CommitEdit([Windows.Controls.DataGridEditingUnit]::Cell, $true)
    [void]$targetGrid.CommitEdit([Windows.Controls.DataGridEditingUnit]::Row, $true)
    $email = $emailBox.Text.Trim()
    if (-not $email -or $email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw '请输入有效的专用用户邮箱。' }
    if ([string]::IsNullOrWhiteSpace($passwordBox.Password)) { throw '请输入专用用户密码。' }
    $table = $tableBox.Text.Trim()
    if ($table -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw '表名只能包含字母、数字和下划线，且不能以数字开头。' }
    $timeout = 0
    if (-not [int]::TryParse($timeoutBox.Text.Trim(), [ref]$timeout) -or $timeout -lt 3 -or $timeout -gt 120) { throw '请求超时必须是 3–120 秒。' }
    if ($targets.Count -eq 0) { throw '请至少添加一个 Supabase 项目。' }
    $items = @()
    for ($i = 0; $i -lt $targets.Count; $i++) {
        $target = $targets[$i]
        $uri = $null
        if (-not [Uri]::TryCreate($target.Url.Trim(), [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') { throw "项目 $($i + 1) 的 URL 必须是有效的 HTTPS 地址。" }
        if ($uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw "项目 $($i + 1) 的 URL 不能包含用户信息、查询参数或片段。" }
        $key = $target.ApiKey.Trim()
        if (-not $key) { throw "项目 $($i + 1) 的 API Key 为空。" }
        if (Test-PrivilegedKey $key) { throw "项目 $($i + 1) 使用了 secret/service_role key，请改用 publishable/anon key。" }
        $items += [ordered]@{ name = $target.Name.Trim(); url = $uri.AbsoluteUri.TrimEnd('/'); apiKey = $key }
    }
    $mode = Get-ScheduleMode
    $scheduleValueText = $scheduleValue.Text.Trim()
    if ($mode -eq 'daily') {
        $time = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($scheduleValueText, 'HH:mm', $null, 'None', [ref]$time)) { throw '每天执行时间应为 HH:mm，例如 09:00。' }
    }
    else {
        $hours = 0
        if (-not [int]::TryParse($scheduleValueText, [ref]$hours) -or $hours -lt 1 -or $hours -gt 168) { throw '固定间隔必须是 1–168 小时。' }
    }
    [ordered]@{
        version = 2
        email = $email
        passwordProtected = ConvertFrom-SecureString (ConvertTo-SecureString $passwordBox.Password -AsPlainText -Force)
        tableName = $table
        timeoutSec = $timeout
        targetIntervalSeconds = 0.2
        schedule = [ordered]@{ enabled = [bool]$scheduleEnabled.IsChecked; mode = $mode; value = $scheduleValueText }
        targets = $items
    }
}

function Save-Config {
    $config = Read-UiConfig
    if (-not (Test-Path -LiteralPath $appDir)) { New-Item -ItemType Directory -Path $appDir -Force | Out-Null }
    $tempPath = Join-Path $appDir 'config.json.tmp'
    $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $configPath -Force
    return $config
}

function Apply-Config($Config) {
    $emailBox.Text = [string]$Config.email
    $tableBox.Text = if ($Config.tableName) { [string]$Config.tableName } elseif ($Config.keepAliveTableName) { [string]$Config.keepAliveTableName } else { 'test' }
    $timeoutBox.Text = if ($Config.timeoutSec) { [string]$Config.timeoutSec } else { '12' }
    if ($Config.passwordProtected) {
        try {
            $secure = ConvertTo-SecureString ([string]$Config.passwordProtected)
            $passwordBox.Password = ([PSCredential]::new('keepalive', $secure)).GetNetworkCredential().Password
        }
        catch { Set-Status '已读取配置，但加密密码无法解密，请重新输入。' $true }
    }
    elseif ($Config.password) { $passwordBox.Password = [string]$Config.password }
    $targets.Clear()
    foreach ($target in @($Config.targets)) {
        $targets.Add((New-Target $target.name $target.url $(if ($target.apiKey) { $target.apiKey } else { $target.apikey })))
    }
    if ($targets.Count -eq 0) { $targets.Add((New-Target)) }
    $schedule = $Config.schedule
    $scheduleEnabled.IsChecked = [bool]($schedule -and $schedule.enabled)
    $mode = if ($schedule -and $schedule.mode -eq 'interval') { 'interval' } else { 'daily' }
    $scheduleMode.SelectedIndex = if ($mode -eq 'interval') { 1 } else { 0 }
    $scheduleValue.Text = if ($schedule -and $schedule.value) { [string]$schedule.value } elseif ($mode -eq 'interval' -and $schedule.intervalHours) { [string]$schedule.intervalHours } elseif ($mode -eq 'daily' -and $schedule.dailyTime) { [string]$schedule.dailyTime } elseif ($mode -eq 'interval') { '48' } else { '09:00' }
    Update-ScheduleUi
}

function Refresh-Status {
    $task = Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction SilentlyContinue
    if ($task) {
        $info = $task | Get-ScheduledTaskInfo
        $next = if ($info.NextRunTime -and $info.NextRunTime.Year -gt 1900) { $info.NextRunTime.ToString('MM-dd HH:mm') } else { '未计划' }
        $taskStateText.Text = "任务管理器：$($task.State) · 下次 $next"
    }
    elseif (Get-ScheduledTask -TaskName $legacyTaskName -ErrorAction SilentlyContinue) {
        $taskStateText.Text = '检测到旧版任务 · 应用计划后自动迁移'
    }
    else { $taskStateText.Text = '任务管理器：未启用' }
    if (Test-Path -LiteralPath $logPath) {
        $lastLogText.Text = Get-Content -LiteralPath $logPath -Tail 1 -Encoding UTF8
    }
    else { $lastLogText.Text = '尚无运行日志' }
}

function Install-Schedule {
    $config = Save-Config
    if (-not $config.schedule.enabled) { throw '请先勾选“启用自动保活”。' }
    $powershellExe = (Get-Command powershell.exe).Source
    $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -ConfigPath "{1}"' -f $workerSource, $configPath
    $action = New-ScheduledTaskAction -Execute $powershellExe -Argument $arguments -WorkingDirectory $PSScriptRoot
    if ($config.schedule.mode -eq 'daily') {
        $at = [datetime]::ParseExact([string]$config.schedule.value, 'HH:mm', $null)
        $trigger = New-ScheduledTaskTrigger -Daily -At $at
    }
    else {
        $hours = [int]$config.schedule.value
        $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Hours $hours) -RepetitionDuration (New-TimeSpan -Days 3650)
    }
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'SoftwareToolkit Supabase 多项目保活'
    Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -InputObject $task -Force | Out-Null
    if (Get-ScheduledTask -TaskName $legacyTaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $legacyTaskName -Confirm:$false
    }
    Set-Status '配置已保存，计划已接入 SoftwareToolkit 定时任务管理器。'
    Refresh-Status
}

$timer = [Windows.Threading.DispatcherTimer]::new()
$timer.Interval = [TimeSpan]::FromMilliseconds(500)
$timer.Add_Tick({
    if (-not $script:runningProcess -or -not $script:runningProcess.HasExited) { return }
    $exitCode = $script:runningProcess.ExitCode
    $script:runningProcess.Dispose()
    $script:runningProcess = $null
    $timer.Stop()
    (Get-Control 'RunButton').IsEnabled = $true
    Set-Status $(if ($exitCode -eq 0) { '本次保活执行完成。' } else { '本次执行有失败，请查看日志。' }) ($exitCode -ne 0)
    Refresh-Status
})

(Get-Control 'AddButton').Add_Click({ $targets.Add((New-Target)); $targetGrid.SelectedIndex = $targets.Count - 1 })
(Get-Control 'RemoveButton').Add_Click({ if ($targetGrid.SelectedItem) { $targets.Remove($targetGrid.SelectedItem) } })
$scheduleMode.Add_SelectionChanged({
    $scheduleValue.Text = if ((Get-ScheduleMode) -eq 'daily') { '09:00' } else { '48' }
    Update-ScheduleUi
})
(Get-Control 'ImportButton').Add_Click({
    $dialog = [Microsoft.Win32.OpenFileDialog]::new()
    $dialog.Title = '导入 Supabase KeepAlive JSON'
    $dialog.Filter = 'JSON 配置 (*.json)|*.json'
    if ($dialog.ShowDialog($window) -ne $true) { return }
    try {
        $raw = Get-Content -Raw -LiteralPath $dialog.FileName -Encoding UTF8 | ConvertFrom-Json
        if (-not $raw.targets) { throw '配置中没有 targets。' }
        Apply-Config $raw
        Set-Status "已导入 $($raw.targets.Count) 个项目；保存后密码将改用 DPAPI 加密。"
    }
    catch { Show-Error "导入失败：$($_.Exception.Message)" }
})
(Get-Control 'InstallButton').Add_Click({ try { Install-Schedule } catch { Show-Error "应用计划失败：$($_.Exception.Message)" } })
(Get-Control 'UninstallButton').Add_Click({
    try {
        $task = Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction SilentlyContinue
        $legacyTask = Get-ScheduledTask -TaskName $legacyTaskName -ErrorAction SilentlyContinue
        if (-not $task -and -not $legacyTask) { Set-Status '自动任务尚未启用。'; return }
        if ([System.Windows.MessageBox]::Show($window, '停用自动保活任务？已保存的加密配置和日志会保留。', '确认停用', 'YesNo', 'Question') -ne 'Yes') { return }
        if ($task) { Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Confirm:$false }
        if ($legacyTask) { Unregister-ScheduledTask -TaskName $legacyTaskName -Confirm:$false }
        Set-Status '自动保活任务已停用；配置和日志已保留。'
        Refresh-Status
    }
    catch { Show-Error "停用失败：$($_.Exception.Message)" }
})
(Get-Control 'TaskManagerButton').Add_Click({
    try {
        if (-not (Test-Path -LiteralPath $taskManagerPath -PathType Leaf)) { throw '定时任务管理器组件不存在。' }
        Start-Process -FilePath ((Get-Command powershell.exe).Source) -WorkingDirectory (Split-Path -Parent $taskManagerPath) -Verb RunAs -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$taskManagerPath`""
        )
        Set-Status '已打开定时任务管理器，可统一运行、禁用或删除保活计划。'
    }
    catch { Show-Error "打开定时任务管理器失败：$($_.Exception.Message)" }
})
(Get-Control 'OpenLogButton').Add_Click({
    try {
        if (-not (Test-Path -LiteralPath $logPath)) { Set-Status '尚无运行日志。'; return }
        Start-Process notepad.exe -ArgumentList "`"$logPath`""
    }
    catch { Show-Error "打开日志失败：$($_.Exception.Message)" }
})
(Get-Control 'RunButton').Add_Click({
    try {
        [void](Save-Config)
        if ($script:runningProcess -and -not $script:runningProcess.HasExited) { Set-Status '已有保活任务正在运行。'; return }
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = (Get-Command powershell.exe).Source
        $psi.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -ConfigPath "{1}"' -f $workerSource, $configPath
        $psi.WorkingDirectory = $PSScriptRoot
        $psi.UseShellExecute = $true
        $psi.WindowStyle = 'Hidden'
        $script:runningProcess = [Diagnostics.Process]::Start($psi)
        (Get-Control 'RunButton').IsEnabled = $false
        Set-Status '正在执行保活，请稍候…'
        $timer.Start()
    }
    catch { Show-Error "启动失败：$($_.Exception.Message)" }
})

try {
    if (-not (Test-Path -LiteralPath $workerSource -PathType Leaf)) { throw '后台执行组件缺失。' }
    if (Test-Path -LiteralPath $configPath) {
        Apply-Config (Get-Content -Raw -LiteralPath $configPath -Encoding UTF8 | ConvertFrom-Json)
    }
    else {
        $targets.Add((New-Target))
        $scheduleMode.SelectedIndex = 0
        $scheduleValue.Text = '09:00'
        Update-ScheduleUi
    }
    Refresh-Status
}
catch { Show-Error "初始化失败：$($_.Exception.Message)" }

if ($ValidateOnly) {
    $timer.Stop()
    $window.Close()
    exit 0
}

$window.ShowDialog() | Out-Null
