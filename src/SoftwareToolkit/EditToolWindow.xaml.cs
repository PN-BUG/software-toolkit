using System.IO;
using System.Windows;
using System.Windows.Controls;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class EditToolWindow : Window
{
    private readonly ConfigLoader _configLoader;
    private readonly ToolDefinition? _existingTool;
    private readonly string? _dropFilePath;

    /// <summary>保存成功后返回 true</summary>
    public bool Saved { get; private set; }

    /// <summary>拖入文件新建工具</summary>
    public EditToolWindow(ConfigLoader configLoader, string dropFilePath)
    {
        InitializeComponent();
        _configLoader = configLoader;
        _dropFilePath = dropFilePath;
        _existingTool = null;

        TitleText.Text = "➕ 添加工具 - 从文件拖入";
        SaveBtn.Content = "添加";
        BrowseBtn.Visibility = Visibility.Collapsed;

        PrefillFromFile(dropFilePath);
        HookLocalization();
    }

    /// <summary>编辑已有工具</summary>
    public EditToolWindow(ConfigLoader configLoader, ToolDefinition tool)
    {
        InitializeComponent();
        _configLoader = configLoader;
        _existingTool = tool;
        _dropFilePath = null;

        TitleText.Text = "✏️ 编辑工具";
        SaveBtn.Content = "保存";
        BrowseBtn.Visibility = tool.Kind == ToolKind.Executable ? Visibility.Visible : Visibility.Collapsed;

        LoadTool(tool);
        HookLocalization();
    }

    /// <summary>新建空白工具</summary>
    public EditToolWindow(ConfigLoader configLoader)
    {
        InitializeComponent();
        _configLoader = configLoader;
        _existingTool = null;
        _dropFilePath = null;

        TitleText.Text = "➕ 添加工具";
        SaveBtn.Content = "添加";

        NameBox.Text = "";
        DescBox.Text = "";
        CategoryBox.Text = "未分类";
        KindCombo.SelectedIndex = 0;
        PathBox.Text = "";
        ArgsBox.Text = "";
        TagsBox.Text = "";
        HookLocalization();
    }

    private void HookLocalization()
    {
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        LocalizationService.Apply(this);
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e) => LocalizationService.Apply(this);

    private void PrefillFromFile(string filePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            NameBox.Text = name;
            DescBox.Text = "";
            CategoryBox.Text = "未分类";
            PathBox.Text = filePath;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".exe":
                case ".bat":
                case ".cmd":
                case ".ps1":
                case ".py":
                case ".msi":
                    KindCombo.SelectedIndex = 0; // Executable
                    break;
                case ".url":
                case ".html":
                case ".htm":
                    KindCombo.SelectedIndex = 1; // Url
                    break;
                case ".dll":
                    KindCombo.SelectedIndex = 3; // Plugin
                    break;
                case ".csproj":
                case ".sln":
                    KindCombo.SelectedIndex = 4; // Build
                    PathBox.Text = filePath;
                    ShowBuildConfig(true);
                    break;
                default:
                    KindCombo.SelectedIndex = 0;
                    break;
            }

            TagsBox.Text = "";
            BrowseBtn.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void LoadTool(ToolDefinition tool)
    {
        NameBox.Text = tool.Name;
        DescBox.Text = tool.Description ?? "";
        CategoryBox.Text = tool.Category;
        PathBox.Text = tool.Path;
        ArgsBox.Text = tool.Args ?? "";
        TagsBox.Text = string.Join(", ", tool.Tags);
        RunAsAdminChk.IsChecked = tool.RunAsAdmin;

        KindCombo.SelectedIndex = tool.Kind switch
        {
            ToolKind.Executable => 0,
            ToolKind.Url => 1,
            ToolKind.Command => 2,
            ToolKind.Plugin => 3,
            ToolKind.Build => 4,
            ToolKind.BuiltIn => 5,
            _ => 0
        };

        // 加载编译配置
        if (tool.Kind == ToolKind.Build)
        {
            BuildConfigBox.Text = tool.Arguments.GetValueOrDefault("config", "Release");
            BuildOutputBox.Text = tool.Arguments.GetValueOrDefault("output", "");
            ShowBuildConfig(true);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var isBuild = (KindCombo.SelectedItem as ComboBoxItem)?.Tag as string == "Build";
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = isBuild ? "选择项目文件 (.csproj / .sln)" : "选择可执行文件",
            Filter = isBuild
                ? "项目文件|*.csproj;*.sln|所有文件|*.*"
                : "可执行文件|*.exe;*.bat;*.cmd;*.ps1;*.py;*.msi|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 验证
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WpfMessageBox.Show("请输入工具名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        var path = PathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            WpfMessageBox.Show("请输入路径或 URL。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            PathBox.Focus();
            return;
        }

        var kind = (KindCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var toolKind = kind switch
        {
            "Url" => ToolKind.Url,
            "Command" => ToolKind.Command,
            "Plugin" => ToolKind.Plugin,
            "Build" => ToolKind.Build,
            "BuiltIn" => ToolKind.BuiltIn,
            _ => ToolKind.Executable
        };

        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(TagsBox.Text))
        {
            tags.AddRange(TagsBox.Text.Split(',', '，')
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        if (_existingTool != null)
        {
            // 编辑已有工具
            _existingTool.Name = name;
            _existingTool.Description = DescBox.Text?.Trim();
            _existingTool.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "未分类" : CategoryBox.Text.Trim();
            _existingTool.Kind = toolKind;
            _existingTool.Path = path;
            _existingTool.Args = ArgsBox.Text?.Trim();
            _existingTool.Tags = tags;
            _existingTool.RunAsAdmin = RunAsAdminChk.IsChecked ?? false;

            _configLoader.SaveTool(_existingTool);
        }
        else
        {
            // 新建工具
            var tool = new ToolDefinition
            {
                Name = name,
                Description = DescBox.Text?.Trim(),
                Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "未分类" : CategoryBox.Text.Trim(),
                Kind = toolKind,
                Path = path,
                Args = ArgsBox.Text?.Trim(),
                Tags = tags,
                RunAsAdmin = RunAsAdminChk.IsChecked ?? false
            };

            if (_dropFilePath != null)
            {
                // 拖入文件场景：保存到 tools 目录
                _configLoader.AddToolFromDrop(tool, _dropFilePath);
            }
            else
            {
                _configLoader.AddTool(tool);
            }
        }

        Saved = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void KindCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var tag = (KindCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        var isBuild = tag == "Build";
        ShowBuildConfig(isBuild);

        // 更新路径标签提示
        if (isBuild)
            ArgsLabel.Text = "额外 dotnet publish 参数（可选）";
        else
            ArgsLabel.Text = "命令行参数（可选）";

        // 更新浏览按钮可见性
        BrowseBtn.Visibility = Visibility.Visible;
    }

    private void ShowBuildConfig(bool show)
    {
        BuildConfigPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}
