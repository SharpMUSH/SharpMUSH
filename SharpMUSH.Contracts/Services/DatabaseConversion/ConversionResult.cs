namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Result of a database conversion operation
/// </summary>
public record ConversionResult
{
	/// <summary>The durable isolated preview identifier, when this result came from the production coordinator.</summary>
	public string? ImportSessionId { get; init; }
	/// <summary>A successful preview still needs separate explicit promotion and a process restart.</summary>
	public bool RequiresPromotion { get; init; }

	/// <summary>The server the database came from, as <c>RhostMUSH</c>; empty when the file was never read.</summary>
	public string SourceName { get; init; } = "";

	public int PlayersConverted { get; init; }
	public int RoomsConverted { get; init; }
	public int ThingsConverted { get; init; }
	public int ExitsConverted { get; init; }
	public int AttributesConverted { get; init; }
	public int LocksConverted { get; init; }
	public int MailAliasesConverted { get; init; }
	public int MailMessagesConverted { get; init; }
	public int ChannelsConverted { get; init; }
	public int ChannelMembersConverted { get; init; }
	public List<string> Errors { get; init; } = [];
	public List<string> Warnings { get; init; } = [];

	/// <summary>What every import of this kind does, reported for the record rather than as a warning.</summary>
	public List<string> Notes { get; init; } = [];

	/// <summary>
	/// Every attribute whose softcode the import rewrote so SharpMUSH reads it as the source server did, with its
	/// text before and after. Empty for a PennMUSH import, whose softcode is kept as written.
	/// </summary>
	public List<SoftcodeRewrite> SoftcodeRewrites { get; init; } = [];
	public TimeSpan Duration { get; init; }

	public int TotalObjects => PlayersConverted + RoomsConverted + ThingsConverted + ExitsConverted;

	/// <summary>
	/// The conversion stopped part-way, cancelled or on a fatal error, rather than reaching the end with some
	/// errors along the way: the world holds only part of the database.
	/// </summary>
	public bool Aborted { get; init; }

	public bool IsSuccessful => !Aborted && Errors.Count == 0;
}

/// <summary>Safe journal discovery; excludes private paths, source configuration, and key material.</summary>
/// <param name="Started">When the preview was started; unknown for an import that predates it.</param>
/// <param name="SourceFile">The database's file name; unknown for an import that predates it.</param>
public sealed record ImportSessionStatus(string SessionId, string State, ConversionResult? Result, string? Failure,
	DateTimeOffset? Started = null, string? SourceFile = null);

/// <summary>Whether the world the last promotion replaced is still kept, which stops another promotion.</summary>
public sealed record PreviousWorldStatus(bool Retained);
