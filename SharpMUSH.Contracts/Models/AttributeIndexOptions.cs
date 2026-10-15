using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// How an indexed attribute's values are ordered, as <c>@attribute/index &lt;name&gt;=&lt;options&gt;</c> and a
/// package's <c>index:</c> give them: words separated by spaces, each optional.
/// <list type="bullet">
/// <item><c>exact</c>: values compare as written. Without it they compare as <c>strmatch()</c> does, ignoring case
/// (lower-cased, culture-invariant), so the order is <c>sort(..., i)</c>'s.</item>
/// <item><c>numeric</c>: values <c>isnum()</c> accepts come first, in numeric order, so <c>05</c> and <c>5</c> are
/// one key; every other value follows as text, in the index's case rule.</item>
/// <item><c>split</c> or <c>split:&lt;delimiter&gt;</c>: one entry per element of the value as a list (spaces, or
/// the one character given), so an object is found by any element. Empty elements have no entry.</item>
/// <item><c>type:&lt;type&gt;</c>: only copies on objects of that type (<c>player</c>, <c>room</c>, <c>thing</c>
/// or <c>exit</c>) are indexed.</item>
/// </list>
/// </summary>
/// <param name="Exact">Values compare with case; otherwise lower-cased first.</param>
/// <param name="Numeric">Numbers first, in numeric order, then text.</param>
/// <param name="Split">The list delimiter, a single character (<c>" "</c> for a space list); null indexes the whole value.</param>
/// <param name="Type">The object type indexed, upper case; null for every type.</param>
public sealed record AttributeIndexOptions(bool Exact = false, bool Numeric = false, string? Split = null, string? Type = null)
{
	/// <summary>Case-insensitive text, whole values, every object.</summary>
	public static AttributeIndexOptions Default { get; } = new();

	/// <summary>
	/// The longest attribute name an index can hold, in UTF-8 bytes once upper-cased. An index key is the name, a
	/// separator and up to 400 bytes of value, 1,425 bytes at most: inside LMDB 1.0's key limit at any page size from
	/// 4 KiB up (1,978 bytes at 4 KiB; 8,122 at the 16 KiB SharpMUSH creates worlds with).
	/// </summary>
	public const int MaxNameBytes = 1024;

	/// <summary>Whether <paramref name="name"/> is short enough to index (<see cref="MaxNameBytes"/>).</summary>
	public static bool NameFits(string name) => Encoding.UTF8.GetByteCount(name.ToUpperInvariant()) <= MaxNameBytes;

	/// <summary>Why <paramref name="name"/> cannot be indexed, when it is too long.</summary>
	public static string NameTooLong(string name)
		=> $"{name.ToUpperInvariant()} is too long to index: an indexed attribute's name is at most {MaxNameBytes} bytes.";

	/// <summary>The object types a <c>type:</c> option names, as stored.</summary>
	public static IReadOnlyList<string> ObjectTypes { get; } = ["PLAYER", "ROOM", "THING", "EXIT"];

	/// <summary>
	/// Reads the option words. An unknown word, a <c>split:</c> delimiter that is not one character, or a type that
	/// is not an object type is an error naming it.
	/// </summary>
	public static Result<AttributeIndexOptions> Parse(string words)
	{
		var options = Default;
		foreach (var word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var colon = word.IndexOf(':');
			var head = (colon < 0 ? word : word[..colon]).ToLowerInvariant();
			var argument = colon < 0 ? null : word[(colon + 1)..];
			switch (head)
			{
				case "exact" when argument is null:
					options = options with { Exact = true };
					break;
				case "numeric" when argument is null:
					options = options with { Numeric = true };
					break;
				case "split" when argument is null:
					options = options with { Split = " " };
					break;
				case "split" when argument is { Length: 1 }:
					options = options with { Split = argument };
					break;
				case "split":
					return new Error<string>($"split:<delimiter> takes one character, not '{argument}'.");
				case "type" when argument is not null && TypeNamed(argument) is { } type:
					options = options with { Type = type };
					break;
				case "type":
					return new Error<string>($"type:<type> names player, room, thing or exit, not '{argument}'.");
				default:
					return new Error<string>($"'{word}' is not an index option: exact, numeric, split[:<delimiter>] or type:<type>.");
			}
		}

		return options;
	}

	private static string? TypeNamed(string text) => text.ToUpperInvariant() switch
	{
		"PLAYER" or "PLAYERS" => "PLAYER",
		"ROOM" or "ROOMS" => "ROOM",
		"THING" or "THINGS" or "OBJECT" or "OBJECTS" => "THING",
		"EXIT" or "EXITS" => "EXIT",
		_ => null
	};

	/// <summary>The options as <see cref="Parse"/> reads them back, in a fixed order; empty for <see cref="Default"/>.</summary>
	public string Words
	{
		get
		{
			var words = new List<string>(4);
			if (Exact) words.Add("exact");
			if (Numeric) words.Add("numeric");
			if (Split is not null) words.Add(Split == " " ? "split" : $"split:{Split}");
			if (Type is not null) words.Add($"type:{Type.ToLowerInvariant()}");
			return string.Join(' ', words);
		}
	}

	/// <summary>The options in a sentence fragment, for <c>@attribute</c> and the package review screen.</summary>
	public string Describe()
	{
		var order = (Numeric, Exact) switch
		{
			(true, true) => "numbers, then text with case",
			(true, false) => "numbers, then text without case",
			(false, true) => "text with case",
			_ => "text without case"
		};
		var split = Split switch
		{
			null => string.Empty,
			" " => ", each word of a list",
			_ => $", each element of a list split on {Split}"
		};
		var scope = Type is null ? string.Empty : $", {Type.ToLowerInvariant()}s only";
		return order + split + scope;
	}

	/// <summary>The fold <see cref="Exact"/> chooses, applied to one plain value.</summary>
	public string Fold(string plain) => Exact ? plain : plain.ToLowerInvariant();

	/// <summary>
	/// The elements of <paramref name="plain"/> this index keeps, before folding: the whole value, or each element of
	/// it as a list, with empty ones dropped. A space list splits on runs of spaces, as MUSH lists do.
	/// </summary>
	public IEnumerable<string> Elements(string plain)
	{
		if (Split is null)
		{
			if (plain.Length > 0) yield return plain;
			yield break;
		}

		var parts = Split == " "
			? plain.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			: plain.Split(Split[0]);
		foreach (var part in parts)
		{
			if (part.Length > 0) yield return part;
		}
	}
}
