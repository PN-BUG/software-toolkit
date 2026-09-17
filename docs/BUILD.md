# 构建与发布

本文说明 SoftwareToolkit 的构建要求、发布模式、脚本参数、输出结构和验证流程。

## 环境要求

| 场景 | 要求 |
| --- | --- |
| 构建 | Windows 10/11、.NET 8 SDK |
| 运行框架依赖包 | 与发布架构一致的 .NET 8 Desktop Runtime |
| 运行自包含包 | 无需预装 .NET 运行时 |

项目支持 `win-x64` 和 `win-arm64`，默认配置为 Release / win-x64。

## 选择发布模式

| 模式 | 脚本参数 | 优点 | 代价 |
| --- | --- | --- | --- |
| 自包含 | 默认 | 解压即可运行，不依赖机器上的 .NET | 包含 .NET/WPF 运行时，体积较大 |
| 轻量版（框架依赖） | `-Lightweight` 或 `-FrameworkDependent` | 包体积小 | 目标机器需要 .NET 8 Desktop Runtime |

自包含单文件已经启用程序集压缩。项目没有开启 trimming，因为 WPF、WinForms 托盘以及反射加载的插件无法安全依赖裁剪结果。Microsoft 的相关说明见[单文件部署](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)和[已知裁剪不兼容项](https://learn.microsoft.com/dotnet/core/deploying/trimming/incompatibilities)。

## 常用命令

### 双击构建

直接运行 `build.bat`。默认行为相当于：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -OpenOutput
```

批处理入口会在结束时等待按键。传入参数时，会将参数原样转交给 `build.ps1`：

```powershell
.\build.bat -Lightweight
```

### 命令行或 CI

自动化环境应直接调用 `build.ps1`；脚本默认不等待输入，并在失败时返回非零退出码。

```powershell
# 默认自包含目录及 ZIP
powershell -ExecutionPolicy Bypass -File build.ps1

# 轻量版目录及 ZIP
powershell -ExecutionPolicy Bypass -File build.ps1 -Lightweight

# 一次生成自包含版和轻量版
powershell -ExecutionPolicy Bypass -File build.ps1 -All

# ARM64 Release
powershell -ExecutionPolicy Bypass -File build.ps1 -Runtime win-arm64

# 仅生成目录，不压缩
powershell -ExecutionPolicy Bypass -File build.ps1 -NoZip

# Debug，并在结束时等待输入
powershell -ExecutionPolicy Bypass -File build.ps1 -Configuration Debug -Pause
```

## 脚本参数

| 参数 | 可选值 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `-Configuration` | `Release`、`Debug` | `Release` | 构建配置 |
| `-Runtime` | `win-x64`、`win-arm64` | `win-x64` | 目标运行时标识 |
| `-Lightweight` | 开关 | 关闭 | 生成依赖 .NET 8 Desktop Runtime 的轻量版；别名为 `-FrameworkDependent` |
| `-All` | 开关 | 关闭 | 顺序生成自包含版和轻量版 |
| `-NoZip` | 开关 | 关闭 | 只生成目录，不生成 ZIP |
| `-OpenOutput` | 开关 | 关闭 | 成功后打开输出目录 |
| `-Pause` | 开关 | 关闭 | 结束前等待按键 |

## 输出结构

输出结构与 AI-Master 保持一致，使用固定的发布目录名：

```text
release/
├── SoftwareToolkit-win-x64-standalone/
│   ├── SoftwareToolkit.exe
│   ├── SoftwareToolkit.bat
│   ├── START-HERE.txt
│   ├── LICENSE
│   ├── package-manifest.json
│   ├── tools.json
│   └── tools/
├── SoftwareToolkit-win-x64-lightweight/
├── SoftwareToolkit-win-x64-standalone.log
├── SoftwareToolkit-win-x64-lightweight.log
├── SoftwareToolkit-win-x64-standalone.zip
├── SoftwareToolkit-win-x64-standalone.zip.sha256
├── SoftwareToolkit-win-x64-lightweight.zip
└── SoftwareToolkit-win-x64-lightweight.zip.sha256
```

`standalone` 表示自包含包，`lightweight` 表示框架依赖包。每次构建会安全清理同模式的旧目录和压缩包。`package-manifest.json` 记录构建配置、运行时、文件大小和 SHA-256；ZIP 自身的哈希写入同名 `.zip.sha256` 文件。

## 发布脚本执行的检查

`build.ps1` 会依次：

1. 调用 `dotnet publish`。
2. 检查主程序、`tools.json` 和必要的内置工具清单。
3. 校验所有随包资源与源码文件的哈希一致。
4. 写入启动说明、许可证和包清单。
5. 默认生成 ZIP 及其 SHA-256；使用 `-NoZip` 可跳过。

构建日志位于输出目录旁。失败时，先查看终端错误，再打开对应 `.log` 文件获取完整的发布输出。

## 手动发布

```powershell
dotnet publish src\SoftwareToolkit\SoftwareToolkit.csproj `
  -c Release `
  -r win-x64 `
  --self-contained=false `
  -o release\manual
```

手动发布不会生成启动说明、包清单、ZIP 校验和，也不会执行 `build.ps1` 中的资源完整性检查。

## 验证

```powershell
dotnet build SoftwareToolkit.sln -c Release
dotnet run --project tests/SoftwareToolkit.Smoke -c Release
powershell -ExecutionPolicy Bypass -File build.ps1 -All
```

烟雾测试覆盖搜索、排序、选择、批量操作、布局、软件清单、AI 管理窗口和本地化，不会执行配置中的外部工具或改写工具配置。

## 升级现有安装

发布脚本会替换 `release` 中同名模式的旧包。手动替换现有安装前，请备份：

- 发布目录中的 `tools.json` 与 `tools/`。
- `%LOCALAPPDATA%\SoftwareToolkit` 中的用户状态和内置工具数据。
