using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using SoftwareToolkit.Models;
using SoftwareToolkit.Sdk;

namespace SoftwareToolkit.Services;

/// <summary>
/// 工具启动器: 根据 ToolKind 以不同方式启动工具。
/// </summary>
public sealed class ToolLauncher
{
    private readonly ConfigLoader _configLoader;
    private readonly IntPtr _ownerHandle;

    /// <summary>Windows 系统目录 (如 C:\Windows\System32)</summary>
    private static readonly string SystemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);

    private readonly BuildService _buildService = new();

    public ToolLauncher(ConfigLoader configLoader, IntPtr ownerHandle)
    {
        _configLoader = configLoader;
        _ownerHandle = ownerHandle;
    }

    /// <summary>
    /// 启动一个工具。
    /// </summary>
    [RequiresUnreferencedCode("插件系统依赖运行时反射，Plugin 类型无法被裁剪器静态分析")]
    public async Task LaunchAsync(ToolDefinition tool)
    {
        var id = ConfigLoader.GetEffectiveId(tool);
        _configLoader.RecordUsage(id);

        try
        {
            switch (tool.Kind)
            {
                case ToolKind.Executable:
                    await LaunchExecutableAsync(tool);
                    break;
                case ToolKind.Url:
                    LaunchUrl(tool);
                    break;
                case ToolKind.Plugin:
                    await LaunchPluginAsync(tool);
                    break;
                case ToolKind.Command:
                    await LaunchCommandAsync(tool);
                    break;
                case ToolKind.Build:
                    LaunchBuild(tool);
                    break;
                case ToolKind.BuiltIn:
                    LaunchBuiltIn(tool);
                    break;
                default:
                    throw new NotSupportedException($"不支持的工具类型: {tool.Kind}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ToolLauncher] 启动 {tool.Name} 失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 启动外部可执行文件/脚本。
    /// </summary>
    private Task LaunchExecutableAsync(ToolDefinition tool)
    {
        var path = ResolveSystemToolPath(tool.Path, tool.SourceFile);
        var args = ResolveArgs(tool.Args, tool);
        var workDir = !string.IsNullOrWhiteSpace(tool.WorkingDirectory)
            ? ResolvePath(tool.WorkingDirectory, tool.SourceFile)
            : Path.GetDirectoryName(path) ?? "";

        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args ?? "",
            WorkingDirectory = workDir,
            UseShellExecute = true
        };

        if (tool.RunAsAdmin)
            psi.Verb = "runas";

        Process.Start(psi);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 解析可执行文件路径：配置目录优先，然后查找 Windows 目录和 PATH。
    /// </summary>
    private static string ResolveSystemToolPath(string path, string? sourceFile)
    {
        path = Environment.ExpandEnvironmentVariables(path);

        if (Path.IsPathRooted(path))
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
            return FindExecutable(Path.GetFileName(path)) ?? path;
        }

        if (path.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            return ResolvePath(path, sourceFile);

        var localPath = ResolvePath(path, sourceFile);
        if (File.Exists(localPath)) return localPath;

        return FindExecutable(path) ?? path;
    }

    private static string? FindExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var extensions = Path.HasExtension(fileName)
            ? new[] { string.Empty }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windowsDir = Path.GetDirectoryName(SystemDir);
        var directories = new List<string> { SystemDir };
        if (!string.IsNullOrWhiteSpace(windowsDir)) directories.Add(windowsDir);
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"')));

        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var extension in extensions)
        {
            try
            {
                var candidate = Path.Combine(directory, fileName + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }

        return null;
    }

    /// <summary>
    /// 用系统默认浏览器打开 URL。
    /// </summary>
    private void LaunchUrl(ToolDefinition tool)
    {
        var url = tool.Path;
        // 如果是相对路径的 HTML 文件,转为 file:// URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme == "file")
        {
            url = ResolvePath(url, tool.SourceFile);
            if (File.Exists(url))
                url = "file:///" + url.Replace('\\', '/');
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// 动态加载并执行插件 DLL。
    /// </summary>
    [RequiresUnreferencedCode("插件系统依赖运行时反射加载 DLL，无法被裁剪器静态分析")]
    private Task LaunchPluginAsync(ToolDefinition tool)
    {
        var dllPath = ResolvePath(tool.Path, tool.SourceFile);
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"插件 DLL 未找到: {dllPath}");

        var assembly = Assembly.LoadFrom(dllPath);
        var pluginType = assembly.GetExportedTypes()
            .FirstOrDefault(t => typeof(IToolPlugin).IsAssignableFrom(t) && !t.IsAbstract);

        if (pluginType == null)
            throw new InvalidOperationException($"DLL 中未找到实现 IToolPlugin 的类型: {dllPath}");

        var plugin = (IToolPlugin)Activator.CreateInstance(pluginType)!;
        var context = new ToolContextImpl(
            _ownerHandle,
            Path.GetDirectoryName(dllPath) ?? "",
            tool.Arguments);

        // 插件可能弹出自己的窗口,在 UI 线程执行
        plugin.Launch(context);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行 Shell 命令，命令行窗口保持打开以便查看输出。
    /// </summary>
    private Task LaunchCommandAsync(ToolDefinition tool)
    {
        var workDir = !string.IsNullOrWhiteSpace(tool.WorkingDirectory)
            ? ResolvePath(tool.WorkingDirectory, tool.SourceFile)
            : !string.IsNullOrWhiteSpace(tool.SourceFile)
                ? Path.GetDirectoryName(Path.GetFullPath(tool.SourceFile)) ?? AppContext.BaseDirectory
                : AppContext.BaseDirectory;

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k {tool.Path}",
            WorkingDirectory = workDir,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 打开编译输出窗口执行 dotnet publish。
    /// </summary>
    private void LaunchBuild(ToolDefinition tool)
    {
        // 在 UI 线程打开编译窗口
        var window = new BuildOutputWindow(_buildService, tool);
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }

    /// <summary>打开随主程序发布的原生工具窗口。</summary>
    private static void LaunchBuiltIn(ToolDefinition tool)
    {
        // Path.GetFileName 兼容旧版本 ConfigLoader 已经错误展开成绝对路径的配置。
        var builtInId = Path.GetFileName(tool.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Window window = builtInId.ToLowerInvariant() switch
        {
            "software-inventory" => new SoftwareInventoryWindow(),
            "ai-manager" => new AiManagerWindow(),
            _ => throw new NotSupportedException($"未知的内置工具: {builtInId}")
        };
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }

    // ====== 辅助方法 ======

    private static string ResolvePath(string path, string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (System.IO.Path.IsPathRooted(path)) return path;
        if (sourceFile != null)
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(sourceFile) ?? "", path));
        return System.IO.Path.GetFullPath(path);
    }

    private static string? ResolveArgs(string? args, ToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(args)) return args;
        return args
            .Replace("${dir}", Path.GetDirectoryName(tool.SourceFile) ?? "")
            .Replace("${self}", tool.SourceFile ?? "");
    }

    /// <summary>
    /// IToolContext 实现。
    /// </summary>
    private sealed class ToolContextImpl : IToolContext
    {
        public IntPtr OwnerHandle { get; }
        public string ToolDirectory { get; }
        public IReadOnlyDictionary<string, string> Arguments { get; }

        public ToolContextImpl(IntPtr ownerHandle, string toolDir, Dictionary<string, string> args)
        {
            OwnerHandle = ownerHandle;
            ToolDirectory = toolDir;
            Arguments = args;
        }

        public void Log(string message)
        {
            Debug.WriteLine($"[Plugin] {message}");
        }
    }
}
