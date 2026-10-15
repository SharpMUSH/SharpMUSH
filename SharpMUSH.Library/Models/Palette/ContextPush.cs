using System.Text;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Palette;

/// <summary>
/// What <c>@context/push</c> was given after the <c>=</c>:
/// <c>&lt;name&gt;[,&lt;values as JSON&gt;[,&lt;lifetime&gt;[,&lt;info&gt;]]]</c>. The values are one JSON
/// object, whose own commas do not split it, and the info is the rest of the line, commas included.
/// </summary>
public sealed record ContextPush(string Name, IReadOnlyDictionary<string, string> Values, TimeSpan? Lifetime, string? Info)
{
	public const string InvalidName = "#-1 INVALID CONTEXT NAME";
	public const string InvalidValues = "#-1 CONTEXT VALUES MUST BE A JSON OBJECT OF TEXT";
	public const string InvalidLifetime = "#-1 INVALID CONTEXT LIFETIME";
	public const string InfoTooLong = "#-1 CONTEXT INFO TOO LONG";

	/// <summary>Reads <paramref name="text"/>, or the error to answer with.</summary>
	public static Result<ContextPush> Parse(string text)
	{
		var comma = text.IndexOf(',');
		var name = (comma < 0 ? text : text[..comma]).Trim();
		if (!IsValidName(name)) return new Error<string>(InvalidName);
		if (comma < 0) return new ContextPush(name.ToLowerInvariant(), new Dictionary<string, string>(), null, null);

		var afterName = text[(comma + 1)..];
		var start = afterName.Length - afterName.TrimStart().Length;
		string valuesText;
		string? rest;
		if (start < afterName.Length && afterName[start] == '{')
		{
			if (JsonObjectLength(afterName[start..]) is not { } length) return new Error<string>(InvalidValues);
			valuesText = afterName.Substring(start, length);
			var next = afterName[(start + length)..].TrimStart();
			if (next.Length > 0 && next[0] != ',') return new Error<string>(InvalidValues);
			rest = next.Length > 0 ? next[1..] : null;
		}
		else
		{
			comma = afterName.IndexOf(',');
			valuesText = (comma < 0 ? afterName : afterName[..comma]).Trim();
			if (valuesText.Length > 0) return new Error<string>(InvalidValues);
			rest = comma < 0 ? null : afterName[(comma + 1)..];
		}

		if (ReadValues(valuesText) is not IReadOnlyDictionary<string, string> values) return new Error<string>(InvalidValues);
		if (rest is null) return new ContextPush(name.ToLowerInvariant(), values, null, null);

		comma = rest.IndexOf(',');
		var lifetimeText = (comma < 0 ? rest : rest[..comma]).Trim();
		if (!DurationSetting.TryParse(lifetimeText, (long)ContextWeights.MaxLifetime.TotalSeconds, out var lifetime))
			return new Error<string>(InvalidLifetime);

		var info = comma < 0 ? null : rest[(comma + 1)..].Trim();
		if (info?.Length > ContextWeights.MaxInfoLength) return new Error<string>(InfoTooLong);
		if (info is not null && info.Any(char.IsControl)) info = string.Concat(info.Select(c => char.IsControl(c) ? ' ' : c));

		return new ContextPush(name.ToLowerInvariant(), values, lifetime > TimeSpan.Zero ? lifetime : null,
			string.IsNullOrEmpty(info) ? null : info);
	}

	/// <summary>
	/// A context name: letters, digits and <c>_ - . : #</c>, at most <see cref="ContextWeights.MaxNameLength"/>.
	/// <c>#</c> and <c>:</c> are there for an object's own system, its objid.
	/// </summary>
	public static bool IsValidName(string name)
		=> name.Length is > 0 and <= ContextWeights.MaxNameLength
			&& name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '#');

	/// <summary>The length of the JSON object <paramref name="text"/> starts with, or null when it does not start with one.</summary>
	private static int? JsonObjectLength(string text)
	{
		var bytes = Encoding.UTF8.GetBytes(text);
		var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
		try
		{
			if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
			reader.Skip();
			return Encoding.UTF8.GetCharCount(bytes, 0, (int)reader.BytesConsumed);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>A flat JSON object, each value text, a number or a boolean, as text.</summary>
	private static IReadOnlyDictionary<string, string>? ReadValues(string text)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (text.Length == 0) return values;
		try
		{
			using var document = JsonDocument.Parse(text);
			if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
			foreach (var property in document.RootElement.EnumerateObject())
			{
				var value = property.Value.ValueKind switch
				{
					JsonValueKind.String => property.Value.GetString(),
					JsonValueKind.Number => property.Value.GetRawText(),
					JsonValueKind.True => "1",
					JsonValueKind.False => "0",
					_ => null
				};
				if (value is null || value.Length > ContextWeights.MaxValueLength || value.Any(char.IsControl)) return null;
				if (property.Name.Length is 0 or > ContextWeights.MaxNameLength) return null;
				values[property.Name] = value;
			}

			return values.Count > ContextWeights.MaxValues ? null : values;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
