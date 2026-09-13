using System.Diagnostics;
using System.Text;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>
/// .NET 项目编译服务：执行 dotnet publish 并实时捕获输出。
/// </summary>
public sealed class BuildService
{
    /// <summary>
    /// 编译结果。
    /// </summary>
    public sealed class BuildResult
    {
        public bool Success { get; init; }
        public string Output { get; init; } = "";
        public int ExitCode { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string? ExePath { get; init; }
    }

    /// <summary>
    /// 执行编译。通过回调实时报告每一行输出。
    /// </summary>
    public async Task<BuildResult> BuildAsync(ToolDefinition tool, Action<string>? onOutputLine = null)
    {
        var projectPath = ResolveProjectPath(tool);
        if (!File.Exists(projectPath))
        {
            var msg = $"项目文件不存在: {projectPath}";
            onOutputLine?.Invoke($"[错误] {msg}");
            return new BuildResult { Success = false, Output = msg, ExitCode = -1 };
        }

        // 解析编译参数
        var config = tool.Arguments.GetValueOrDefault("config", "Release");
        var output = tool.Arguments.GetValueOrDefault("output", "");
        var extraArgs = tool.Args ?? "";

        // 如果未指定输出目录,使用默认 publish 子目录
        if (string.IsNullOrWhiteSpace(output))
        {
            var projDir = Path.GetDirectoryName(projectPath) ?? ".";
            output = Path.Combine(projDir, "publish");
        }

        // 构建 dotnet publish 命令参数
        var args = $"publish \"{projectPath}\" -c {config} -o \"{output}\" --nologo";
        if (!string.IsNullOrWhiteSpace(extraArgs))
            args += $" {extraArgs}";

        var sb = new StringBuilder();
        onOutputLine?.Invoke($"$ dotnet {args}");
        onOutputLine?.Invoke("");

        var sw = Stopwatch.StartNew();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    sb.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    sb.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            sw.Stop();

            var success = process.ExitCode == 0;
            var exePath = success ? FindPublishedExe(output, projectPath) : null;

            if (success)
                onOutputLine?.Invoke($"\n[完成] 编译成功，耗时 {sw.Elapsed.TotalSeconds:F1}s");
            else
                onOutputLine?.Invoke($"\n[失败] 编译失败，退出码 {process.ExitCode}");

            return new BuildResult
            {
                Success = success,
                Output = sb.ToString(),
                ExitCode = process.ExitCode,
                Elapsed = sw.Elapsed,
                ExePath = exePath
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = $"启动编译进程失败: {ex.Message}";
            onOutputLine?.Invoke($"\n[错误] {msg}");
            return new BuildResult
            {
                Success = false,
                Output = sb.ToString() + "\n" + msg,
                ExitCode = -1,
                Elapsed = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// 解析项目文件路径（.csproj / .sln）。
    /// </summary>
    private static string ResolveProjectPath(ToolDefinition tool)
    {
        var path = tool.Path;
        if (string.IsNullOrWhiteSpace(path)) return path;

        // 展开环境变量
        path = Environment.ExpandEnvironmentVariables(path);

        // 相对路径 → 基于 SourceFile 解析
        if (!Path.IsPathRooted(path) && tool.SourceFile != null)
        {
            var baseDir = Path.GetDirectoryName(tool.SourceFile) ?? ".";
            path = Path.GetFullPath(Path.Combine(baseDir, path));
        }

        return path;
    }

    /// <summary>
    /// 在输出目录中查找生成的 .exe 文件。
    /// </summary>
    private static string? FindPublishedExe(string outputDir, string projectPath)
    {
        if (!Directory.Exists(outputDir)) return null;

        var projName = Path.GetFileNameWithoutExtension(projectPath);
        var exePath = Path.Combine(outputDir, projName + ".exe");
        return File.Exists(exePath) ? exePath : null;
    }
}
