using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>
/// ToolKind 转 Emoji 图标。
/// </summary>
public class KindToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ToolKind kind)
        {
            return kind switch
            {
                ToolKind.Executable => "⚡",
                ToolKind.Url => "🌐",
                ToolKind.Plugin => "🧩",
                ToolKind.Command => "⌨️",
                ToolKind.Build => "🔨",
                ToolKind.BuiltIn => "📋",
                _ => "🔧"
            };
        }
        return "🔧";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// ToolKind 转标签文本(EXE / URL / DLL / CMD)。
/// </summary>
public class KindToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ToolKind kind)
        {
            return kind switch
            {
                ToolKind.Executable => "EXE",
                ToolKind.Url => "URL",
                ToolKind.Plugin => "DLL",
                ToolKind.Command => "CMD",
                ToolKind.Build => "Build",
                ToolKind.BuiltIn => "内置",
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 分类名称转颜色画刷。
/// </summary>
public class CategoryToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var category = value as string ?? "";
        var color = category switch
        {
            var c when c.StartsWith("开发") => Color.FromRgb(0x89, 0xB4, 0xFA),
            var c when c.StartsWith("工具") => Color.FromRgb(0xA6, 0xE3, 0xA1),
            var c when c.StartsWith("网络") => Color.FromRgb(0x94, 0xE2, 0xD5),
            var c when c.StartsWith("图形") => Color.FromRgb(0xF5, 0xC2, 0xE7),
            var c when c.StartsWith("系统") => Color.FromRgb(0xFA, 0xE8, 0xB4),
            var c when c.StartsWith("娱乐") => Color.FromRgb(0xCB, 0xB6, 0xFF),
            _ => Color.FromRgb(0xE6, 0xE9, 0xF0)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 分类名称转对应的 Emoji 图标。
/// </summary>
public class CategoryToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        return name switch
        {
            var c when c.StartsWith("开发") => "💻",
            var c when c.StartsWith("工具") => "🔧",
            var c when c.StartsWith("网络") => "🌐",
            var c when c.StartsWith("图形") => "🎨",
            var c when c.StartsWith("音频") => "🎵",
            var c when c.StartsWith("文档") => "📄",
            var c when c.StartsWith("游戏") => "🎮",
            var c when c.StartsWith("系统") => "⚙️",
            var c when c.StartsWith("安全") => "🔒",
            var c when c.StartsWith("数据") => "📊",
            _ => "📁"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 批量模式开关转 CheckBox 可见性。
/// 通过 ListBox.Tag == "Batch" 控制。
/// </summary>
public class BatchModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s == "Batch")
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 取反的 BooleanToVisibility: true→Visible, false→Collapsed。
/// WPF 内置的 BooleanToVisibilityConverter 是 true→Visible, false→Collapsed，与我们的需求相反。
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
