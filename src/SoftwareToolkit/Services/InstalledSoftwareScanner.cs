using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>读取 Windows 卸载注册表与当前用户 Store/MSIX 包。</summary>
public sealed class InstalledSoftwareScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public async Task<List<InstalledSoftware>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = await Task.Run(ScanRegistry, cancellationToken);
        var storeApps = await ScanStoreAppsAsync(cancellationToken);
        results.AddRange(storeApps);

        var deduplicated = results
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => NormalizeIdentity(item.Name, item.Publisher), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(Score).First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        await EnrichWingetIdsAsync(deduplicated, cancellationToken);
        return deduplicated;
    }

    private static List<InstalledSoftware> ScanRegistry()
    {
        var results = new List<InstalledSoftware>();
        ReadRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry64, "本机 · 64 位", results);
        ReadRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry32, "本机 · 32 位", results);
        ReadRegistryHive(RegistryHive.CurrentUser, RegistryView.Registry64, "当前用户", results);
        ReadRegistryHive(RegistryHive.CurrentUser, RegistryView.Registry32, "当前用户 · 32 位", results);
        return results;
    }

    private static void ReadRegistryHive(
        RegistryHive hive,
        RegistryView view,
        string source,
        ICollection<InstalledSoftware> results)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall == null) return;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(subKeyName);
                    if (key == null || ReadInt(key, "SystemComponent") == 1) continue;
                    if (key.GetValue("ParentKeyName") != null) continue;

                    var name = ReadString(key, "DisplayName");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var publisher = ReadString(key, "Publisher");
                    var registryIdentity = $"{hive}|{view}|{subKeyName}";
                    results.Add(new InstalledSoftware
                    {
                        Id = StableId(registryIdentity),
                        Name = name.Trim(),
                        Version = ReadString(key, "DisplayVersion"),
                        Publisher = publisher,
                        InstallDate = FormatInstallDate(ReadString(key, "InstallDate")),
                        InstallLocation = ReadString(key, "InstallLocation"),
                        HomePageUrl = FirstValidHttpUrl(
                            ReadString(key, "URLInfoAbout"),
                            ReadString(key, "HelpLink")),
                        Source = source
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SoftwareInventory] 无法读取 {subKeyName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoftwareInventory] 无法读取 {hive}/{view}: {ex.Message}");
        }
    }

    private static async Task<List<InstalledSoftware>> ScanStoreAppsAsync(CancellationToken cancellationToken)
    {
        const string script = "$ProgressPreference='SilentlyContinue'; " +
            "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); Get-AppxPackage | " +
            "Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } | " +
            "Select-Object Name,PackageFamilyName,Version,Publisher,InstallLocation | ConvertTo-Json -Compress";

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            if (!await WaitForExitAsync(process, TimeSpan.FromSeconds(20), cancellationToken)) return new();
            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return new();

            using var document = JsonDocument.Parse(output);
            var elements = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { document.RootElement };
            return elements.Select(element =>
            {
                var family = JsonString(element, "PackageFamilyName");
                var name = JsonString(element, "Name") ?? family ?? "Store 应用";
                return new InstalledSoftware
                {
                    Id = StableId("appx|" + (family ?? name)),
                    Name = name,
                    Version = JsonString(element, "Version"),
                    Publisher = FriendlyStorePublisher(JsonString(element, "Publisher")),
                    InstallLocation = JsonString(element, "InstallLocation"),
                    Source = "Microsoft Store / MSIX",
                    PackageId = family
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoftwareInventory] Store 应用扫描失败: {ex.Message}");
            return new();
        }
    }

    /// <summary>用 WinGet 的本机列表为条目补全包 ID；失败不影响注册表扫描结果。</summary>
    private static async Task EnrichWingetIdsAsync(List<InstalledSoftware> items, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = "list --accept-source-agreements --disable-interactivity",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            if (!await WaitForExitAsync(process, TimeSpan.FromSeconds(30), cancellationToken)) return;
            _ = await errorTask;
            if (process.ExitCode != 0) return;

            var packages = ParseWingetList(await outputTask);
            foreach (var item in items.Where(item => string.IsNullOrWhiteSpace(item.PackageId)))
            {
                var normalizedName = NormalizeText(item.Name);
                var match = packages.FirstOrDefault(package =>
                    NormalizeText(package.Name) == normalizedName);
                if (string.IsNullOrWhiteSpace(match.Id))
                {
                    match = packages.FirstOrDefault(package =>
                        normalizedName.Length >= 5 &&
                        (NormalizeText(package.Name).Contains(normalizedName) ||
                         normalizedName.Contains(NormalizeText(package.Name))));
                }
                if (!string.IsNullOrWhiteSpace(match.Id)) item.PackageId = match.Id;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoftwareInventory] WinGet 识别不可用: {ex.Message}");
        }
    }

    public static List<(string Name, string Id)> ParseWingetList(string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n');
        var separatorIndex = Array.FindIndex(lines, line =>
            line.TrimStart().StartsWith("---", StringComparison.Ordinal));
        if (separatorIndex < 0) return new();

        var separator = lines[separatorIndex];
        var runs = System.Text.RegularExpressions.Regex.Matches(separator, "-+")
            .Select(match => (match.Index, match.Length)).ToList();
        if (runs.Count < 2) return new();

        string Slice(string line, int column)
        {
            var start = runs[column].Index;
            var end = column + 1 < runs.Count ? runs[column + 1].Index : line.Length;
            if (start >= line.Length) return string.Empty;
            return line[start..Math.Min(end, line.Length)].Trim();
        }

        var result = new List<(string Name, string Id)>();
        foreach (var line in lines.Skip(separatorIndex + 1))
        {
            var name = Slice(line, 0);
            var id = Slice(line, 1);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                result.Add((name, id));
        }
        return result;
    }

    public static string BuildAutomaticDownloadUrl(InstalledSoftware item)
    {
        if (IsHttpUrl(item.HomePageUrl)) return item.HomePageUrl!;
        if (!string.IsNullOrWhiteSpace(item.PackageId) && item.Source.Contains("Store", StringComparison.OrdinalIgnoreCase))
            return $"https://apps.microsoft.com/search?query={Uri.EscapeDataString(item.Name)}";
        if (!string.IsNullOrWhiteSpace(item.PackageId))
            return $"https://winstall.app/apps/{Uri.EscapeDataString(item.PackageId)}";
        return $"https://www.bing.com/search?q={Uri.EscapeDataString(item.Name + " " + (item.Publisher ?? "") + " 官方下载")}";
    }

    private static int Score(InstalledSoftware item) =>
        (string.IsNullOrWhiteSpace(item.Version) ? 0 : 2) +
        (string.IsNullOrWhiteSpace(item.Publisher) ? 0 : 2) +
        (string.IsNullOrWhiteSpace(item.HomePageUrl) ? 0 : 2) +
        (item.Source.Contains("64") ? 1 : 0);

    private static string NormalizeIdentity(string name, string? publisher) =>
        $"{NormalizeText(name)}|{NormalizeText(publisher ?? string.Empty)}";

    private static string NormalizeText(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string StableId(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];

    private static string? ReadString(RegistryKey key, string name) =>
        key.GetValue(name)?.ToString()?.Trim() is { Length: > 0 } value ? value : null;

    private static int ReadInt(RegistryKey key, string name) =>
        int.TryParse(key.GetValue(name)?.ToString(), out var value) ? value : 0;

    private static string? FirstValidHttpUrl(params string?[] values) => values.FirstOrDefault(IsHttpUrl);

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? FormatInstallDate(string? value) =>
        value?.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", null,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd")
            : value;

    private static string? JsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : null;

    private static string? FriendlyStorePublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return null;
        if (publisher.Contains("CN=Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
        return publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)
            ? publisher[3..].Split(',')[0]
            : publisher;
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutToken.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutToken.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            Debug.WriteLine($"[SoftwareInventory] 命令执行超过 {timeout.TotalSeconds:0} 秒，已跳过该扫描阶段。");
            return false;
        }
    }
}
