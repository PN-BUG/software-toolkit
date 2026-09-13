using System.Text.Json.Serialization;

namespace SoftwareToolkit.Models;

/// <summary>
/// 工具启动类型。
/// </summary>
public enum ToolKind
{
    /// <summary>外部可执行文件 (.exe / .bat / .cmd / .ps1 / .py 等,通过 Process.Start)</summary>
    Executable,

    /// <summary>URL/本地 HTML,用系统默认浏览器或内置 WebView 打开</summary>
    Url,

    /// <summary>插件 DLL,实现 SoftwareToolkit.Sdk.IToolPlugin</summary>
    Plugin,

    /// <summary>一组 Shell/CMD 命令</summary>
    Command,

    /// <summary>.NET 项目一键编译(dotnet publish),path 指向 .csproj/.sln</summary>
    Build,

    /// <summary>随主程序发布的原生内置工具，path 为内置工具标识</summary>
    BuiltIn
}

/// <summary>
/// 单个工具的配置项,从 tools.json 或 tools/&lt;name&gt;/manifest.json 反序列化得到。
/// </summary>
public sealed class ToolDefinition
{
    /// <summary>工具唯一 ID,缺省时由文件路径生成</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>显示名(必填)</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>详细描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>分类(可多级,如 "开发/编辑器")</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "未分类";

    /// <summary>用于过滤/搜索的标签</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>工具图标:支持相对路径(png/ico/jpg)或内置图标名(如 "tool", "code")</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>作者</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>版本</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>启动类型</summary>
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter<ToolKind>))]
    public ToolKind Kind { get; set; } = ToolKind.Executable;

    /// <summary>目标:exe 路径 / URL / DLL 路径 / 命令</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>命令行参数(支持占位符 ${dir} ${self})</summary>
    [JsonPropertyName("args")]
    public string? Args { get; set; }

    /// <summary>工作目录(留空则使用 path 所在目录)</summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>是否以管理员身份运行</summary>
    [JsonPropertyName("runAsAdmin")]
    public bool RunAsAdmin { get; set; }

    /// <summary>传给插件 DLL 的额外参数字典</summary>
    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; set; } = new();

    // ====== 运行时辅助字段(不序列化) ======

    /// <summary>定义文件来源的绝对路径,用于解析相对路径</summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>是否被用户置顶</summary>
    [JsonIgnore]
    public bool IsPinned { get; set; }

    /// <summary>最近一次启动时间</summary>
    [JsonIgnore]
    public DateTime? LastUsed { get; set; }

    /// <summary>累计启动次数</summary>
    [JsonIgnore]
    public int UsageCount { get; set; }
}

/// <summary>
/// 顶层 tools.json 结构。
/// </summary>
public sealed class ToolsConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>子目录扫描根(相对宿主可执行文件目录),为空则不扫描</summary>
    [JsonPropertyName("scanDirectories")]
    public List<string> ScanDirectories { get; set; } = new() { "tools" };

    /// <summary>主配置中直接声明的工具</summary>
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = new();
}

/// <summary>
/// 用户本地状态(置顶、使用次数等),保存到 AppData。
/// </summary>
public sealed class UserState
{
    [JsonPropertyName("pinned")]
    public HashSet<string> Pinned { get; set; } = new();

    [JsonPropertyName("usage")]
    public Dictionary<string, ToolUsage> Usage { get; set; } = new();

    /// <summary>全局热键(如 "Ctrl+Alt+T")</summary>
    [JsonPropertyName("hotKey")]
    public string HotKey { get; set; } = "Ctrl+Alt+T";

    /// <summary>关闭主窗口时最小化到托盘</summary>
    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>自定义工具排序（工具 ID 列表）</summary>
    [JsonPropertyName("toolOrder")]
    public List<string>? ToolOrder { get; set; }

    /// <summary>开机自动启动</summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    /// <summary>界面语言（zh-CN / en-US）</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh-CN";
}

public sealed class ToolUsage
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("lastUsed")]
    public DateTime? LastUsed { get; set; }
}
