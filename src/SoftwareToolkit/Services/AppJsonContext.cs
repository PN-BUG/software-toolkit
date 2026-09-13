using System.Text.Json;
using System.Text.Json.Serialization;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>
/// 源生成 JSON 序列化上下文，用于 AOT 兼容。
/// 配置与原来 _jsonOpts 保持一致。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolsConfig))]
[JsonSerializable(typeof(ToolDefinition))]
[JsonSerializable(typeof(UserState))]
[JsonSerializable(typeof(ToolUsage))]
[JsonSerializable(typeof(SoftwareListState))]
[JsonSerializable(typeof(SoftwareListEntry))]
[JsonSerializable(typeof(AiManagerSettings))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
