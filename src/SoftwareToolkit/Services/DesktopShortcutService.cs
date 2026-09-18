using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>
/// Creates Windows desktop shortcuts that preserve a tool's launch arguments,
/// working directory, icon, and elevation preference.
/// </summary>
public static class DesktopShortcutService
{
    private const uint RunAsUserFlag = 0x00002000;

    public static bool CanCreate(ToolDefinition tool, string? resolvedLocation)
    {
        if (tool.Kind == ToolKind.Command)
            return !string.IsNullOrWhiteSpace(tool.Path);

        if (tool.Kind == ToolKind.Url && IsExternalUri(tool.Path))
            return true;

        return tool.Kind is ToolKind.Executable or ToolKind.Url && resolvedLocation != null;
    }

    public static string Create(ToolDefinition tool, string? resolvedLocation, string? desktopDirectory = null)
    {
        if (!CanCreate(tool, resolvedLocation))
            throw new NotSupportedException("该工具类型无法创建可独立启动的桌面快捷方式。");

        desktopDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
            throw new DirectoryNotFoundException("无法找到当前用户的桌面目录。");
        Directory.CreateDirectory(desktopDirectory);

        var shortcutName = SanitizeFileName(tool.Name) + " - SoftwareToolkit";
        if (tool.Kind == ToolKind.Url && IsExternalUri(tool.Path))
        {
            var shortcutPath = Path.Combine(desktopDirectory, shortcutName + ".url");
            CreateInternetShortcut(shortcutPath, tool);
            return shortcutPath;
        }

        var linkPath = Path.Combine(desktopDirectory, shortcutName + ".lnk");
        var targetPath = tool.Kind == ToolKind.Command
            ? Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : resolvedLocation!;
        var arguments = tool.Kind switch
        {
            ToolKind.Command => $"/k {tool.Path}",
            ToolKind.Executable => ResolveArguments(tool.Args, tool),
            _ => null
        };
        var workingDirectory = ResolveWorkingDirectory(tool, targetPath);
        var iconPath = ResolveIconPath(tool.Icon, tool.SourceFile) ?? targetPath;

        CreateShellLink(linkPath, targetPath, arguments, workingDirectory, tool.Description, iconPath, tool.RunAsAdmin);
        return linkPath;
    }

    private static bool IsExternalUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static void CreateInternetShortcut(string shortcutPath, ToolDefinition tool)
    {
        if (!Uri.TryCreate(tool.Path, UriKind.Absolute, out var uri) || uri.IsFile)
            throw new UriFormatException("工具链接不是有效的绝对 URL。");

        var lines = new List<string>
        {
            "[InternetShortcut]",
            $"URL={uri.AbsoluteUri}"
        };
        if (ResolveIconPath(tool.Icon, tool.SourceFile) is { } iconPath)
        {
            lines.Add($"IconFile={iconPath}");
            lines.Add("IconIndex=0");
        }
        File.WriteAllLines(shortcutPath, lines, new UTF8Encoding(false));
    }

    private static void CreateShellLink(
        string shortcutPath,
        string targetPath,
        string? arguments,
        string workingDirectory,
        string? description,
        string iconPath,
        bool runAsAdmin)
    {
        var shellLink = (IShellLinkW)(object)new ShellLink();
        try
        {
            shellLink.SetPath(targetPath);
            shellLink.SetArguments(arguments ?? string.Empty);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetDescription(Truncate(description ?? $"启动 {Path.GetFileNameWithoutExtension(shortcutPath)}", 1023));
            shellLink.SetIconLocation(iconPath, 0);
            shellLink.SetShowCmd(1);

            if (runAsAdmin && shellLink is IShellLinkDataList dataList)
            {
                Marshal.ThrowExceptionForHR(dataList.GetFlags(out var flags));
                Marshal.ThrowExceptionForHR(dataList.SetFlags(flags | RunAsUserFlag));
            }

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(shellLink)) Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static string ResolveWorkingDirectory(ToolDefinition tool, string targetPath)
    {
        if (!string.IsNullOrWhiteSpace(tool.WorkingDirectory))
            return ResolveRelativePath(tool.WorkingDirectory, tool.SourceFile);

        if (tool.Kind == ToolKind.Command)
            return Environment.CurrentDirectory;

        return Directory.Exists(targetPath)
            ? targetPath
            : Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory;
    }

    private static string? ResolveArguments(string? arguments, ToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return arguments;
        return arguments
            .Replace("${dir}", Path.GetDirectoryName(tool.SourceFile) ?? string.Empty, StringComparison.Ordinal)
            .Replace("${self}", tool.SourceFile ?? string.Empty, StringComparison.Ordinal);
    }

    private static string? ResolveIconPath(string? icon, string? sourceFile)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        try
        {
            var path = ResolveRelativePath(icon, sourceFile);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string ResolveRelativePath(string value, string? sourceFile)
    {
        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
        var baseDirectory = string.IsNullOrWhiteSpace(sourceFile)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Path.GetFullPath(sourceFile)) ?? AppContext.BaseDirectory;
        return Path.GetFullPath(value, baseDirectory);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "工具" : sanitized;
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maximumPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maximumPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximumPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkDataList
    {
        [PreserveSig] int AddDataBlock(IntPtr dataBlock);
        [PreserveSig] int CopyDataBlock(uint signature, out IntPtr dataBlock);
        [PreserveSig] int RemoveDataBlock(uint signature);
        [PreserveSig] int GetFlags(out uint flags);
        [PreserveSig] int SetFlags(uint flags);
    }
}
