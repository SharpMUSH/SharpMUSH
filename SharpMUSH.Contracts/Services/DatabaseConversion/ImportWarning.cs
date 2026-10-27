using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>What part of the world a warning is about. The import report groups its warnings by it, in this order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportWarningKind>))]
public enum ImportWarningKind
{
	Players,
	Objects,
	Attributes,
	Flags,
	Powers,
	Locks,
	Softcode,
	Mail,
	Channels,
	Packages,
	Configuration,
	Other
}

/// <summary>
/// One thing a database import carried on past. <paramref name="NeedsAction"/> marks the ones an administrator has to
/// do something about, such as setting new passwords, which the report shows ahead of the rest.
/// </summary>
[JsonConverter(typeof(ImportWarningJsonConverter))]
public sealed record ImportWarning(ImportWarningKind Kind, string Text, bool NeedsAction = false)
{
	public override string ToString() => Text;
}

/// <summary>
/// Writes a warning as an object, and reads one written as a bare string, as an import kept on disk from before
/// warnings had a kind wrote it, as <see cref="ImportWarningKind.Other"/>.
/// </summary>
public sealed class ImportWarningJsonConverter : JsonConverter<ImportWarning>
{
	private sealed record Stored(ImportWarningKind Kind, string Text, bool NeedsAction);

	public override ImportWarning? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.String)
		{
			return new ImportWarning(ImportWarningKind.Other, reader.GetString() ?? string.Empty);
		}

		return JsonSerializer.Deserialize<Stored>(ref reader, options) is { } stored
			? new ImportWarning(stored.Kind, stored.Text ?? string.Empty, stored.NeedsAction)
			: null;
	}

	public override void Write(Utf8JsonWriter writer, ImportWarning value, JsonSerializerOptions options)
		=> JsonSerializer.Serialize(writer, new Stored(value.Kind, value.Text, value.NeedsAction), options);
}
