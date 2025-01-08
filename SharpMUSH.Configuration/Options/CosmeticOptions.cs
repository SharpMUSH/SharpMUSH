namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	string MoneySingular = "Penny",
	string MoneyPlural = "Pennies",
	bool PlayerNameSpaces = true,
	bool AnsiNames = true,
	bool OnlyAsciiInNames = true,
	bool Monikers = true,
	int FloatPrecision = 6,
	bool CommaExitList = true,
	bool CountAll = false,
	bool PageAliases = false,
	bool FlagsOnExamine = true,
	bool ExPublicAttribs = true,
	string WizardWallPrefix = "Broadcast: ",
	string RoyaltyWallPrefix = "Admin: ",
	string WallPrefix = "Announcement: ",
	bool AnnounceConnects = true,
	bool ChatStripQuote = true
);