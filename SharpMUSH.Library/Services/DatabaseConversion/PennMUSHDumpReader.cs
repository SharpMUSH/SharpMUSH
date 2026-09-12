using System.Buffers;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Reads the lines and labeled fields of a PennMUSH database dump, the way PennMUSH's own
/// <c>db_read_labeled_string</c> (src/db.c) does.
/// </summary>
/// <remarks>
/// A labeled field is a label, whitespace, and a value to the end of the line. A value that starts
/// with <c>"</c> is quoted: it runs to the next unescaped <c>"</c>, may span lines, and escapes
/// <c>"</c> and <c>\</c> with a backslash (<c>putstring</c>). Reading values this way rather than
/// line by line is what keeps a line of an attribute value that happens to begin with <c>!</c> or
/// <c>+</c> from being taken for the next object or section.
/// <para>One of these belongs to one parse; the parser is a singleton.</para>
/// </remarks>
internal sealed class PennMUSHDumpReader(TextReader reader)
{
	private static readonly SearchValues<char> NewLine = SearchValues.Create("\n");
	private static readonly SearchValues<char> QuoteOrEscape = SearchValues.Create("\"\\");

	private readonly char[] _buffer = new char[64 * 1024];
	private int _position;
	private int _length;

	/// <summary>The 1-based line the next character is on, for error messages.</summary>
	public int Line { get; private set; } = 1;

	/// <summary>The next character without consuming it, or -1 at the end.</summary>
	public async ValueTask<int> PeekAsync(CancellationToken cancellationToken)
		=> await FillAsync(cancellationToken) ? _buffer[_position] : -1;

	/// <summary>Takes the rest of the current line, without its line ending; <c>null</c> at the end.</summary>
	public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
	{
		if (await PeekAsync(cancellationToken) == -1)
		{
			return null;
		}

		var line = new StringBuilder();
		await ReadUntilAsync(line, NewLine, cancellationToken);
		await ReadAsync(cancellationToken);
		return line.ToString().TrimEnd('\r');
	}

	/// <summary>Takes one labeled field, skipping any blank lines before it.</summary>
	public async ValueTask<(string Label, string Value)> ReadLabeledAsync(CancellationToken cancellationToken)
	{
		int c;
		while ((c = await ReadAsync(cancellationToken)) != -1 && char.IsWhiteSpace((char)c))
		{
		}

		if (c == -1)
		{
			throw Error("unexpected end of the database");
		}

		var label = new StringBuilder();
		do
		{
			label.Append((char)c);
		} while ((c = await ReadAsync(cancellationToken)) != -1 && !char.IsWhiteSpace((char)c));

		while (c is ' ' or '\t')
		{
			c = await ReadAsync(cancellationToken);
		}

		if (c is -1 or '\n' or '\r')
		{
			throw Error($"no value for '{label}'");
		}

		return (label.ToString(), c == '"'
			? await ReadQuotedAsync(cancellationToken)
			: await ReadBareAsync((char)c, cancellationToken));
	}

	/// <summary>Takes a labeled field that must carry <paramref name="label"/>.</summary>
	public async ValueTask<string> ReadLabeledAsync(string label, CancellationToken cancellationToken)
	{
		var (read, value) = await ReadLabeledAsync(cancellationToken);
		return read == label ? value : throw Error($"expected '{label}', found '{read}'");
	}

	public FormatException Error(string message) => new($"PennMUSH database, line {Line}: {message}.");

	private async ValueTask<string> ReadQuotedAsync(CancellationToken cancellationToken)
	{
		var value = new StringBuilder();
		var start = Line;
		while (true)
		{
			await ReadUntilAsync(value, QuoteOrEscape, cancellationToken);
			var c = await ReadAsync(cancellationToken);
			if (c == '"')
			{
				break;
			}

			if (c == '\\')
			{
				c = await ReadAsync(cancellationToken);
			}

			if (c == -1)
			{
				throw new FormatException($"PennMUSH database, line {start}: a quoted value is never closed.");
			}

			value.Append((char)c);
		}

		// Nothing but whitespace may follow the closing quote on its line.
		int rest;
		while ((rest = await ReadAsync(cancellationToken)) is not -1 and not '\n')
		{
			if (!char.IsWhiteSpace((char)rest))
			{
				throw Error("text after a quoted value");
			}
		}

		return value.ToString();
	}

	private async ValueTask<string> ReadBareAsync(char first, CancellationToken cancellationToken)
	{
		var value = new StringBuilder().Append(first);
		await ReadUntilAsync(value, NewLine, cancellationToken);
		await ReadAsync(cancellationToken);
		return value.ToString().TrimEnd('\r');
	}

	/// <summary>
	/// Appends everything up to, not including, the next of <paramref name="stops"/> or the end, a buffer
	/// at a time. Values are most of a dump, so they are copied in runs rather than a character per await.
	/// </summary>
	private async ValueTask ReadUntilAsync(StringBuilder into, SearchValues<char> stops, CancellationToken cancellationToken)
	{
		while (await FillAsync(cancellationToken))
		{
			var available = _buffer.AsSpan(_position, _length - _position);
			var stop = available.IndexOfAny(stops);
			var run = stop < 0 ? available : available[..stop];
			into.Append(run);
			Line += run.Count('\n');
			_position += run.Length;
			if (stop >= 0)
			{
				return;
			}
		}
	}

	private async ValueTask<int> ReadAsync(CancellationToken cancellationToken)
	{
		if (!await FillAsync(cancellationToken))
		{
			return -1;
		}

		var c = _buffer[_position++];
		if (c == '\n')
		{
			Line++;
		}

		return c;
	}

	private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
	{
		if (_position < _length)
		{
			return true;
		}

		_length = await reader.ReadAsync(_buffer, cancellationToken);
		_position = 0;
		return _length > 0;
	}
}
