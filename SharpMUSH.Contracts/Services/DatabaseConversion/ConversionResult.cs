namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// Result of a database conversion operation
/// </summary>
public record ConversionResult
{
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
	public TimeSpan Duration { get; init; }

	public int TotalObjects => PlayersConverted + RoomsConverted + ThingsConverted + ExitsConverted;

	/// <summary>
	/// The conversion stopped part-way, cancelled or on a fatal error, rather than reaching the end with some
	/// errors along the way: the world holds only part of the database.
	/// </summary>
	public bool Aborted { get; init; }

	public bool IsSuccessful => Errors.Count == 0;
}
