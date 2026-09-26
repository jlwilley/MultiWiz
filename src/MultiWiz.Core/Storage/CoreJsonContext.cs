using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Storage;

/// <summary>
/// Source-generated JSON metadata for everything MultiWiz persists: camelCase property names, enums as their
/// names (also as dictionary keys), indented output, and tolerant reading (comments and trailing commas allowed).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AccountsDocument))]
[JsonSerializable(typeof(TeamsDocument))]
public sealed partial class CoreJsonContext : JsonSerializerContext
{
}
