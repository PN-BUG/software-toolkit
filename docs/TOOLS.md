# 工具与插件配置

SoftwareToolkit 从主配置文件和工具目录加载工具：

1. 读取可执行文件旁的 `tools.json`。
2. 扫描 `scanDirectories` 指定目录中的一级子目录。
3. 从每个子目录读取 `manifest.json`。
4. 合并 `%LOCALAPPDATA%\SoftwareToolkit\userstate.json` 中的收藏和使用记录。

## 主配置 `tools.json`

```jsonc
{
  "scanDirectories": ["tools"],
  "tools": [
    {
      "id": "notepad",
      "name": "记事本",
      "description": "Windows 自带文本编辑器",
      "category": "工具/文本",
      "tags": ["文本", "编辑"],
      "kind": "Executable",
      "path": "notepad.exe"
    }
  ]
}
```

`scanDirectories` 相对于 `SoftwareToolkit.exe` 所在目录。没有 `tools.json` 时，程序仍会尝试扫描默认的 `tools/` 目录。

## 独立工具目录

```text
tools/
└── my-tool/
    ├── manifest.json
    ├── run.bat
    └── icon.png
```

```jsonc
{
  "id": "my-tool",
  "name": "我的工具",
  "description": "通过目录扫描自动加载",
  "category": "开发/工具",
  "tags": ["示例"],
  "icon": "icon.png",
  "kind": "Executable",
  "path": "run.bat",
  "args": "--source ${dir}",
  "runAsAdmin": false
}
```

目录清单中省略 `id` 时，默认使用目录名。相对的 `path` 和图标路径以 `manifest.json` 所在目录为基准。

## 字段参考

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `id` | 建议 | 稳定的唯一标识；影响收藏与使用记录 |
| `name` | 是 | 工具显示名称 |
| `description` | 否 | 详情和搜索文本 |
| `category` | 否 | 分类路径，例如 `开发/编辑器`；默认为“未分类” |
| `tags` | 否 | 用于搜索和筛选的字符串数组 |
| `icon` | 否 | PNG、ICO、JPG 路径或内置图标名 |
| `kind` | 是 | 启动类型，见下表 |
| `path` | 是 | 文件、网址、命令、项目、插件或内置工具标识 |
| `args` | 否 | 传给外部进程的命令行参数 |
| `workingDirectory` | 否 | 工作目录；省略时通常使用目标文件所在目录 |
| `runAsAdmin` | 否 | 是否申请管理员权限，默认为 `false` |
| `author` | 否 | 作者信息 |
| `version` | 否 | 工具版本 |
| `arguments` | 否 | 传给 DLL 插件的字符串键值对 |

`args` 支持 `${dir}`（配置文件所在目录）和 `${self}`（配置文件完整路径）。可执行文件路径中的 Windows 环境变量也会展开。

## 工具类型

| `kind` | `path` 含义 | 示例 |
| --- | --- | --- |
| `Executable` | EXE、BAT、CMD、PS1、PY 等文件 | `run.bat` |
| `Url` | HTTP(S) 地址或本地 HTML | `https://example.com` |
| `Command` | 在命令提示符中执行的命令 | `echo hello && pause` |
| `Build` | `.csproj` 或 `.sln` | `..\MyProject.csproj` |
| `Plugin` | 实现 `IToolPlugin` 的 DLL | `my-plugin.dll` |
| `BuiltIn` | 主程序识别的内置工具标识 | `software-inventory` |

`BuiltIn` 由主程序实现，不能仅靠清单新增。自定义工具通常使用其他五种类型。

## DLL 插件

插件项目引用 `src/SoftwareToolkit.Sdk/SoftwareToolkit.Sdk.csproj`，并实现 `IToolPlugin`：

```csharp
using SoftwareToolkit.Sdk;

public sealed class MyPlugin : IToolPlugin
{
    public string Id => "my-plugin";
    public string DisplayName => "我的插件";

    public void Launch(IToolContext context)
    {
        context.Log("插件已启动");
    }
}
```

`IToolContext` 提供主窗口句柄、插件目录、清单中的 `arguments` 以及状态日志方法。编译后将 DLL 和资源放入独立工具目录，并配置：

```jsonc
{
  "id": "my-plugin",
  "name": "我的插件",
  "kind": "Plugin",
  "path": "my-plugin.dll",
  "arguments": {
    "profile": "default"
  }
}
```

插件由反射动态加载。替换 DLL 后应重新启动 SoftwareToolkit，以避免旧程序集仍被当前进程占用。

## 修改和删除

- 主配置中的工具会写回 `tools.json`。
- 目录清单工具会写回自己的 `manifest.json`。
- 将文件拖入主窗口时，程序会在 `tools/` 下创建独立目录及清单。
- 从工具箱移除目录清单工具会删除对应的 `manifest.json`；目录为空时一并删除目录，其他工具文件会保留。
- 收藏、排序和使用次数属于用户状态，不应写入工具清单。

## 排错

- 工具未出现：检查 JSON 语法、`scanDirectories` 和清单所在层级。
- 打不开本地文件：确认相对路径是相对于定义它的配置文件，而不是当前终端目录。
- 管理员工具无响应：检查 UAC 提示是否被其他窗口遮挡。
- 插件无法加载：确认 DLL 引用了兼容版本的 SDK，且包含一个可实例化的 `IToolPlugin` 实现。
