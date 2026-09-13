using System.Text.Json.Serialization;

namespace SoftwareToolkit.Models;

public sealed class AiManagerSettings
{
    [JsonPropertyName("warningPercent")]
    public double WarningPercent { get; set; } = 70;

    [JsonPropertyName("pausePercent")]
    public double PausePercent { get; set; } = 90;

    [JsonPropertyName("autoPause")]
    public bool AutoPause { get; set; } = true;

    [JsonPropertyName("refreshSeconds")]
    public int RefreshSeconds { get; set; } = 60;
}

public sealed class AiLimitWindow
{
    public string LimitId { get; init; } = string.Empty;
    public string Name { get; init; } = "Codex";
    public string WindowName { get; init; } = string.Empty;
    public double UsedPercent { get; init; }
    public double RemainingPercent => Math.Max(0, 100 - UsedPercent);
    public DateTimeOffset? ResetsAt { get; init; }
    public int WindowDurationMinutes { get; init; }
    public bool IsPrimary { get; init; }
    public string UsedText => $"{UsedPercent:0.#}% 已用";
    public string RemainingText => $"剩余 {RemainingPercent:0.#}%";
    public string ResetText => ResetsAt is null ? "重置时间未知" : $"{ResetsAt.Value.LocalDateTime:M月d日 HH:mm} 重置";
}

public sealed class AiDailyUsage
{
    public DateOnly Date { get; init; }
    public long Tokens { get; init; }
}

public sealed class AiThreadSummary
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = "未命名任务";
    public string Status { get; init; } = "notLoaded";
    public string? ModelName { get; init; }
    public string? ReasoningEffort { get; init; }
    public string? RolloutPath { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public bool IsActive => Status is "active" or "inProgress" or "waitingOnApproval" or "waitingOnUserInput";
    public string StatusText => Status switch
    {
        "active" or "inProgress" => "推理中",
        "waitingOnApproval" => "等待批准",
        "waitingOnUserInput" => "等待输入",
        "idle" => "已完成",
        "completed" => "已完成",
        "interrupted" => "已暂停",
        "failed" or "systemError" => "异常",
        _ => "历史记录"
    };
    public string UpdatedText => UpdatedAt?.LocalDateTime.ToString("M月d日 HH:mm") ?? "时间未知";
}

public sealed class AiManagerSnapshot
{
    public List<AiLimitWindow> Limits { get; init; } = new();
    public List<AiDailyUsage> DailyUsage { get; init; } = new();
    public List<AiThreadSummary> Threads { get; init; } = new();
    public long? LifetimeTokens { get; init; }
    public int ResetCredits { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public AiLimitWindow? MainLimit => Limits
        .Where(item => item.IsPrimary)
        .OrderByDescending(item => item.WindowDurationMinutes)
        .FirstOrDefault() ?? Limits.OrderByDescending(item => item.WindowDurationMinutes).FirstOrDefault();
}

public sealed record AiForecast(
    double DailyTokens,
    double DailyPercent,
    DateTimeOffset? ExhaustsAt,
    bool ExhaustsBeforeReset,
    string Summary);

public sealed class AiFloatingSnapshot
{
    public List<AiThreadSummary> Tasks { get; init; } = new();
    public string TaskName { get; init; } = "暂无最近任务";
    public string TaskStatus { get; init; } = "notLoaded";
    public string TaskStatusText { get; init; } = "等待任务";
    public string ModelName { get; init; } = "模型未知";
    public string? ReasoningEffort { get; init; }
    public double? RemainingPercent { get; init; }
    public string QuotaWindow { get; init; } = "额度窗口未知";
    public DateTimeOffset? ResetsAt { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
}
