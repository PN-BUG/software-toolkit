namespace SoftwareToolkit.Sdk;

/// <summary>
/// 工具插件接口 - 第三方 DLL 工具实现此接口即可被宿主加载执行。
/// </summary>
public interface IToolPlugin
{
    /// <summary>工具唯一 ID(同一类的不同实例可不同)</summary>
    string Id { get; }

    /// <summary>显示名</summary>
    string DisplayName { get; }

    /// <summary>启动工具(可弹出自己的窗口,或执行某个动作)</summary>
    /// <param name="context">宿主提供的上下文(主窗口句柄、工作目录、参数等)</param>
    void Launch(IToolContext context);
}

/// <summary>
/// 宿主提供给插件的上下文。
/// </summary>
public interface IToolContext
{
    /// <summary>主窗口句柄(可作为 Owner)</summary>
    IntPtr OwnerHandle { get; }

    /// <summary>工具所在目录(用于读取自带资源)</summary>
    string ToolDirectory { get; }

    /// <summary>用户配置中传给该工具的参数</summary>
    IReadOnlyDictionary<string, string> Arguments { get; }

    /// <summary>把消息写到宿主的日志/状态栏</summary>
    void Log(string message);
}
