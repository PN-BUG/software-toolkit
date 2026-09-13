using System.Text.Json;
using System.Text.Json.Nodes;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

internal sealed class AiManagerService : IAsyncDisposable
{
    private readonly CodexAppServerClient _client = new();
    private readonly string _settingsPath;

    public AiManagerService()
    {
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareToolkit");
        _settingsPath = Path.Combine(stateDirectory, "ai-manager.json");
        Settings = LoadSettings();
    }

    public AiManagerSettings Settings { get; private set; }
    public AiManagerSnapshot? LastSnapshot { get; private set; }

    public async Task<AiManagerSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(cancellationToken);
        // Read the small, essential quota response first. A large thread/list response can
        // otherwise queue ahead of it in app-server and make the whole dashboard look offline.
        var rate = await _client.RequestAsync("account/rateLimits/read",
            cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(45));
        var usageTask = TryRequestAsync("account/usage/read", null, TimeSpan.FromSeconds(12), cancellationToken);
        var threadsTask = TryRequestAsync("thread/list", BuildThreadListParameters(), TimeSpan.FromSeconds(12), cancellationToken);
        var usage = await usageTask;
        var threads = await threadsTask;

        LastSnapshot = new AiManagerSnapshot
        {
            Limits = ParseLimits(rate),
            DailyUsage = usage is { } usageValue ? ParseDailyUsage(usageValue) : new(),
            LifetimeTokens = usage is { } usageSummary ? ParseLifetimeTokens(usageSummary) : null,
            Threads = threads is { } threadValue ? ParseThreads(threadValue) : new(),
            ResetCredits = ParseResetCredits(rate),
            CapturedAt = DateTimeOffset.Now
        };
        return LastSnapshot;
    }

    public async Task<AiFloatingSnapshot> RefreshFloatingAsync(CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(cancellationToken);
        var rate = await _client.RequestAsync("account/rateLimits/read",
            cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(45));

        // Start from the fast state database so a cold floating-window launch does not wait for
        // a full rollout scan. Each visible task is enriched below from thread/read and its rollout.
        var threadsTask = TryRequestAsync("thread/list", BuildLatestThreadParameters(),
            TimeSpan.FromSeconds(10), cancellationToken);
        var modelsTask = TryRequestAsync("model/list", new JsonObject
        {
            ["limit"] = 100,
            ["includeHidden"] = true
        }, TimeSpan.FromSeconds(10), cancellationToken);
        var threadsRoot = await threadsTask;
        var modelsRoot = await modelsTask;

        var threads = threadsRoot is { } threadValue ? ParseThreads(threadValue) : new();
        var candidates = threads.Where(IsUsefulFloatingTask)
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.UpdatedAt)
            .Take(3)
            .ToList();
        var visibleTasks = candidates.Count == 0
            ? new List<AiThreadSummary>()
            : (await Task.WhenAll(candidates.Select(item => EnrichFromLatestTurnAsync(item, cancellationToken))))
                .OrderByDescending(item => item.IsActive)
                .ThenByDescending(item => item.UpdatedAt)
                .ToList();
        var focus = visibleTasks.FirstOrDefault();

        var model = ResolveModel(modelsRoot, focus?.ModelName);
        var limits = ParseLimits(rate);
        var tightest = limits.OrderBy(item => item.RemainingPercent).FirstOrDefault();
        return new AiFloatingSnapshot
        {
            Tasks = visibleTasks,
            TaskName = focus?.Title ?? "暂无最近任务",
            TaskStatus = focus?.Status ?? "notLoaded",
            TaskStatusText = focus?.StatusText ?? "等待任务",
            ModelName = model.DisplayName ?? focus?.ModelName ?? "模型未知",
            ReasoningEffort = focus?.ReasoningEffort ?? model.ReasoningEffort,
            RemainingPercent = tightest?.RemainingPercent,
            QuotaWindow = tightest is null ? "额度窗口未知" : $"{tightest.Name} · {tightest.WindowName}",
            ResetsAt = tightest?.ResetsAt,
            CapturedAt = DateTimeOffset.Now
        };
    }

    private static bool IsUsefulFloatingTask(AiThreadSummary thread) =>
        !string.IsNullOrWhiteSpace(thread.Id) &&
        !thread.Title.StartsWith("The following is the Codex agent history", StringComparison.OrdinalIgnoreCase);

    private async Task<JsonElement?> TryRequestAsync(string method, JsonNode? parameters, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try { return await _client.RequestAsync(method, parameters, cancellationToken, timeout); }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"[AiManager] Optional request {method} failed: {ex.Message}");
            return null;
        }
    }

    public void SaveSettings(AiManagerSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath,
            JsonSerializer.Serialize(settings, AppJsonContext.Default.AiManagerSettings));
        Settings = settings;
    }

    public AiForecast BuildForecast(AiManagerSnapshot snapshot)
    {
        var recent = snapshot.DailyUsage
            .Where(item => item.Date >= DateOnly.FromDateTime(DateTime.Today.AddDays(-6)))
            .ToList();
        var dailyTokens = recent.Sum(item => (double)item.Tokens) / 7d;
        var main = snapshot.MainLimit;
        if (main?.ResetsAt is null || main.WindowDurationMinutes <= 0 || main.UsedPercent <= 0)
            return new AiForecast(dailyTokens, 0, null, false, "积累更多用量后可预测耗尽时间");

        var start = main.ResetsAt.Value.AddMinutes(-main.WindowDurationMinutes);
        var elapsedDays = Math.Max((snapshot.CapturedAt - start).TotalDays, 1d / 24d);
        var dailyPercent = main.UsedPercent / elapsedDays;
        if (dailyPercent <= 0.001)
            return new AiForecast(dailyTokens, dailyPercent, null, false, "当前消耗很低，本周期预计不会耗尽");

        var exhaustion = snapshot.CapturedAt.AddDays(main.RemainingPercent / dailyPercent);
        var beforeReset = exhaustion < main.ResetsAt.Value;
        var summary = beforeReset
            ? $"按当前速度，预计 {exhaustion.LocalDateTime:M月d日 HH:mm} 触顶"
            : $"按当前速度，本周期结束时约使用 {Math.Min(999, main.UsedPercent + dailyPercent * (main.ResetsAt.Value - snapshot.CapturedAt).TotalDays):0}%";
        return new AiForecast(dailyTokens, dailyPercent, exhaustion, beforeReset, summary);
    }

    public async Task<int> PauseActiveThreadsAsync(CancellationToken cancellationToken = default)
    {
        if (!_client.IsConnected) await _client.ConnectAsync(cancellationToken);
        var list = await _client.RequestAsync("thread/list", BuildThreadListParameters(), cancellationToken);
        var active = ParseThreads(list).Where(item => item.IsActive).ToList();
        var interrupted = 0;

        foreach (var thread in active)
        {
            try
            {
                var detail = await _client.RequestAsync("thread/read", new JsonObject
                {
                    ["threadId"] = thread.Id,
                    ["includeTurns"] = true
                }, cancellationToken);
                if (!detail.TryGetProperty("thread", out var threadElement) ||
                    !threadElement.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
                    continue;

                string? turnId = null;
                foreach (var turn in turns.EnumerateArray().Reverse())
                {
                    if (!turn.TryGetProperty("status", out var status) ||
                        !string.Equals(status.GetString(), "inProgress", StringComparison.OrdinalIgnoreCase)) continue;
                    if (turn.TryGetProperty("id", out var id)) turnId = id.GetString();
                    break;
                }
                if (string.IsNullOrWhiteSpace(turnId)) continue;
                await _client.RequestAsync("turn/interrupt", new JsonObject
                {
                    ["threadId"] = thread.Id,
                    ["turnId"] = turnId
                }, cancellationToken);
                interrupted++;
            }
            catch (InvalidOperationException)
            {
                // The task may have completed between discovery and interruption.
            }
        }
        return interrupted;
    }

    private AiManagerSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new AiManagerSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(_settingsPath), AppJsonContext.Default.AiManagerSettings)
                   ?? new AiManagerSettings();
        }
        catch
        {
            return new AiManagerSettings();
        }
    }

    private static JsonObject BuildThreadListParameters(int limit = 12, bool interactiveOnly = false)
    {
        var sources = new JsonArray();
        var sourceKinds = interactiveOnly
            ? new[] { "cli", "vscode", "exec", "appServer" }
            : new[]
            {
                "cli", "vscode", "exec", "appServer", "subAgent", "subAgentReview",
                "subAgentCompact", "subAgentThreadSpawn", "subAgentOther", "unknown"
            };
        foreach (var value in sourceKinds)
            sources.Add(value);
        return new JsonObject
        {
            ["limit"] = limit,
            ["sortKey"] = "updated_at",
            ["sortDirection"] = "desc",
            ["sourceKinds"] = sources,
            ["useStateDbOnly"] = true
        };
    }

    private static JsonObject BuildLatestThreadParameters() => new()
    {
        ["limit"] = 8,
        ["sortKey"] = "updated_at",
        ["sortDirection"] = "desc",
        ["useStateDbOnly"] = true
    };

    private static List<AiLimitWindow> ParseLimits(JsonElement root)
    {
        var result = new List<AiLimitWindow>();
        if (root.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in buckets.EnumerateObject()) ParseBucket(bucket.Name, bucket.Value, result);
        }
        else if (root.TryGetProperty("rateLimits", out var single) && single.ValueKind == JsonValueKind.Object)
        {
            var id = ReadString(single, "limitId") ?? "codex";
            ParseBucket(id, single, result);
        }
        return result.OrderByDescending(item => item.WindowDurationMinutes).ThenBy(item => item.Name).ToList();
    }

    private static void ParseBucket(string id, JsonElement bucket, ICollection<AiLimitWindow> output)
    {
        var name = ReadString(bucket, "limitName");
        if (string.IsNullOrWhiteSpace(name)) name = id.Equals("codex", StringComparison.OrdinalIgnoreCase) ? "Codex 总额度" : id;
        AddWindow(bucket, "primary", id, name, true, output);
        AddWindow(bucket, "secondary", id, name, false, output);
    }

    private static void AddWindow(JsonElement bucket, string property, string id, string name, bool isPrimary,
        ICollection<AiLimitWindow> output)
    {
        if (!bucket.TryGetProperty(property, out var window) || window.ValueKind != JsonValueKind.Object) return;
        var duration = ReadInt(window, "windowDurationMins");
        var resetSeconds = ReadLong(window, "resetsAt");
        output.Add(new AiLimitWindow
        {
            LimitId = id,
            Name = name,
            WindowName = FormatDuration(duration),
            UsedPercent = Math.Clamp(ReadDouble(window, "usedPercent"), 0, 100),
            WindowDurationMinutes = duration,
            ResetsAt = resetSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds) : null,
            IsPrimary = isPrimary
        });
    }

    private static List<AiDailyUsage> ParseDailyUsage(JsonElement root)
    {
        var result = new List<AiDailyUsage>();
        if (!root.TryGetProperty("dailyUsageBuckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in buckets.EnumerateArray())
        {
            if (!item.TryGetProperty("startDate", out var dateElement) ||
                !DateOnly.TryParse(dateElement.GetString(), out var date)) continue;
            result.Add(new AiDailyUsage { Date = date, Tokens = ReadLong(item, "tokens") });
        }
        return result.OrderBy(item => item.Date).ToList();
    }

    private static long? ParseLifetimeTokens(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object ||
            !summary.TryGetProperty("lifetimeTokens", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt64(out var tokens) ? tokens : null;
    }

    private static int ParseResetCredits(JsonElement root)
    {
        if (!root.TryGetProperty("rateLimitResetCredits", out var credits) || credits.ValueKind != JsonValueKind.Object)
            return 0;
        return ReadInt(credits, "availableCount");
    }

    private static List<AiThreadSummary> ParseThreads(JsonElement root)
    {
        var result = new List<AiThreadSummary>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            var title = ReadString(item, "name") ?? ReadString(item, "preview") ?? "未命名任务";
            title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (title.Length > 64) title = title[..64] + "…";
            var status = item.TryGetProperty("status", out var statusElement)
                ? ParseThreadStatus(statusElement)
                : "notLoaded";
            var updated = ReadLong(item, "updatedAt");
            result.Add(new AiThreadSummary
            {
                Id = ReadString(item, "id") ?? string.Empty,
                Title = title,
                Status = status,
                ModelName = ReadString(item, "model"),
                ReasoningEffort = ReadString(item, "reasoningEffort"),
                RolloutPath = ReadString(item, "path"),
                UpdatedAt = updated > 0 ? DateTimeOffset.FromUnixTimeSeconds(updated) : null
            });
        }
        return result;
    }

    private async Task<AiThreadSummary> EnrichFromLatestTurnAsync(AiThreadSummary summary,
        CancellationToken cancellationToken)
    {
        var detail = await TryRequestAsync("thread/read", new JsonObject
        {
            ["threadId"] = summary.Id,
            ["includeTurns"] = true
        }, TimeSpan.FromSeconds(7), cancellationToken);
        if (detail is not { } root || !root.TryGetProperty("thread", out var thread)) return summary;

        var status = thread.TryGetProperty("status", out var threadStatus)
            ? ParseThreadStatus(threadStatus)
            : summary.Status;
        if (thread.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array)
        {
            foreach (var turn in turns.EnumerateArray().Reverse())
            {
                status = ReadString(turn, "status") ?? status;
                break;
            }
        }
        // A separately launched app-server cannot observe another Codex process's in-memory
        // active flag. The shared rollout is authoritative for the latest execution boundary
        // and is appended while the desktop task is running.
        var rolloutPath = ReadString(thread, "path") ?? summary.RolloutPath;
        status = ReadRolloutExecutionStatus(rolloutPath, status);
        return new AiThreadSummary
        {
            Id = summary.Id,
            Title = NormalizeTitle(ReadString(thread, "name") ?? ReadString(thread, "preview") ?? summary.Title),
            Status = status,
            ModelName = ReadString(thread, "model") ?? summary.ModelName,
            ReasoningEffort = ReadString(thread, "reasoningEffort") ?? summary.ReasoningEffort,
            RolloutPath = rolloutPath,
            UpdatedAt = ReadLong(thread, "updatedAt") is var updated && updated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(updated)
                : summary.UpdatedAt
        };
    }

    private static string ParseThreadStatus(JsonElement statusElement)
    {
        if (statusElement.ValueKind == JsonValueKind.String)
            return statusElement.GetString() ?? "notLoaded";
        if (statusElement.ValueKind != JsonValueKind.Object) return "notLoaded";

        var status = ReadString(statusElement, "type") ?? "notLoaded";
        if (status != "active" || !statusElement.TryGetProperty("activeFlags", out var flags) ||
            flags.ValueKind != JsonValueKind.Array) return status;

        var activeFlags = flags.EnumerateArray().Select(flag => flag.GetString()).ToHashSet();
        if (activeFlags.Contains("waitingOnApproval")) return "waitingOnApproval";
        return activeFlags.Contains("waitingOnUserInput") ? "waitingOnUserInput" : status;
    }

    private static string NormalizeTitle(string title)
    {
        title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return title.Length > 64 ? title[..64] + "…" : title;
    }

    private static string ReadRolloutExecutionStatus(string? path, string fallback)
    {
        if (path?.StartsWith(@"\\?\", StringComparison.Ordinal) == true) path = path[4..];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return fallback;
        var status = fallback;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (!(line.Contains("task_started", StringComparison.Ordinal) ||
                      line.Contains("task_complete", StringComparison.Ordinal) ||
                      line.Contains("turn_aborted", StringComparison.Ordinal))) continue;

                try
                {
                    using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 256 });
                    var root = document.RootElement;
                    if (!string.Equals(ReadString(root, "type"), "event_msg", StringComparison.Ordinal) ||
                        !root.TryGetProperty("payload", out var payload)) continue;
                    status = ReadString(payload, "type") switch
                    {
                        "task_started" => "inProgress",
                        "task_complete" => "completed",
                        "turn_aborted" => string.Equals(ReadString(payload, "reason"), "interrupted",
                            StringComparison.OrdinalIgnoreCase) ? "interrupted" : "failed",
                        _ => status
                    };
                }
                catch (JsonException)
                {
                    // The writer can expose a final partial line while a task is actively appending.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[AiManager] Rollout status unavailable: {ex.Message}");
        }
        return status;
    }

    private static (string? DisplayName, string? ReasoningEffort) ResolveModel(
        JsonElement? root, string? preferredModel)
    {
        if (root is not { } modelRoot || !modelRoot.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return (preferredModel, null);

        JsonElement? fallback = null;
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadString(item, "model") ?? ReadString(item, "id");
            if (!string.IsNullOrWhiteSpace(preferredModel) &&
                string.Equals(id, preferredModel, StringComparison.OrdinalIgnoreCase))
                return (ReadString(item, "displayName") ?? id, ReadString(item, "defaultReasoningEffort"));
            if (item.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True)
                fallback = item.Clone();
        }
        if (fallback is { } selected)
            return (ReadString(selected, "displayName") ?? ReadString(selected, "model"),
                ReadString(selected, "defaultReasoningEffort"));
        return (preferredModel, null);
    }

    private static string FormatDuration(int minutes) => minutes switch
    {
        >= 1440 when minutes % 1440 == 0 => $"{minutes / 1440} 天窗口",
        >= 60 when minutes % 60 == 0 => $"{minutes / 60} 小时窗口",
        _ => $"{minutes} 分钟窗口"
    };

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static long ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static double ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) ? number : 0;

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
