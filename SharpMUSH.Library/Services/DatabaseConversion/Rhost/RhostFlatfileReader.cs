using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.Rhost;

/// <summary>
/// Reads a RhostMUSH flatfile, the text dump <c>@dump/flat</c> writes (<c>data/&lt;db&gt;.db.flat</c>), into
/// <see cref="RhostFlatfile"/>.
/// </summary>
/// <remarks>
/// <para>The file starts with <c>+V&lt;flags|version&gt;</c>, <c>+S&lt;db_top&gt;</c> and <c>+N&lt;next attribute&gt;</c>,
/// then one <c>+A&lt;number&gt;</c> line per user attribute followed by <c>&lt;flags&gt;:&lt;name&gt;</c>. Each object
/// is <c>!&lt;dbref&gt;</c> and its fields one per line, in an order the <c>+V</c> flags decide; then
/// <c>&gt;&lt;attribute number&gt;</c> and the value, as often as it has attributes, and <c>&lt;</c>. The file
/// ends <c>***END OF DUMP***</c>.</para>
/// <para>A value that holds a line break is written with <c>\r\n</c> inside it, so a line that ends in
/// <c>\r</c> goes on with the next one; the break is read back as <c>\n</c>.</para>
/// <para>The binary database RhostMUSH runs from (<c>netrhost.db</c> and its gdbm files) is not this; a
/// game's administrator writes the flatfile with <c>@dump/flat</c>, or <c>./Startmush -f</c>'s
/// db_unload, first.</para>
/// </remarks>
public static class RhostFlatfileReader
{
	/// <summary>The text encoding the flatfile is read in.</summary>
	public static readonly Encoding FileEncoding = new UTF8Encoding(false);

	/// <summary>The bits of <c>+V</c> that are the format version.</summary>
	public const int VersionMask = 0xff;

	/// <summary><c>V_ZONE</c>: a zone field follows the location.</summary>
	public const int ZoneFlag = 0x100;

	/// <summary><c>V_LINK</c>: a link field follows the exits.</summary>
	public const int LinkFlag = 0x200;

	/// <summary><c>V_ATTRSEXT</c>: the attributes are kept apart and not in the file at all.</summary>
	public const int AttributesExternalFlag = 0x400;

	/// <summary><c>V_ATRNAME</c>: the name is not a field; it is attribute 43.</summary>
	public const int NameInAttributeFlag = 0x800;

	/// <summary><c>V_ATRKEY</c>: the lock is not a field; it is attribute 42.</summary>
	public const int LockInAttributeFlag = 0x1000;

	/// <summary><c>V_PARENT</c>: a parent field follows the owner.</summary>
	public const int ParentFlag = 0x2000;

	/// <summary><c>V_ATRMONEY</c>: the pennies are not a field.</summary>
	public const int MoneyInAttributeFlag = 0x8000;

	/// <summary><c>V_XFLAGS</c>: three more flag words, eight toggle words and the zone list follow the flags.</summary>
	public const int ExtendedFlagsFlag = 0x10000;

	/// <summary>The line that ends the dump.</summary>
	public const string EndOfDump = "***END OF DUMP***";

	/// <summary>Attribute 43, which holds the name in a <c>V_ATRNAME</c> dump.</summary>
	private const int NameAttribute = 43;

	/// <summary>Reads the flatfile at <paramref name="path"/>.</summary>
	public static async Task<RhostFlatfile> ReadFileAsync(string path, CancellationToken cancellationToken = default)
	{
		await using var stream = File.OpenRead(path);
		return await ReadAsync(stream, cancellationToken);
	}

	/// <summary>Reads a flatfile from <paramref name="stream"/>.</summary>
	public static async Task<RhostFlatfile> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
	{
		using var reader = new StreamReader(stream, FileEncoding);
		var text = await reader.ReadToEndAsync(cancellationToken);
		return Read(text, cancellationToken);
	}

