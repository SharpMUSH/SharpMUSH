namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// A <see cref="StreamReader"/> with one line of pushback, which is all the PennMUSH database
/// format needs to tell an object marker from the attribute value it is reading.
/// </summary>
/// <remarks>
/// One of these belongs to one parse. The buffer used to be a field on
/// <see cref="PennMUSHDatabaseParser"/>, which is a singleton, so two parses running at once shared
/// it: one would consume the line the other had peeked, and both would read a database that never
/// existed.
/// </remarks>
internal sealed class PennMUSHLineReader(StreamReader reader)
{
	private string? _nextLine;

	/// <summary>Takes the next line, consuming any line already peeked.</summary>
	public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
	{
		if (_nextLine is null)
		{
			return await reader.ReadLineAsync(cancellationToken);
		}

		var line = _nextLine;
		_nextLine = null;
		return line;
	}

	/// <summary>Looks at the next line without consuming it.</summary>
	public async Task<string?> PeekLineAsync(CancellationToken cancellationToken)
		=> _nextLine ??= await reader.ReadLineAsync(cancellationToken);
}
