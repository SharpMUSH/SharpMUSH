using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

/// <summary>
/// The lexical layer of TinyMUX's flatfiles (the game database, <c>comsys.db</c> and <c>mail.db</c>):
/// characters, lines, numbers and the quoted strings <c>putstring</c> writes, with the line number for
/// error messages.
/// </summary>
/// <remarks>
/// A quoted string (<c>getstring_noalloc</c>) starts with <c>"</c>, may run over several lines, and ends
/// at the next <c>"</c> that no <c>\</c> escapes; whatever follows on that line is discarded. Inside it
/// <c>\e</c>, <c>\n</c>, <c>\r</c> and <c>\t</c> (either case) are ESC, LF, CR and TAB, and <c>\x</c> is
/// <c>x</c> for anything else.
/// </remarks>
public sealed class TinyMuxFlatfileReader(TextReader text, string fileDescription)
{
	private int _line = 1;

	/// <summary>The current line, counting from 1.</summary>
	public int Line => _line;

	/// <summary>The next character, or -1 at the end, without consuming it. A CR before an LF is skipped.</summary>
	public int Peek()
	{
		var c = text.Peek();
		if (c != '\r') return c;
		text.Read();
		return text.Peek() == '\n' ? '\n' : Peek();
	}

	/// <summary>The next character, or -1 at the end. A CRLF reads as one LF.</summary>
	public int Read()
	{
		var c = text.Read();
		if (c == '\r' && text.Peek() == '\n') c = text.Read();
		if (c == '\n') _line++;
		return c;
	}

	/// <summary>The rest of the current line, without its line ending; null at the end of the file.</summary>
	public string? ReadLine()
	{
		if (text.Peek() == -1) return null;
		var line = new StringBuilder();
		int c;
		while ((c = Read()) is not (-1 or '\n')) line.Append((char)c);
		return line.ToString();
	}

	/// <summary>The rest of the line as a number; a 32-bit flag word may be written signed or unsigned.</summary>
	public long ReadNumber()
	{
		var line = ReadLine() ?? throw Error("the file ends where a number was expected");
		return long.TryParse(line.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
			? number
			: throw Error($"'{line}' is not a number");
	}

	/// <summary>A dbref or count: <see cref="ReadNumber"/> held to 32 bits.</summary>
	public int ReadInt() => unchecked((int)ReadNumber());

	/// <summary>A flag or power word: <see cref="ReadNumber"/> as its 32 bits.</summary>
	public uint ReadWord() => unchecked((uint)ReadNumber());

	/// <summary>
	/// A string as <c>putstring</c> writes it. One that does not start with <c>"</c> is the rest of the
	/// line, as TinyMUX reads it.
	/// </summary>
	public string ReadString()
	{
		if (Peek() != '"')
		{
			return ReadLine() ?? throw Error("the file ends where a string was expected");
		}

		Read();
		var value = new StringBuilder();
		while (true)
		{
			var c = Read();
			switch (c)
			{
				case -1:
					return value.ToString();
				case '"':
					ReadLine();
					return value.ToString();
				case '\\':
					var escaped = Read();
					value.Append(escaped switch
					{
						-1 => string.Empty,
						'e' or 'E' => "\u001b",
						'n' or 'N' => "\n",
						'r' or 'R' => "\r",
						't' or 'T' => "\t",
						_ => ((char)escaped).ToString()
					});
					break;
				default:
					value.Append((char)c);
					break;
			}
		}
	}

	/// <summary>A <see cref="FormatException"/> naming the file and line.</summary>
	public FormatException Error(string message) => new($"{fileDescription}, line {_line}: {message}");

	/// <summary>
	/// The flatfile's text encoding: Latin-1 for the game database before version 3 and the mail
	/// database before version 6, UTF-8 otherwise.
	/// </summary>
	public static Encoding EncodingFor(bool latin1) => latin1 ? Encoding.Latin1 : new UTF8Encoding(false);

	/// <summary>The first line of a file, read as ASCII, which every TinyMUX flatfile header is.</summary>
	public static string FirstLine(Stream stream)
	{
		var bytes = new List<byte>();
		int b;
		while (bytes.Count < 256 && (b = stream.ReadByte()) is not (-1 or '\n')) bytes.Add((byte)b);
		return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
	}
}