	/// <summary>Reads a flatfile held in memory.</summary>
	/// <exception cref="FormatException">The text is not a flatfile this reader can take.</exception>
	public static RhostFlatfile Read(string text, CancellationToken cancellationToken = default)
	{
		var firstBreak = text.IndexOf('\n');
		if (firstBreak > 0 && text[firstBreak - 1] == '\r')
		{
			throw new FormatException("This RhostMUSH flatfile has Windows line endings: it was copied as text, which " +
				"turns every line break into the \\r\\n RhostMUSH writes inside a value. Copy it again as a binary file.");
		}

		var lines = new LineCursor(text);
		var version = -1;
		var top = 0;
		var next = 0;
		var attributes = new Dictionary<int, RhostAttributeDefinition>();

		while (lines.Peek() is { } line && line.StartsWith('+'))
		{
			lines.Next();
			switch (line.Length > 1 ? line[1] : ' ')
			{
				case 'V':
					version = Number(line[2..], lines, "the +V header");
					break;
				case 'S':
					top = Number(line[2..], lines, "the +S header");
					break;
				case 'N':
					next = Number(line[2..], lines, "the +N header");
					break;
				case 'A':
					var number = Number(line[2..], lines, "a +A header");
					var definition = lines.Next() ?? throw lines.Error("the file ends inside a +A header");
					var colon = definition.IndexOf(':');
					if (colon < 0)
					{
						throw lines.Error($"the +A{number} header has no flags:name line");
					}

					attributes[number] = new RhostAttributeDefinition(number, definition[(colon + 1)..],
						UnsignedNumber(definition[..colon], lines, $"the flags of +A{number}"));
					break;
			}
		}

		if (version < 0)
		{
			throw new FormatException("This is not a RhostMUSH flatfile: it does not start with a +V header.");
		}

		if ((version & AttributesExternalFlag) != 0)
		{
			throw new FormatException("This RhostMUSH dump keeps its attributes outside the file (V_ATTRSEXT), so it " +
				"holds no attributes at all. Write a full flatfile with @dump/flat and import that.");
		}

		var objects = new List<RhostFlatfileObject>();
		while (lines.Next() is { } line)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (line == EndOfDump)
			{
				return new RhostFlatfile(version, top, next, attributes, objects);
			}

			if (!line.StartsWith('!'))
			{
				throw lines.Error($"expected an object (!<number>) or {EndOfDump}, found \"{Shorten(line)}\"");
			}

			objects.Add(ReadObject(Number(line[1..], lines, "an object number"), version, lines));
		}

