using MarkupString;
using System.Text.Json;

namespace SharpMUSH.Library.Models;

/// <summary>Persisted markup and its original serialization; inspection is deferred until a save decision.</summary>
public sealed record StoredMarkup
{
	private readonly Lazy<MarkupReadResult> _inspection;
	public string Serialized { get; }
	public MarkupReadStatus Status => _inspection.Value.Status;
	public bool CanRewrite => _inspection.Value.CanRewrite;
	public IReadOnlyList<string> Diagnostics => _inspection.Value.Diagnostics;
	public MString Value { get; }

	private StoredMarkup(string serialized, MString value, Lazy<MarkupReadResult> inspection)
	{
		Serialized = serialized;
		Value = value;
		_inspection = inspection;
	}

	/// <summary>Ordinary display read. Does not invoke codecs' write methods to assess rewrite safety.</summary>
	public static StoredMarkup Read(string serialized, MarkupRegistry? registry = null)
	{
		ValidateObject(serialized);
		var reader = registry ?? MarkupRegistry.Default;
		return new(serialized, MarkupTextSerializer.Deserialize(serialized, reader),
			new(() => MarkupTextSerializer.Inspect(serialized, reader)));
	}

	public static StoredMarkup Inspect(string serialized, MarkupRegistry? registry = null)
	{
		ValidateObject(serialized);
		var inspected = MarkupTextSerializer.Inspect(serialized, registry ?? MarkupRegistry.Default);
		if (inspected.Value is not { } value)
			throw new JsonException("Stored markup has no displayable value.");
		return new(serialized, value, new(() => inspected));
	}

	private static void ValidateObject(string serialized)
	{
		// A malformed row has no recoverable aggregate. Its display fallback must not become a copy.
		using var document = JsonDocument.Parse(serialized);
		if (document.RootElement.ValueKind != JsonValueKind.Object)
			throw new JsonException("Stored markup must be a JSON object.");
	}
}
