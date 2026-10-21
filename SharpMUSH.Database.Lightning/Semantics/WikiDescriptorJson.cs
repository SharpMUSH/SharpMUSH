using System.Buffers;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

internal static class WikiDescriptorJson
{
	internal static WikiDescriptorPage ReadPage(ReadOnlySpan<byte> bytes, WikiDescriptorReadWork work)
	{
		work.Check();
		if (bytes.Length > NativeReadBudget.MaxRecordBytes)
			throw new NativeReadLimitException("descriptor-record-bytes");
		work.Scan(bytes.Length); // Whole structural-reader pass, including whitespace and skipped bodies.
		var page = work.CreatePage();
		try
		{
			var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
			if (!Read(ref reader, work, out _) || reader.TokenType != JsonTokenType.StartObject) throw Bad();
			var seen = 0;
			while (Read(ref reader, work, out _))
			{
				if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
				{
					if (Read(ref reader, work, out _)) throw Bad();
					work.Check();
					return page;
				}
				if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) throw Bad();
				var field = Field(ref reader, work);
				if (field >= 0)
				{
					var bit = 1 << field;
					if ((seen & bit) != 0) throw Bad();
					seen |= bit;
				}
				if (!Read(ref reader, work, out var units)) throw Bad();
				if (field is >= 0 and <= 3)
					page.SetString(field, String(ref reader, units, field switch { 0 => 64, 1 => 256, 2 => 1024, _ => 64 }, page, work));
				else if (field == 4)
				{
					if (reader.TokenType is not (JsonTokenType.Null or JsonTokenType.True or JsonTokenType.False)) throw Bad();
					page.SetPublished(reader.TokenType != JsonTokenType.False);
				}
				else if (field == 5) Categories(ref reader, page, work);
				else Skip(ref reader, work);
			}
			throw Bad();
		}
		catch { page.Dispose(); throw; }
	}

	private static bool Read(ref Utf8JsonReader reader, WikiDescriptorReadWork work, out int units)
	{
		work.Check();
		units = 0;
		if (!reader.Read()) return false;
		work.Token();
		if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
		{
			units = ValidateString(reader.ValueSpan, work);
			if (reader.TokenType == JsonTokenType.PropertyName && units > 128)
				throw new NativeReadLimitException("descriptor-property-name");
		}
		return true;
	}
	private static int Field(ref Utf8JsonReader reader, WikiDescriptorReadWork work)
	{
		if (reader.HasValueSequence) throw Bad(); // Reader is constructed only from a contiguous span.
		using var temporary = work.NativeBudget.ReserveWorking(3 * 128);
		work.Scan(reader.ValueSpan.Length);
		Span<byte> scratch = stackalloc byte[3 * 128];
		var written = reader.CopyString(scratch); // Byte overload: no hidden escaped char/raw scratch.
		var name = scratch[..written];
		if (Equal(name, "Namespace"u8, work)) return 0;
		if (Equal(name, "Slug"u8, work)) return 1;
		if (Equal(name, "Title"u8, work)) return 2;
		if (Equal(name, "AuthorDbref"u8, work)) return 3;
		if (Equal(name, "Published"u8, work)) return 4;
		return Equal(name, "Categories"u8, work) ? 5 : -1;
	}
	private static bool Equal(ReadOnlySpan<byte> decoded, ReadOnlySpan<byte> name, WikiDescriptorReadWork work)
	{
		work.Scan(decoded.Length);
		return decoded.SequenceEqual(name);
	}
	private static string String(ref Utf8JsonReader reader, int units, int max, WikiDescriptorPage page, WikiDescriptorReadWork work)
	{
		if (reader.TokenType != JsonTokenType.String) throw Bad();
		if (units > max) throw new NativeReadLimitException("descriptor-string");
		if (reader.HasValueSequence) throw Bad();
		work.Materialize(units);
		using var temporary = work.NativeBudget.ReserveWorking(3L * units);
		page.Reserve(32L + 2L * units);
		work.Scan(reader.ValueSpan.Length);
		Span<byte> scratch = stackalloc byte[3 * units];
		var written = reader.CopyString(scratch);
		work.Scan(written); // Final UTF-8-to-string pass, separately charged before allocation.
		return Encoding.UTF8.GetString(scratch[..written]);
	}
	private static void Categories(ref Utf8JsonReader reader, WikiDescriptorPage page, WikiDescriptorReadWork work)
	{
		if (reader.TokenType == JsonTokenType.Null) return;
		if (reader.TokenType != JsonTokenType.StartArray) throw Bad();
		page.BeginCategories();
		var count = 0;
		while (Read(ref reader, work, out var units))
		{
			if (reader.TokenType == JsonTokenType.EndArray) return;
			if (count == 128) throw new NativeReadLimitException("descriptor-categories");
			page.AddCategory(String(ref reader, units, 256, page, work));
			count++;
		}
		throw Bad();
	}
	private static void Skip(ref Utf8JsonReader reader, WikiDescriptorReadWork work)
	{
		if (reader.TokenType is not (JsonTokenType.StartArray or JsonTokenType.StartObject)) return;
		var depth = reader.CurrentDepth;
		while (Read(ref reader, work, out _))
			if (reader.CurrentDepth == depth && reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject) return;
		throw Bad();
	}

	// Validate even skipped payload strings without GetString, replacement decoding, or a body buffer.
	private static int ValidateString(ReadOnlySpan<byte> raw, WikiDescriptorReadWork work)
	{
		work.Scan(raw.Length);
		var units = 0;
		var checkedAt = -256;
		for (var index = 0; index < raw.Length;)
		{
			// Largest atomic advance is12 bytes (escaped surrogate pair); <=256 bytes between checks.
			if (index - checkedAt >= 244) { work.Check(); checkedAt = index; }
			var value = raw[index++];
			if (value == '\\')
			{
				if (index == raw.Length) throw Bad();
				var escape = raw[index++];
				if (escape != 'u')
				{
					if (escape is not ((byte)'"' or (byte)'\\' or (byte)'/' or (byte)'b' or (byte)'f' or (byte)'n' or (byte)'r' or (byte)'t')) throw Bad();
					units++;
					continue;
				}
				var first = Hex(raw, ref index);
				if (first is >= 0xD800 and <= 0xDBFF)
				{
					if (raw.Length - index < 6 || raw[index++] != '\\' || raw[index++] != 'u') throw Bad();
					var second = Hex(raw, ref index);
					if (second is < 0xDC00 or > 0xDFFF) throw Bad();
					units += 2;
				}
				else if (first is >= 0xDC00 and <= 0xDFFF) throw Bad();
				else units++;
			}
			else if (value < 0x80)
			{
				if (value < 0x20) throw Bad();
				units++;
			}
			else
			{
				index--;
				if (Rune.DecodeFromUtf8(raw[index..], out var rune, out var consumed) != OperationStatus.Done) throw Bad();
				units += rune.Utf16SequenceLength;
				index += consumed;
			}
		}
		work.Check();
		return units;
	}
	private static int Hex(ReadOnlySpan<byte> raw, ref int index)
	{
		if (raw.Length - index < 4) throw Bad();
		var result = 0;
		for (var digit = 0; digit < 4; digit++)
		{
			var value = raw[index++];
			var hex = value switch { >= (byte)'0' and <= (byte)'9' => value - '0', >= (byte)'a' and <= (byte)'f' => value - 'a' + 10, >= (byte)'A' and <= (byte)'F' => value - 'A' + 10, _ => -1 };
			if (hex < 0) throw Bad();
			result = result * 16 + hex;
		}
		return result;
	}
	private static JsonException Bad() => new("Malformed wiki descriptor source.");
}