		throw new FormatException($"The RhostMUSH flatfile ends without {EndOfDump}: it was cut short.");
	}

	private static RhostFlatfileObject ReadObject(int dbref, int version, LineCursor lines)
	{
		var name = (version & NameInAttributeFlag) != 0 ? null : Field(lines, dbref, "name");
		var location = Ref(lines, dbref, "location");
		var zone = (version & ZoneFlag) != 0 ? Ref(lines, dbref, "zone") : -1;
		var contents = Ref(lines, dbref, "contents");
		var exits = Ref(lines, dbref, "exits");
		var link = (version & LinkFlag) != 0 ? Ref(lines, dbref, "link") : -1;
		var next = Ref(lines, dbref, "next");
		var lockText = (version & LockInAttributeFlag) != 0 ? string.Empty : Field(lines, dbref, "lock");
		var owner = Ref(lines, dbref, "owner");
		var parent = (version & ParentFlag) != 0 ? Ref(lines, dbref, "parent") : -1;
		var pennies = (version & MoneyInAttributeFlag) != 0 ? 0 : Ref(lines, dbref, "pennies");

		var flags = new uint[4];
		var toggles = new uint[8];
		var zones = new List<int>();
		flags[0] = Word(lines, dbref, "flags");
		if ((version & ExtendedFlagsFlag) != 0)
		{
			for (var i = 1; i < flags.Length; i++)
			{
				flags[i] = Word(lines, dbref, $"flags{i + 1}");
			}

			for (var i = 0; i < toggles.Length; i++)
			{
				toggles[i] = Word(lines, dbref, $"toggles{i + 1}");
			}

			for (var zoneRef = Ref(lines, dbref, "zone list"); zoneRef != -1; zoneRef = Ref(lines, dbref, "zone list"))
			{
				zones.Add(zoneRef);
			}
		}

		var attributes = new List<RhostFlatfileAttribute>();
		while (true)
		{
			var line = lines.Next() ?? throw lines.Error($"the file ends inside #{dbref}'s attributes");
			if (line == "<")
			{
				break;
			}

			if (!line.StartsWith('>'))
			{
				throw lines.Error($"expected an attribute (>number) or < in #{dbref}, found \"{Shorten(line)}\"");
			}

			var key = line[1..];
			var value = lines.Next() ?? throw lines.Error($"the file ends inside #{dbref}'s attribute {key}");
			var (attributeOwner, attributeFlags, text) = SplitPrefix(value);
			attributes.Add(int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
				? new RhostFlatfileAttribute(number, string.Empty, attributeOwner, attributeFlags, text)
				: new RhostFlatfileAttribute(-1, key, attributeOwner, attributeFlags, text));
		}

		name ??= attributes.FirstOrDefault(a => a.Number == NameAttribute)?.Value ?? $"#{dbref}";

		return new RhostFlatfileObject
		{
			DBRef = dbref,
			Name = name,
			Location = location,
			ZoneField = zone,
			Contents = contents,
			Exits = exits,
			Link = link,
			Next = next,
			Lock = lockText,
			Owner = owner,
			Parent = parent,
			Pennies = pennies,
			Flags = flags,
			Toggles = toggles,
			Zones = zones,
			Attributes = attributes
		};
	}

	/// <summary>
	/// Splits the <c>\x01owner:flags:</c> prefix RhostMUSH writes before a value whose owner or flags are not
	/// the default. A value without one, or whose prefix does not read, is all text.
	/// </summary>
	public static (int? Owner, uint Flags, string Text) SplitPrefix(string value)
	{
		if (value.Length == 0 || value[0] != '\x01')
		{
			return (null, 0, value);
		}

		var first = value.IndexOf(':', 1);
		var second = first < 0 ? -1 : value.IndexOf(':', first + 1);
		if (second < 0
				|| !int.TryParse(value.AsSpan(1, first - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var owner)
				|| !TryWord(value.AsSpan(first + 1, second - first - 1), out var flags))
		{
			return (null, 0, value);
		}

		return (owner, flags, value[(second + 1)..]);
	}

	private static string Field(LineCursor lines, int dbref, string what)
		=> lines.Next() ?? throw lines.Error($"the file ends inside #{dbref}'s {what}");

	private static int Ref(LineCursor lines, int dbref, string what)
		=> Number(Field(lines, dbref, what), lines, $"#{dbref}'s {what}");

	private static uint Word(LineCursor lines, int dbref, string what)
		=> UnsignedNumber(Field(lines, dbref, what), lines, $"#{dbref}'s {what}");

	private static int Number(string text, LineCursor lines, string what)
		=> int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
			? number
			: throw lines.Error($"{what} is not a number: \"{Shorten(text)}\"");

	/// <summary>A flag word: RhostMUSH writes it as a signed <c>int</c>, so the top bit reads negative.</summary>
	private static uint UnsignedNumber(string text, LineCursor lines, string what)
		=> TryWord(text.Trim(), out var word) ? word : throw lines.Error($"{what} is not a number: \"{Shorten(text)}\"");

	private static bool TryWord(ReadOnlySpan<char> text, out uint word)
	{
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
				&& number is >= int.MinValue and <= uint.MaxValue)
		{
			word = unchecked((uint)number);
			return true;
		}

		word = 0;
		return false;
	}

	private static string Shorten(string text) => text.Length > 40 ? text[..40] + "..." : text;

	/// <summary>
	/// Hands out the file's lines, joining a value that runs over several (one ending in <c>\r</c>), and
	/// remembers where it is for the errors.
	/// </summary>
	private sealed class LineCursor(string text)
	{
		private int _position;
		private int _lineNumber;
		private string? _peeked;
		private int _peekedEnd;

		public string? Peek()
		{
			if (_peeked is null && _position < text.Length)
			{
				(_peeked, _peekedEnd) = ReadAt(_position);
			}

			return _peeked;
		}

		public string? Next()
		{
			if (Peek() is not { } line)
			{
				return null;
			}

			_position = _peekedEnd;
			_peeked = null;
			_lineNumber++;
			return line;
		}

		public FormatException Error(string message)
			=> new($"RhostMUSH flatfile, line {_lineNumber}: {message}.");

		private (string Line, int End) ReadAt(int start)
		{
			var builder = new StringBuilder();
			var position = start;
			while (true)
			{
				var newline = text.IndexOf('\n', position);
				if (newline < 0)
				{
					builder.Append(text, position, text.Length - position);
					return (builder.ToString(), text.Length);
				}

				if (newline > position && text[newline - 1] == '\r')
				{
					builder.Append(text, position, newline - 1 - position).Append('\n');
					position = newline + 1;
					_lineNumber++;
					continue;
				}

				builder.Append(text, position, newline - position);
				return (builder.ToString(), newline + 1);
			}
		}
	}
}
