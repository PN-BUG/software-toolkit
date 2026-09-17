# 定时任务管理器

用于创建和管理 Windows 计划任务。任务统一保存在任务计划程序的 `\SoftwareToolkit\` 文件夹中，关闭 SoftwareToolkit 后仍会由 Windows 执行。

- 开机时（以 SYSTEM 身份运行，无需用户登录）
- 用户登录时
- 每天固定时间
- 按分钟循环

支持 `.exe`、`.bat`、`.cmd`、`.ps1`、`.py` 等文件。创建开机任务和管理系统计划任务需要管理员权限，启动器会自动申请权限。

工具可以独立使用：即使没有安装其他工具，也始终提供“自定义任务”预设，可手动选择任意受支持的程序或脚本。其他工具可通过自己的 `manifest.json` 提供可选预设；预设还可声明一个可浏览选择的配置文件。如果预设声明了 `logPath`，选择对应的已创建任务后可直接查看最新日志，并用记事本打开完整日志。

带配置文件的预设只显示配置选择器；任务所需的命令行参数由工具在后台生成，不需要重复填写。自定义任务仍显示“参数”输入框。

## 脱离 SoftwareToolkit 使用

复制整个 `task-scheduler` 文件夹到任意位置，然后双击 `task-scheduler.vbs`。独立启动器会申请管理员权限，并在不显示 PowerShell/CMD 控制台的情况下打开图形界面。也可双击 `task-scheduler.bat`，它会转交给同一个独立启动器。

独立运行只需要文件夹中的以下文件：

- `task-scheduler.ps1`
- `task-scheduler.vbs`
- `task-scheduler.bat`（可选快捷入口）

`manifest.json` 只用于接入 SoftwareToolkit，独立运行时不需要。其他工具提供的预设是可选的；没有它们时“自定义任务”仍然可用。
