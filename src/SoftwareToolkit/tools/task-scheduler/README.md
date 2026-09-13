# 定时任务管理器

[返回项目主页](https://github.com/PN-BUG/software-toolkit)

用于创建和管理 Windows 计划任务。任务统一保存在任务计划程序的 `\SoftwareToolkit\` 文件夹中，关闭 SoftwareToolkit 后仍会由 Windows 执行。

- 开机时（以 SYSTEM 身份运行，无需用户登录）
- 用户登录时
- 每天固定时间
- 按分钟循环

支持 `.exe`、`.bat`、`.cmd`、`.ps1`、`.py` 等文件。创建开机任务和管理系统计划任务需要管理员权限，启动器会自动申请权限。
