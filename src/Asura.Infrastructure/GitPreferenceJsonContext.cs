using System.Text.Json.Serialization;
using Asura.Application;

namespace Asura.Infrastructure;

[JsonSerializable(typeof(GitPanelPreferenceState))]
internal sealed partial class GitPreferenceJsonContext : JsonSerializerContext;
