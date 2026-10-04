using System.Text.Json.Serialization;
using Asura.Core;

namespace Asura.Agent.Runtime;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkspaceMemoryWrite))]
[JsonSerializable(typeof(WorkspaceMemoryBrief))]
[JsonSerializable(typeof(WorkspaceMemoryReceipt))]
[JsonSerializable(typeof(WorkspaceMemoryPage))]
internal sealed partial class WorkspaceMemoryToolJson : JsonSerializerContext;
