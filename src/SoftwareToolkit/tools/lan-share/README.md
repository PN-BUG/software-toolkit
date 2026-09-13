# lan-share —— 局域网文件共享工具

[返回项目主页](https://github.com/PN-BUG/software-toolkit)

> 🌐 一键在 PC 上启动 HTTP 文件服务器，让局域网内的安卓手机 / iPhone / 其他 PC 通过浏览器扫码访问，**上传 / 下载 / 浏览** 文件，无需安装任何客户端。

## ✨ 特性

- **零依赖**：仅依赖 Windows 自带的 PowerShell 5.1 + .NET Framework，无需额外安装
- **跨平台客户端**：PC、安卓、iOS、平板……任何带浏览器的设备
- **双向传输**：浏览器 ↔ PC 之间互传文件
- **目录浏览**：网页化文件列表，自动显示文件大小 / 修改时间，支持子目录
- **多端访问**：可同时被多台设备访问
- **拖拽即用**：拖文件夹到 `lan-share.bat` 即可共享该目录

## 🚀 使用方法

### 1. 通过 SoftwareToolkit 启动
直接在 SoftwareToolkit 中点击「局域网文件共享」工具。
默认共享 **本工具所在目录** 的上一级（即整个 SoftwareToolkit `publish/`）。

### 2. 拖拽启动
把任意文件夹拖到 `lan-share.bat` 上 —— 该文件夹就会立即变成共享根目录。

### 3. 命令行启动

```powershell
# 共享当前目录,默认端口 8088
.\lan-share.bat

# 共享指定目录
.\lan-share.bat D:\MyShare

# 指定目录和端口
.\lan-share.bat D:\MyShare 9000

# 直接调用 PowerShell 脚本
powershell -ExecutionPolicy Bypass -File lan-share.ps1 -SharePath D:\MyShare -Port 9000
```

### 4. 客户端访问
启动后控制台会显示类似：
```
  ✅ 服务已启动
  ───────────────────────────────────
  本机访问:   http://127.0.0.1:8088
  局域网访问: http://192.168.1.123:8088
  共享目录:   D:\MyShare
  ───────────────────────────────────
  📱 手机连接同一 WiFi 后,在浏览器输入上面的局域网地址
  按 Ctrl+C 停止服务
```
> 把局域网地址输入手机浏览器即可，或用任意二维码 App 扫一下也行。

## 🛠 高级参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `-SharePath` | 共享根目录 | 当前工作目录 |
| `-Port` | HTTP 监听端口 | `8088` |
| `-ReadOnly` | 只允许下载,禁用上传 | `false` |
| `-Token` | 简易访问令牌,带 `?t=xxx` 才能访问 | 无 |

```powershell
# 只读模式 + 访问令牌
powershell -ExecutionPolicy Bypass -File lan-share.ps1 -SharePath D:\Files -Port 8088 -ReadOnly -Token mysecret
```

## ⚠️ 防火墙

首次运行时，Windows 防火墙可能会弹窗询问是否允许 PowerShell 通信。
**请勾选「专用网络」** 并允许，否则手机连不上。

如需手动放行端口:
```powershell
# 以管理员身份运行
New-NetFirewallRule -DisplayName "lan-share 8088" -Direction Inbound -Protocol TCP -LocalPort 8088 -Action Allow
```

## ⚠️ HttpListener URL 保留

监听 `0.0.0.0` 在某些系统上需要 URL 保留。如果启动失败，请以管理员身份运行一次：
```powershell
netsh http add urlacl url=http://+:8088/ user=Everyone
```
或者用本工具内置的 fallback —— 监听 `127.0.0.1` 时无需特权，但只有本机能访问。

## 📁 文件结构

```
lan-share/
├── manifest.json     # SoftwareToolkit 工具描述
├── lan-share.bat     # 命令行入口(支持拖拽)
├── lan-share.ps1     # 核心 HTTP 服务器实现
└── README.md         # 本文件
```

## 🔐 安全提示

- 本工具**没有加密**(纯 HTTP),不要在公网或不可信网络使用
- 启用 `-Token` 可以做最基本的访问控制,但仍不能防嗅探
- 不要用 `-SharePath C:\` 这种粗暴写法 —— 任何人都能下载你整个 C 盘
- 建议为每次会话单独建一个共享目录,会话结束后立即 Ctrl+C 关闭
