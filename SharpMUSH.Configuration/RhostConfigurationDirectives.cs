using System.Globalization;

namespace SharpMUSH.Configuration;

/// <summary>One reality level, from a <c>reality_level</c> line of RhostMUSH's <c>netrhost.conf</c> or TinyMUX's <c>netmux.conf</c>.</summary>
/// <param name="Name">The level's name.</param>
/// <param name="Value">Its bit mask: an object is on the level when its own mask has every one of these bits.</param>
/// <param name="DescriptionAttribute">The attribute that describes an object to viewers on the level.</param>
public sealed record RealityLevelDirective(string Name, uint Value, string DescriptionAttribute);

/// <summary>
/// The reality levels of a RhostMUSH or TinyMUX game and the masks an object without levels of its own has, by type,
/// from its configuration file. The database holds only each object's masks; this is what names their bits.
/// </summary>
/// <param name="Levels">The levels, in the order the file defines them.</param>
/// <param name="Defaults">
/// The masks the file gave an object of each type that has no levels of its own, keyed <c>room_rx</c>,
/// <c>player_tx</c> and so on. A type the file leaves out has RhostMUSH's default (<see cref="Default"/>).
/// </param>
public sealed record RealityDirectives(IReadOnlyList<RealityLevelDirective> Levels, IReadOnlyDictionary<string, uint> Defaults)
{
	public static readonly RealityDirectives None = new([], new Dictionary<string, uint>());

	/// <summary>
	/// The mask an object of <paramref name="type"/> (<c>room</c>, <c>player</c>, <c>exit</c>, <c>thing</c>) with
	/// no levels of its own receives (<c>rx</c>) or transmits (<c>tx</c>): the file's, or else the default both servers
	/// share, which is the first bit, and every bit for what a room transmits.
	/// </summary>
	public uint Default(string type, string direction)
		=> Defaults.TryGetValue($"{type}_{direction}", out var mask)
			? mask
			: type == "room" && direction == "tx" ? uint.MaxValue : 1;

	/// <summary>The directives a configuration file gives realities in: <c>reality_level</c> and <c>def_&lt;type&gt;_&lt;rx|tx&gt;</c>.</summary>
	public static bool IsDirective(string name)
		=> name.Equals("reality_level", StringComparison.OrdinalIgnoreCase) || DefaultKey(name) is not null;

	/// <summary>
	/// Reads the reality directives among <paramref name="lines"/>, each a directive name and its value. A level
	/// line with no usable mask takes the first bit, as in RhostMUSH. A repeated level name keeps the first.
	/// </summary>
	public static RealityDirectives Read(IEnumerable<(string Name, string Value)> lines)
	{
		var levels = new List<RealityLevelDirective>();
		var defaults = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value) in lines)
		{
			var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (name.Equals("reality_level", StringComparison.OrdinalIgnoreCase))
			{
				if (words.Length < 2 || levels.Any(level => level.Name.Equals(words[0], StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}

				levels.Add(new RealityLevelDirective(words[0], Mask(words[1]) is { } mask and not 0 ? mask : 1,
					words.Length > 2 ? words[2].ToUpperInvariant() : "DESC"));
			}
			else if (DefaultKey(name) is { } key && words.Length > 0 && Mask(words[0]) is { } mask)
			{
				defaults[key] = mask;
			}
		}

		return levels.Count == 0 && defaults.Count == 0 ? None : new RealityDirectives(levels, defaults);
	}

	private static string? DefaultKey(string name)
	{
		var parts = name.ToLowerInvariant().Split('_');
		return parts is ["def", "room" or "player" or "exit" or "thing", "rx" or "tx"] ? $"{parts[1]}_{parts[2]}" : null;
	}

	/// <summary>A mask written in decimal or as <c>0x</c> hexadecimal, as RhostMUSH takes both.</summary>
	internal static uint? Mask(string text)
		=> text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null
			: uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
			: long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed) ? unchecked((uint)signed) : null;
}

/// <summary>
/// One RhostMUSH totem a <c>netrhost.conf</c> defines with <c>totem_add &lt;name&gt; &lt;slot&gt; &lt;value&gt;</c>: a
/// flag held as a bit of one of an object's ten totem words.
/// </summary>
/// <param name="Name">The totem's name.</param>
/// <param name="Slot">Which of the object's totem words holds it, 0-9.</param>
/// <param name="Value">Its bit in that word.</param>
public sealed record TotemDirective(string Name, int Slot, uint Value)
{
	/// <summary>Reads the <c>totem_add</c> lines among <paramref name="lines"/>, each a directive name and its value.</summary>
	public static IReadOnlyList<TotemDirective> Read(IEnumerable<(string Name, string Value)> lines)
		=> [.. lines
			.Where(line => line.Name.Equals("totem_add", StringComparison.OrdinalIgnoreCase))
			.Select(line => line.Value.Split([' ', '\t', '=', ','], StringSplitOptions.RemoveEmptyEntries))
			.Where(words => words.Length >= 3 && int.TryParse(words[1], CultureInfo.InvariantCulture, out var slot) && slot is >= 0 and < 10
				&& RealityDirectives.Mask(words[2]) is not (null or 0))
			.Select(words => new TotemDirective(words[0], int.Parse(words[1], CultureInfo.InvariantCulture), RealityDirectives.Mask(words[2]) ?? 0))];
}
