namespace SharpMUSH.Configuration.Options;

/// <summary>
/// The MSSP variables an administrator sets: what crawlers read about the game that the server cannot
/// work out for itself (its genre, contact address, Discord, and so on). The ones the server can —
/// <c>NAME</c>, <c>PLAYERS</c>, <c>UPTIME</c>, <c>PORT</c> and the rest of
/// <see cref="Mssp.MsspCatalog.ServerReported"/> — are never stored here.
/// </summary>
/// <remarks>
/// PennMUSH keeps these as <c>mssp name/value</c> lines in <c>mush.cnf</c>
/// (<c>game/mushcnf.dst</c>); <see cref="ReadPennMushConfig"/> reads them into this.
/// </remarks>
public record MsspOptions(
	[property: SharpConfig(
		Name = "mssp",
		Category = "Mssp",
		Description = "Game details reported to MUD crawlers through MSSP, by variable name",
		Group = "Crawler Listing",
		Order = 1,
		Tooltip = "Format: MSSP variable → values, the default value last")]
	Dictionary<string, string[]> Variables
);
