# SoftwareToolkit

> 🧰 轻量级 Windows 工具启动器 —— 管理、分类、一键启动你的所有工具

SoftwareToolkit 是一个基于 **WPF (.NET 8)** 的桌面工具启动器，支持托盘常驻、全局热键呼出、工具分类搜索、批量操作，以及可扩展的插件体系。

---

## ✨ 功能特性

- **工具管理** —— 通过 `tools.json` 集中管理所有工具，支持分类、标签、图标
- **目录扫描** —— 自动扫描 `tools/` 子目录下的 `manifest.json`，零配置添加工具
- **多种启动方式** —— 支持可执行文件、URL、Shell 命令、.NET 项目编译、插件 DLL
- **分类 & 搜索** —— 左侧分类树 + 实时搜索过滤，快速定位工具
- **卡片 / 列表视图** —— 一键切换布局模式
- **批量操作** —— 批量模式下一次启动多个工具
- **软件清单** —— 扫描设备安装项，维护可下载、可分享的软件列表
- **Supabase 保活** —— 多项目保活、DPAPI 加密凭据、Windows 计划任务与日志状态
- **系统托盘** —— 最小化到托盘，不占任务栏空间
- **全局热键** —— <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> 呼出/隐藏主窗口（可在设置中自定义）
- **使用频率排序** —— 自动记录使用次数，常用工具优先显示
- **插件 SDK** —— `SoftwareToolkit.Sdk` 提供 `IToolPlugin` 接口，第三方可编写 DLL 插件
- **编译输出窗口** —— Build 类型工具实时显示 `dotnet publish` 编译日志

---

## 🏗 项目结构

```
SoftwareToolkit/
├── build.ps1                     # 一键构建脚本
├── build.bat                     # 双击打包入口（独立运行 ZIP）
├── package-all.cmd               # 同时打包独立版和轻量版
├── SoftwareToolkit.sln           # 解决方案
├── src/
│   ├── SoftwareToolkit/          # WPF 主程序
│   │   ├── Models/               # 数据模型 (ToolDefinition 等)
│   │   ├── Services/             # 核心服务
│   │   │   ├── ConfigLoader.cs   # 配置加载 (tools.json + manifest 扫描)
│   │   │   ├── ToolLauncher.cs   # 工具启动器 (多类型分发)
│   │   │   ├── BuildService.cs   # .NET 编译服务
│   │   │   ├── AppJsonContext.cs # AOT 兼容 JSON 源生成上下文
│   │   │   └── Converters.cs     # WPF 值转换器
│   │   ├── Resources/            # 图标等资源
│   │   ├── tools/                # 内置工具定义 (随输出目录复制)
│   │   └── tools.json            # 主配置文件
│   └── SoftwareToolkit.Sdk/      # 插件 SDK
│       └── IToolPlugin.cs        # 插件接口定义
└── release/                      # 发布包输出 (已 gitignore)
```

---

## 🚀 快速开始

### 环境要求

