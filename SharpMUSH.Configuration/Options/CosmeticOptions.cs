namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[property: SharpConfig(
		Name = "money_singular",
		Category = "Cosmetic",
		Description = "Singular form of money (e.g., 'penny')",
		Group = "Currency",
		Order = 1)]
	string MoneySingular,
	
	[property: SharpConfig(
		Name = "money_plural",
		Category = "Cosmetic",
		Description = "Plural form of money (e.g., 'pennies')",
		Group = "Currency",
		Order = 2)]
	string MoneyPlural,
	
	[property: SharpConfig(
		Name = "player_name_spaces",
		Category = "Cosmetic",
		Description = "Allow spaces in player names",
		Group = "Names",
		Order = 1)]
	bool PlayerNameSpaces,
	
	[property: SharpConfig(
		Name = "ansi_names",
		Category = "Cosmetic",
		Description = "Allow ANSI color codes in player names",
		Group = "Names",
		Order = 2)]
	bool AnsiNames,
	
	[property: SharpConfig(
		Name = "only_ascii_in_names",
		Category = "Cosmetic",
		Description = "Restrict names to ASCII characters only",
		Group = "Names",
		Order = 3)]
	bool OnlyAsciiInNames,
	
	[property: SharpConfig(
		Name = "monikers",
		Category = "Cosmetic",
		Description = "Enable moniker (nickname) system for players",
		Group = "Names",
		Order = 4)]
	bool Monikers,
	
	[property: SharpConfig(
		Name = "float_precision",
		Category = "Cosmetic",
		Description = "Number of decimal places for floating-point display",
		ValidationPattern = @"^\d+$",
		Group = "Display",
		Order = 1,
		Min = 0,
		Max = 15)]
	uint FloatPrecision,
	
	[property: SharpConfig(
		Name = "comma_exit_list",
		Category = "Cosmetic",
		Description = "Use commas to separate exit names in lists",
		Group = "Display",
		Order = 2)]
	bool CommaExitList,
	
	[property: SharpConfig(
		Name = "count_all",
		Category = "Cosmetic",
		Description = "Include all objects in @count command results",
		Group = "Display",
		Order = 3)]
	bool CountAll,
	
	[property: SharpConfig(
		Name = "page_aliases",
		Category = "Cosmetic",
		Description = "Allow @page command to use player aliases",
		Group = "Names",
		Order = 5)]
	bool PageAliases,
	
	[property: SharpConfig(
		Name = "flags_on_examine",
		Category = "Cosmetic",
		Description = "Show object flags when using examine command",
		Group = "Display",
		Order = 4)]
	bool FlagsOnExamine,
	
	[property: SharpConfig(
		Name = "ex_public_attribs",
		Category = "Cosmetic",
		Description = "Show public attributes in examine command",
		Group = "Display",
		Order = 5)]
	bool ExaminePublicAttributes,
	
	[property: SharpConfig(
		Name = "wizwall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for wizard wall messages",
		Group = "Announcements",
		Order = 1)]
	string WizardWallPrefix,
	
	[property: SharpConfig(
		Name = "rwall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for royalty wall messages",
		Group = "Announcements",
		Order = 2)]
	string RoyaltyWallPrefix,
	
	[property: SharpConfig(
		Name = "wall_prefix",
		Category = "Cosmetic",
		Description = "Prefix text for general wall messages",
		Group = "Announcements",
		Order = 3)]
	string WallPrefix,
	
	[property: SharpConfig(
		Name = "announce_connects",
		Category = "Cosmetic",
		Description = "Announce when players connect to the MUSH",
		Group = "Announcements",
		Order = 4)]
	bool AnnounceConnects,
	
	[property: SharpConfig(
		Name = "chat_strip_quote",
		Category = "Cosmetic",
		Description = "Remove quote marks from chat messages",
		Group = "Announcements",
		Order = 5)]
	bool ChatStripQuote
);
