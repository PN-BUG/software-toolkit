using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SoftwareToolkit.Models;
using SoftwareToolkit.Sdk;

namespace SoftwareToolkit.Services;

/// <summary>
/// 配置加载器: 读取 tools.json + 扫描 tools/ 目录下的 manifest.json。
/// </summary>
public sealed class ConfigLoader
{
    private readonly string _baseDir;
    private readonly string _userStateDir;

    public ConfigLoader(string? baseDir = null)
    {
        _baseDir = baseDir ?? AppContext.BaseDirectory;
        _userStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareToolkit");
        Directory.CreateDirectory(_userStateDir);
    }

    /// <summary>
    /// 加载所有工具定义。
    /// </summary>
    public List<ToolDefinition> LoadAllTools()
    {
        var tools = new ConcurrentBag<ToolDefinition>();

        // 1. 读取主配置 tools.json
        var mainConfigPath = Path.Combine(_baseDir, "tools.json");
        if (File.Exists(mainConfigPath))
        {
            try
            {
                var json = File.ReadAllText(mainConfigPath);
                var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolsConfig);
                if (config?.Tools != null)
                {
                    foreach (var t in config.Tools)
                    {
                        t.SourceFile = mainConfigPath;
                        tools.Add(t);
                    }
                }

                // 2. 扫描子目录
                if (config?.ScanDirectories != null)
                {
                    foreach (var dir in config.ScanDirectories)
                    {
                        var fullDir = Path.Combine(_baseDir, dir);
                        if (Directory.Exists(fullDir))
                            ScanDirectory(fullDir, tools);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigLoader] 读取 {mainConfigPath} 失败: {ex.Message}");
            }
        }
        else
        {
            // 没有 tools.json 也扫描 tools/ 目录
            var defaultDir = Path.Combine(_baseDir, "tools");
            if (Directory.Exists(defaultDir))
                ScanDirectory(defaultDir, tools);
        }

        // 3. 合并用户状态
        var state = LoadUserState();
        var result = tools.ToList();
        foreach (var t in result)
        {
            var id = GetEffectiveId(t);
            t.IsPinned = state.Pinned.Contains(id);
            if (state.Usage.TryGetValue(id, out var usage))
            {
                t.UsageCount = usage.Count;
                t.LastUsed = usage.LastUsed;
            }
        }

        return result;
    }

    /// <summary>
    /// 扫描 tools/ 下的子目录,每个子目录查找 manifest.json。
    /// </summary>
    private void ScanDirectory(string dir, ConcurrentBag<ToolDefinition> tools)
    {
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            var manifestPath = Path.Combine(subDir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var json = File.ReadAllText(manifestPath);
                var tool = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolDefinition);
                if (tool == null) continue;

                tool.SourceFile = manifestPath;

                // 自动生成 ID
                if (string.IsNullOrWhiteSpace(tool.Id))
                    tool.Id = Path.GetFileName(subDir);

                // BuiltIn 的 path 是逻辑标识（如 ai-manager），不是文件路径。
                if (tool.Kind != ToolKind.BuiltIn &&
                    !string.IsNullOrWhiteSpace(tool.Path) && !System.IO.Path.IsPathRooted(tool.Path))
                    tool.Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(subDir, tool.Path));

                // 自动补全图标路径
                if (!string.IsNullOrWhiteSpace(tool.Icon) && !System.IO.Path.IsPathRooted(tool.Icon))
                    tool.Icon = System.IO.Path.GetFullPath(System.IO.Path.Combine(subDir, tool.Icon));