- **Windows 10/11 x64**
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**（8.0.x，与发布架构一致；独立包无需安装）
- **[.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**（仅构建时需要）

### 构建

**双击根目录的 `build.bat`**，自动生成 Release / win-x64 独立运行包和 ZIP，成功后打开 `release` 文件夹。双击 `package-all.cmd` 可同时生成独立版与轻量版，布局与 AI-Master 的打包方式一致。

```powershell
# 一键构建（Release、win-x64、自包含并生成 ZIP）
powershell -ExecutionPolicy Bypass -File build.ps1

# 轻量版（需要目标机器安装 .NET 8 Desktop Runtime）
powershell -ExecutionPolicy Bypass -File build.ps1 -Lightweight

# 打包完成后打开输出文件夹
powershell -ExecutionPolicy Bypass -File build.ps1 -OpenOutput

# 同时生成独立版和轻量版
powershell -ExecutionPolicy Bypass -File build.ps1 -All

# ARM64 / 调试 / 构建后停留，均可按需指定
powershell -ExecutionPolicy Bypass -File build.ps1 -Runtime win-arm64 -Configuration Debug -Pause

# 或手动执行
dotnet publish src\SoftwareToolkit\SoftwareToolkit.csproj -c Release -r win-x64 -o release\manual -p:SelfContained=false
```

构建结果使用固定名称写入 `release/SoftwareToolkit-架构-standalone` 或 `release/SoftwareToolkit-架构-lightweight`，同时生成同名 ZIP 和 `.sha256`。每次打包会先清理对应的旧发布包，避免残留文件混入；用户配置保存在 `%LOCALAPPDATA%/SoftwareToolkit`，不会被打包清理。

脚本显示发布、资源校验、清单、压缩四个阶段，并将编译日志保存为输出目录旁的同名 `.log`。打包前逐一校验 `tools.json` 和 `tools/` 文件是否完整且与源码一致；ZIP 包含隐藏文件，压缩完成后才生成正式 `.zip` 文件。命令行自动化请直接使用 `build.ps1`；`build.bat` 会等待按键，传入参数时按指定参数运行脚本。

双击输出目录中的 `SoftwareToolkit.exe` 即可运行，`tools.json` 与 `tools/` 必须保留在它旁边。`package-manifest.json` 记录打包参数及每个有效载荷文件的大小、SHA-256（不包含清单自身）。项目文件统一负责复制资源，不再由脚本重复复制。升级前请备份工具配置与 `%LOCALAPPDATA%/SoftwareToolkit` 用户状态。

默认发布为自包含单文件，Release 不携带调试符号，托盘直接使用 .NET 内置 `NotifyIcon`。独立包启用单文件压缩；为了兼容 WPF 和反射插件，禁用裁剪。

### 运行

启动后显示主窗口。双击托盘图标或按 <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> 呼出主窗口；是否在关闭或最小化时隐藏到托盘由设置控制。

单击工具查看底部的路径与编辑入口，双击工具或点击卡片上的“启动”按钮打开。每张卡片都提供收藏星标和更多操作菜单。卡片随窗口宽度自动排成两列或三列，列表模式提供更紧凑的浏览方式。

顶部可选择常用优先、名称、最近使用、收藏优先四种排序。“多选”模式下，选中的工具会高亮，底部提供收藏、删除和完成按钮；筛选保留仍然可见的选择，并清除隐藏项，避免误操作。没有选中工具时，批量操作按钮禁用。

右键或点击“更多”打开分组菜单，可启动、收藏、编辑、打开所在位置、复制路径或进入多选。网页显示“复制链接”，命令显示“复制命令”；本地文件不存在时禁用打开位置。多选右键针对所选工具，右键未选中的工具会将它加入选择。按 `Shift+F10` 或菜单键也可打开当前工具菜单。“从工具箱移除”仅移除条目，保留原软件文件。

按 `Ctrl+F` 聚焦搜索，`↓` 选中搜索结果，`Enter` 启动工具，`Esc` 清空搜索或退出多选，`F5` 刷新列表。多选时，在工具列表中按空格切换选中、`Ctrl+A` 全选。搜索支持名称、分类、描述与标签，使用 180 ms 防抖。列表布局使用回收式虚拟化；大量工具建议切换列表布局。

### 交互回归检查

```powershell
dotnet run --project tests/SoftwareToolkit.Smoke -c Release
```

检查搜索、排序、单击选中、详情栏、多选筛选、布局复用与列表虚拟化。测试使用内存中的示例工具，不执行工具或改写配置。传入 PNG 路径可额外渲染真实内置工具的卡片、详情、多选、窄窗口、列表及空状态，并验证三列/两列排布：

```powershell
dotnet run --project tests/SoftwareToolkit.Smoke -c Release -- publish/layout-preview.png
```

---

## 📦 工具配置

### 主配置文件 `tools.json`

```jsonc
{
  "scanDirectories": ["tools"],   // 要扫描 manifest 的子目录
  "tools": [
    {
      "id": "notepad",
      "name": "记事本",
      "description": "Windows 自带文本编辑器",
      "category": "工具/文本",
      "tags": ["文本", "编辑"],
      "kind": "Executable",        // 启动类型
      "path": "notepad.exe"        // 可执行文件路径
    }
  ]
}
```

### 工具类型 (`kind`)

| 类型 | 说明 | `path` 示例 |
|------|------|------------|
| `Executable` | 外部可执行文件 (.exe/.bat/.ps1/.py 等) | `notepad.exe` 或 `tools\my-tool\run.bat` |
| `Url` | URL 或本地 HTML | `https://example.com` 或 `docs/index.html` |
| `Command` | Shell 命令组 | `echo hello && pause` |
| `Build` | .NET 项目编译 (dotnet publish) | `..\MyProject.csproj` |
| `Plugin` | 插件 DLL (实现 IToolPlugin) | `tools\my-plugin\plugin.dll` |
| `BuiltIn` | 随主程序发布的原生工具 | `software-inventory` |

### 目录扫描 `manifest.json`

在 `tools/<工具名>/manifest.json` 中定义工具，程序启动时自动加载：

```jsonc
{
  "name": "我的工具",
  "description": "通过 manifest 自动加载的工具",
  "category": "开发/工具",
  "tags": ["示例"],
  "kind": "Executable",
  "path": "run.bat"
}
```

---

## � 内置工具

### 软件清单 (`software-inventory`)

扫描 Windows 的 HKLM/HKCU 32 位与 64 位卸载注册表及当前用户 Store/MSIX 应用，并在 WinGet 可用时自动匹配包 ID。可从设备软件中勾选条目加入个人清单；下载链接默认按注册表官网、Microsoft Store 或 WinGet 安装页自动配置，也可逐项修改并恢复自动值。

清单自动保存到 `%LOCALAPPDATA%\SoftwareToolkit\software-list.json`。支持复制 Markdown 分享文本，以及导出不含安装路径等设备隐私信息的独立 HTML 分享页。扫描的辅助命令设有超时，Store 或 WinGet 不可用时仍会保留注册表扫描结果。

### 局域网文件共享 (`lan-share`)

一键启动局域网文件共享服务，**PC / 安卓 / iOS** 浏览器扫码即可上传下载，无需安装客户端。

**功能亮点：**
- 📁 文件浏览、上传、下载，支持目录导航
- 💬 实时聊天，支持发送文件附件
- 📱 二维码扫码连接，手机端自适应布局
- 🔍 设备自动发现（UDP 广播）+ 客户端注册
- 🌙 深色/浅色主题切换
- ⚡ 零依赖 —— 仅需 Windows 自带的 PowerShell 5.1 + .NET HttpListener

**启动方式：** 在 SoftwareToolkit 中双击“局域网文件共享”卡片、点击其“启动”按钮，或直接运行 `tools/lan-share/lan-share.bat`

**默认端口：** `8088`（可通过 `tools/lan-share/lan-share.bat` 修改参数自定义）

**兼容性：** 任何支持现代浏览器的设备均可访问（Windows / macOS / Linux / Android / iOS）

### 定时任务管理器 (`task-scheduler`)

通过图形界面创建和管理 Windows 计划任务，支持开机时、用户登录时、每天固定时间及按分钟循环执行。任务由 Windows 任务计划程序托管，关闭 SoftwareToolkit 后仍然有效；开机任务可在用户尚未登录时以 SYSTEM 身份运行。

### Supabase 保活 (`supabase-keepalive`)

从独立 SupabaseKeepAliveTool 迁入的多项目保活工具。密码使用 Windows DPAPI 加密保存在当前用户目录，后台任务由 Windows 计划任务按次执行，无需保持网页或主程序开启。保活计划统一注册到 `\SoftwareToolkit\` 任务目录，可直接在“定时任务管理器”中运行、启停或删除。支持导入旧版 JSON、立即测试、每日/间隔计划、状态查看和日志轮转，并拒绝 secret/service_role key。详细的表结构与 RLS 示例见 [`src/SoftwareToolkit/tools/supabase-keepalive/README.md`](src/SoftwareToolkit/tools/supabase-keepalive/README.md)。

---

## �🔌 插件开发

引用 `SoftwareToolkit.Sdk` 项目，实现 `IToolPlugin` 接口：

```csharp
using SoftwareToolkit.Sdk;

public class MyPlugin : IToolPlugin
{
    public string Id => "my-plugin";
    public string DisplayName => "我的插件";

    public void Launch(IToolContext context)
    {
        context.Log("插件已启动");
        // 弹出自己的窗口或执行任务...
    }
}
```

编译后将 DLL 放入 `tools/<插件名>/` 目录，在 `manifest.json` 中配置 `"kind": "Plugin"` 即可。

---

## 🛠 技术栈

| 技术 | 说明 |
|------|------|
| .NET 8 | 目标框架 |
| WPF | UI 框架 |
| System.Windows.Forms.NotifyIcon | .NET 内置系统托盘，无第三方托盘依赖 |
| System.Text.Json (源生成) | AOT 兼容的 JSON 序列化 |
| Single-file publish | 单文件发布 (框架依赖模式) |

---

## 📄 许可证

本项目基于 [Apache License 2.0](LICENSE) 开源。
