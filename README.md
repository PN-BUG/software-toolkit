# SoftwareToolkit

> 🧰 轻量级 Windows 工具启动器 —— 管理、分类、一键启动你的所有工具

SoftwareToolkit 是一个基于 **WPF (.NET 8)** 的桌面工具启动器，支持托盘常驻、全局热键呼出、工具分类搜索、批量操作，以及可扩展的插件体系。

![SoftwareToolkit 主界面：工具分类、搜索、卡片与详情操作](docs/SoftwareToolkit.png)

_主工作台支持分类浏览、实时搜索、卡片与列表布局，以及工具详情操作。_

## 文档导航

- [快速开始](#-快速开始)
- [基本操作](#运行)
- [内置工具](#内置工具)
- [工具与插件配置](docs/TOOLS.md)
- [构建与发布](docs/BUILD.md)

---

## ✨ 功能特性

- **工具管理** —— 通过 `tools.json` 集中管理所有工具，支持分类、标签、图标
- **目录扫描** —— 自动扫描 `tools/` 子目录下的 `manifest.json`，零配置添加工具
- **多种启动方式** —— 支持可执行文件、URL、Shell 命令、.NET 项目编译、插件 DLL
- **分类 & 搜索** —— 左侧分类树 + 实时搜索过滤，快速定位工具
- **卡片 / 列表视图** —— 一键切换布局模式
- **批量操作** —— 批量模式下一次启动多个工具
- **软件清单** —— 扫描设备安装项，维护可下载、可分享的软件列表
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
├── build.bat                     # 双击打包入口（小体积框架依赖 ZIP）
├── docs/                         # 构建、工具和插件文档
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
├── tests/                        # 交互烟雾测试
└── publish/                      # 构建输出 (已 gitignore)
```

---

## 🚀 快速开始

### 环境要求

- **Windows 10/11 x64 或 ARM64**
- **[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**（8.0.x，与发布架构一致；独立包无需安装）
- **[.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)**（仅构建时需要）

### 构建

**双击根目录的 `build.bat`**，默认生成 Release / win-x64 小体积框架依赖 ZIP，并打开输出目录。

```powershell
# 小体积框架依赖 ZIP；目标机器需要 .NET 8 Desktop Runtime
powershell -ExecutionPolicy Bypass -File build.ps1 -Zip

# 自包含 ZIP；目标机器无需安装 .NET
powershell -ExecutionPolicy Bypass -File build.ps1 -SelfContained -Zip
```

当前版本框架依赖 ZIP 约 0.24 MB，自包含 ZIP 约 63 MB。完整参数、输出结构、校验机制和 CI 用法参见[构建与发布](docs/BUILD.md)。

### 运行

启动后显示主窗口。双击托盘图标或按 <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> 呼出主窗口；是否在关闭或最小化时隐藏到托盘由设置控制。

单击工具查看底部的路径与编辑入口，双击工具或点击卡片上的“启动”按钮打开。每张卡片都提供收藏星标和更多操作菜单。卡片随窗口宽度自动排成两列或三列，列表模式提供更紧凑的浏览方式。

顶部可选择常用优先、名称、最近使用、收藏优先四种排序。“多选”模式下，选中的工具会高亮，底部提供收藏、删除和完成按钮；筛选保留仍然可见的选择，并清除隐藏项，避免误操作。没有选中工具时，批量操作按钮禁用。

右键或点击“更多”打开分组菜单，可启动、收藏、编辑、打开所在位置、复制路径或进入多选。网页显示“复制链接”，命令显示“复制命令”；本地文件不存在时禁用打开位置。多选右键针对所选工具，右键未选中的工具会将它加入选择。按 `Shift+F10` 或菜单键也可打开当前工具菜单。“从工具箱移除”仅移除条目，保留原软件文件。

按 `Ctrl+F` 聚焦搜索，`↓` 选中搜索结果，`Enter` 启动工具，`Esc` 清空搜索或退出多选，`F5` 刷新列表。多选时，在工具列表中按空格切换选中、`Ctrl+A` 全选。搜索支持名称、分类、描述与标签，使用 180 ms 防抖。列表布局使用回收式虚拟化；大量工具建议切换列表布局。

## 开发与验证

```powershell
dotnet run --project tests/SoftwareToolkit.Smoke -c Release
```

检查搜索、排序、单击选中、详情栏、多选筛选、布局复用与列表虚拟化。测试使用内存中的示例工具，不执行工具或改写配置。传入 PNG 路径可额外渲染真实内置工具的卡片、详情、多选、窄窗口、列表及空状态，并验证三列/两列排布：

```powershell
dotnet run --project tests/SoftwareToolkit.Smoke -c Release -- publish/layout-preview.png
```

发布包的完整验证流程参见[构建与发布](docs/BUILD.md#验证)。

---

## 工具与插件配置

工具可直接写入 `tools.json`，也可放在独立目录中，通过 `tools/<工具名>/manifest.json` 自动发现。支持可执行文件、网址、Shell 命令、.NET 构建、DLL 插件和内置工具。

字段参考、路径规则、完整示例和 `IToolPlugin` 用法统一收录在[工具与插件配置](docs/TOOLS.md)。

---

## 内置工具

| 工具 | 用途 | 文档 |
| --- | --- | --- |
| 软件清单 | 扫描本机软件，维护下载入口并导出分享清单 | [使用说明](src/SoftwareToolkit/tools/software-inventory/README.md) |
| AI 管理大师 | 查看 Codex 额度、消耗趋势、任务和模型 | [使用说明](src/SoftwareToolkit/tools/ai-manager/README.md) |
| 局域网文件共享 | 通过浏览器在局域网内传输文件 | [使用说明](src/SoftwareToolkit/tools/lan-share/README.md) |
| 定时任务管理器 | 创建和管理 Windows 计划任务 | [使用说明](src/SoftwareToolkit/tools/task-scheduler/README.md) |

---

## 插件开发

插件引用 `SoftwareToolkit.Sdk` 并实现 `IToolPlugin`。编译后将 DLL 与资源放入独立工具目录，再在 `manifest.json` 中配置 `"kind": "Plugin"`。接口示例和上下文字段参见[工具与插件配置](docs/TOOLS.md#dll-插件)。

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