                tools.Add(tool);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ConfigLoader] 读取 {manifestPath} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取工具的有效 ID。
    /// </summary>
    public static string GetEffectiveId(ToolDefinition tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.Id)) return tool.Id;
        // 用 name+path 生成稳定 ID
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{tool.Name}|{tool.Path}")))[..12];
    }

    // ====== 用户状态 ======

    public UserState LoadUserState()
    {
        var path = Path.Combine(_userStateDir, "userstate.json");
        if (!File.Exists(path)) return new UserState();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.UserState) ?? new UserState();
        }
        catch { return new UserState(); }
    }

    public void SaveUserState(UserState state)
    {
        var path = Path.Combine(_userStateDir, "userstate.json");
        var json = JsonSerializer.Serialize(state, AppJsonContext.Default.UserState);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 记录工具使用。
    /// </summary>
    public void RecordUsage(string toolId)
    {
        var state = LoadUserState();
        if (!state.Usage.TryGetValue(toolId, out var usage))
        {
            usage = new ToolUsage();
            state.Usage[toolId] = usage;
        }
        usage.Count++;
        usage.LastUsed = DateTime.Now;
        SaveUserState(state);
    }

    /// <summary>
    /// 切换置顶状态。
    /// </summary>
    public bool TogglePin(string toolId)
    {
        var state = LoadUserState();
        if (state.Pinned.Contains(toolId))
            state.Pinned.Remove(toolId);
        else
            state.Pinned.Add(toolId);
        SaveUserState(state);
        return state.Pinned.Contains(toolId);
    }

    // ====== 工具保存/新增 ======

    /// <summary>
    /// 保存已有工具的修改到其 SourceFile。
    /// </summary>
    public void SaveTool(ToolDefinition tool)
    {
        if (string.IsNullOrEmpty(tool.SourceFile) || !File.Exists(tool.SourceFile))
        {
            // 没有 SourceFile 则按 tools.json 方式保存
            AddTool(tool);
            return;
        }

        var ext = Path.GetExtension(tool.SourceFile).ToLowerInvariant();
        if (ext == ".json" && tool.SourceFile.EndsWith("tools.json", StringComparison.OrdinalIgnoreCase))
        {
            // tools.json 格式：找到对应条目替换
            var json = File.ReadAllText(tool.SourceFile);
            var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolsConfig);
            if (config?.Tools != null)
            {
                var id = GetEffectiveId(tool);
                var idx = config.Tools.FindIndex(t =>
                    GetEffectiveId(t) == id ||
                    (!string.IsNullOrWhiteSpace(t.Id) && t.Id == tool.Id));
                if (idx >= 0)
                {
                    // 保留 Id
                    tool.Id = config.Tools[idx].Id;
                    config.Tools[idx] = tool;
                }
                else
                {
                    config.Tools.Add(tool);
                }

                var newJson = JsonSerializer.Serialize(config, AppJsonContext.Default.ToolsConfig);
                File.WriteAllText(tool.SourceFile, newJson);
            }
        }
        else if (ext == ".json")
        {
            // manifest.json 格式
            var newJson = SerializeToolDefinition(tool);
            File.WriteAllText(tool.SourceFile, newJson);
        }
    }

    /// <summary>
    /// 添加新工具到 tools.json。
    /// </summary>
    public void AddTool(ToolDefinition tool)
    {
        var mainConfigPath = Path.Combine(_baseDir, "tools.json");
        ToolsConfig config;
        if (File.Exists(mainConfigPath))
        {
            var json = File.ReadAllText(mainConfigPath);
            config = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolsConfig) ?? new ToolsConfig();
        }
        else
        {
            config = new ToolsConfig();
        }

        tool.SourceFile = mainConfigPath;
        config.Tools ??= new List<ToolDefinition>();
        config.Tools.Add(tool);

        var newJson = JsonSerializer.Serialize(config, AppJsonContext.Default.ToolsConfig);
        File.WriteAllText(mainConfigPath, newJson);
    }

    /// <summary>
    /// 拖入文件场景：在 tools/ 下创建独立目录和 manifest.json。
    /// </summary>
    public void AddToolFromDrop(ToolDefinition tool, string dropFilePath)
    {
        var toolsDir = Path.Combine(_baseDir, "tools");
        Directory.CreateDirectory(toolsDir);

        var dirName = SanitizeDirName(Path.GetFileNameWithoutExtension(dropFilePath)
            ?? Guid.NewGuid().ToString("N")[..8]);
        var toolDir = Path.Combine(toolsDir, dirName);
        Directory.CreateDirectory(toolDir);

        tool.SourceFile = Path.Combine(toolDir, "manifest.json");
        tool.Id = dirName;

        var newJson = SerializeToolDefinition(tool);
        File.WriteAllText(tool.SourceFile, newJson);
    }

    /// <summary>
    /// 保存工具排序（userstate）。
    /// </summary>
    public void SaveToolOrder(List<string> toolIds)
    {
        var state = LoadUserState();
        state.ToolOrder = toolIds;
        SaveUserState(state);
    }

    /// <summary>
    /// 加载自定义排序，若没有则返回 null。
    /// </summary>
    public List<string>? LoadToolOrder()
    {
        var state = LoadUserState();
        return state.ToolOrder?.Count > 0 ? state.ToolOrder : null;
    }

    private static string SerializeToolDefinition(ToolDefinition tool)
    {
        return JsonSerializer.Serialize(tool, AppJsonContext.Default.ToolDefinition);
    }

    private static string SanitizeDirName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "tool" : sanitized;
    }

    /// <summary>
    /// 删除工具定义。
    /// </summary>
    public void DeleteTool(ToolDefinition tool)
    {
        if (string.IsNullOrEmpty(tool.SourceFile) || !File.Exists(tool.SourceFile))
            return;

        var ext = Path.GetExtension(tool.SourceFile).ToLowerInvariant();
        if (ext == ".json" && tool.SourceFile.EndsWith("tools.json", StringComparison.OrdinalIgnoreCase))
        {
            // 从 tools.json 中移除
            var json = File.ReadAllText(tool.SourceFile);
            var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.ToolsConfig);
            if (config?.Tools != null)
            {
                var id = GetEffectiveId(tool);
                config.Tools.RemoveAll(t =>
                    GetEffectiveId(t) == id ||
                    (!string.IsNullOrWhiteSpace(t.Id) && t.Id == tool.Id));

                var newJson = JsonSerializer.Serialize(config, AppJsonContext.Default.ToolsConfig);
                File.WriteAllText(tool.SourceFile, newJson);
            }
        }
        else if (ext == ".json")
        {
            // manifest.json 格式：删除文件及其目录
            var dir = Path.GetDirectoryName(tool.SourceFile);
            try
            {
                File.Delete(tool.SourceFile);
                if (dir != null && Directory.Exists(dir) && !Directory.GetFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { }
        }
    }
}
