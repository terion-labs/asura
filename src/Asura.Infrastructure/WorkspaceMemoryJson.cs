using System.Text.Json.Serialization;
using Asura.Core;

namespace Asura.Infrastructure;

[JsonSerializable(typeof(WorkspaceMemory))]
[JsonSerializable(typeof(WorkspaceMemoryWrite))]
internal sealed partial class WorkspaceMemoryJson : JsonSerializerContext;
