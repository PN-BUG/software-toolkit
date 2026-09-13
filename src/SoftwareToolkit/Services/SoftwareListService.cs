using System.Net;
using System.Text;
using System.Text.Json;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

public sealed class SoftwareListService
{
    private readonly string _statePath;

    public SoftwareListService(string? stateDirectory = null)
    {
        var directory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareToolkit");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "software-list.json");
    }

    public SoftwareListState Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return new SoftwareListState();
            return JsonSerializer.Deserialize(File.ReadAllText(_statePath),
                       AppJsonContext.Default.SoftwareListState)
                   ?? new SoftwareListState();
        }
        catch
        {
            return new SoftwareListState();
        }
    }

    public void Save(string title, IEnumerable<InstalledSoftware> items)
    {
        var state = new SoftwareListState
        {
            Title = string.IsNullOrWhiteSpace(title) ? "我的软件清单" : title.Trim(),
            UpdatedAt = DateTime.Now,
            Items = items.Where(item => item.IsIncluded).Select(ToEntry).ToList()
        };
        File.WriteAllText(_statePath,
            JsonSerializer.Serialize(state, AppJsonContext.Default.SoftwareListState));
    }

    public static string BuildShareText(string title, IEnumerable<InstalledSoftware> items)
    {
        var selected = items.Where(item => item.IsIncluded).ToList();
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}").AppendLine();
        builder.AppendLine($"共 {selected.Count} 个软件 · 更新于 {DateTime.Now:yyyy-MM-dd}").AppendLine();
        foreach (var item in selected)
        {
            var version = string.IsNullOrWhiteSpace(item.Version) ? string.Empty : $" {item.Version}";
            var publisher = string.IsNullOrWhiteSpace(item.Publisher) ? string.Empty : $" — {item.Publisher}";
            builder.AppendLine($"- [{item.Name}{version}]({item.DownloadUrl}){publisher}");
        }
        return builder.ToString();
    }

    public static string BuildShareHtml(string title, IEnumerable<InstalledSoftware> items)
    {
        var selected = items.Where(item => item.IsIncluded).ToList();
        static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var cards = string.Join(Environment.NewLine, selected.Select(item => $"""
            <article class="app">
              <div class="app-main"><h2>{E(item.Name)}</h2><p>{E(item.PublisherDisplay)} · {E(item.VersionDisplay)}</p></div>
              <a class="download" href="{E(item.DownloadUrl)}" target="_blank" rel="noopener noreferrer">下载</a>
            </article>
            """));

        return $$$"""
            <!doctype html>
            <html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{{E(title)}}}</title>
            <style>
            :root{color-scheme:light;font-family:"Microsoft YaHei UI",system-ui,sans-serif;color:#23312b;background:#eef4f0}
            *{box-sizing:border-box}body{margin:0}.wrap{width:min(860px,calc(100% - 32px));margin:48px auto}
            header{margin-bottom:24px}h1{font-size:30px;margin:0 0 8px}header p{color:#68756f;margin:0}
            .app{display:flex;align-items:center;gap:20px;background:#fff;border:1px solid #dce7e1;border-radius:14px;padding:18px 20px;margin:10px 0;box-shadow:0 4px 16px #1d392408}
            .app-main{min-width:0;flex:1}.app h2{font-size:16px;margin:0 0 5px}.app p{font-size:13px;color:#75827c;margin:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
            .download{color:white;background:#167d6b;text-decoration:none;border-radius:9px;padding:9px 18px;font-weight:600}.download:hover{background:#106253}
            footer{font-size:12px;color:#89958f;margin-top:24px}@media(max-width:520px){.wrap{margin:24px auto}.app{padding:15px}.download{padding:8px 13px}}
            </style></head><body><main class="wrap"><header><h1>{{{E(title)}}}</h1><p>共 {{{selected.Count}}} 个软件 · 更新于 {{{DateTime.Now:yyyy-MM-dd}}}</p></header>
            {{{cards}}}
            <footer>由 SoftwareToolkit 软件清单生成。下载前请确认链接来源与软件许可。</footer></main></body></html>
            """;
    }

    private static SoftwareListEntry ToEntry(InstalledSoftware item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Version = item.Version,
        Publisher = item.Publisher,
        Source = item.Source,
        PackageId = item.PackageId,
        DownloadUrl = item.DownloadUrl,
        IsDownloadUrlAutomatic = item.IsDownloadUrlAutomatic
    };
}
