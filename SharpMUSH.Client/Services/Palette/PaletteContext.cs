namespace SharpMUSH.Client.Services;

/// <summary>
/// What the page the player is on tells the command palette: its kind (<c>play</c>, <c>scene</c>,
/// <c>wiki</c>, <c>editor</c>, <c>app:&lt;slug&gt;</c>), its subject, and rows of its own. A page sets it as
/// it loads and clears it as it leaves. It also carries a line the palette wants in the Play composer to
/// whichever Play page is listening, or keeps it until one is.
/// </summary>
public sealed class PaletteContext
{
	private Func<string, Task>? _insert;

	public string? Page { get; private set; }

	public string? Subject { get; private set; }

	public IReadOnlyList<PaletteRow> Rows { get; private set; } = [];

	/// <summary>A line waiting for the Play page to put it into its composer.</summary>
	public string? PendingInsert { get; private set; }

	/// <summary>The current page names itself, and the rows it adds.</summary>
	public void Set(string page, string? subject = null, IReadOnlyList<PaletteRow>? rows = null)
	{
		Page = page;
		Subject = subject;
		Rows = rows ?? [];
	}

	/// <summary>The page leaves; nothing changes when another page has set the context since.</summary>
	public void Clear(string page)
	{
		if (Page != page) return;
		Page = null;
		Subject = null;
		Rows = [];
	}

	/// <summary>The Play page takes lines into its composer while it is open.</summary>
	public IDisposable ListenForInsert(Func<string, Task> insert)
	{
		_insert = insert;
		return new Listening(this, insert);
	}

	/// <summary>Puts <paramref name="line"/> into the composer; false when no Play page is open to take it.</summary>
	public async Task<bool> InsertAsync(string line)
	{
		if (_insert is { } insert)
		{
			await insert(line);
			return true;
		}

		PendingInsert = line;
		return false;
	}

	/// <summary>The waiting line, once: the Play page takes it as it opens.</summary>
	public string? TakePendingInsert()
	{
		var line = PendingInsert;
		PendingInsert = null;
		return line;
	}

	private sealed class Listening(PaletteContext owner, Func<string, Task> insert) : IDisposable
	{
		public void Dispose()
		{
			if (owner._insert == insert) owner._insert = null;
		}
	}
}
