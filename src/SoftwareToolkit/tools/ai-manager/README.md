# AI 管理大师

[返回项目主页](https://github.com/PN-BUG/software-toolkit)

AI 管理大师通过已安装并登录的 Codex CLI 连接 Codex App Server，在本机显示额度、用量趋势和任务状态。认证由 Codex 管理，本工具不会读取或保存登录令牌。

![AI 管理大师主面板与桌面悬浮监控](https://raw.githubusercontent.com/PN-BUG/software-toolkit/main/docs/AI%E7%AE%A1%E7%90%86%E5%A4%A7%E5%B8%88.png)

_主面板集中展示额度窗口、消耗预测、任务闸门和保护策略；右上角为桌面悬浮监控。_

## 功能

- 查看账户返回的总额度、7 天窗口和 5 小时窗口。
- 显示剩余比例、重置时间及近期 Token 用量。
- 根据当前周期消耗速度估算额度是否会提前耗尽。
- 设置预警线和暂停线。
- 在悬浮窗中查看最近任务、模型、推理强度和最紧张的额度窗口。
- 超过暂停线时提醒，并尝试中断当前 App Server 连接可见的任务。

## 使用要求

1. 安装 Codex CLI。
2. 使用 Codex 完成登录。
3. 在 SoftwareToolkit 中打开“AI 管理大师”，点击“立即同步”。

## 悬浮监控

点击“悬浮监控”打开桌面右下角窗口。窗口默认置顶，每 20 秒自动刷新，可拖动、取消置顶或单独关闭。

悬浮窗只显示当前连接能够读取到的状态。Codex 的运行态不会在不同客户端进程之间完整共享，因此其他 Codex 窗口里的任务可能仍需要手动停止。

## 数据与隐私

设置和本地统计保存在 `%LOCALAPPDATA%\SoftwareToolkit\ai-manager.json`。登录凭据仍由 Codex 自己管理。

如果同步失败，请先确认 Codex CLI 可以正常启动且已登录，再在工具中重新同步。
