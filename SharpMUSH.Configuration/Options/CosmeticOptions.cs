namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[property: PennConfig(Name = "money_singular", Description = "Singular form of money (e.g., 'penny')")] string MoneySingular,
	[property: PennConfig(Name = "money_plural", Description = "Plural form of money (e.g., 'pennies')")] string MoneyPlural,
	[property: PennConfig(Name = "player_name_spaces", Description = "Allow spaces in player names")] bool PlayerNameSpaces,
	[property: PennConfig(Name = "ansi_names", Description = "Allow ANSI color codes in player names")] bool AnsiNames,
	[property: PennConfig(Name = "only_ascii_in_names", Description = "Restrict names to ASCII characters only")] bool OnlyAsciiInNames,
	[property: PennConfig(Name = "monikers", Description = "Enable moniker (nickname) system for players")] bool Monikers,
	[property: PennConfig(Name = "float_precision", Description = "Number of decimal places for floating-point display")] uint FloatPrecision,
	[property: PennConfig(Name = "comma_exit_list", Description = "Use commas to separate exit names in lists")] bool CommaExitList,
	[property: PennConfig(Name = "count_all", Description = "Include all objects in @count command results")] bool CountAll,
	[property: PennConfig(Name = "page_aliases", Description = "Allow @page command to use player aliases")] bool PageAliases,
	[property: PennConfig(Name = "flags_on_examine", Description = "Show object flags when using examine command")] bool FlagsOnExamine,
	[property: PennConfig(Name = "ex_public_attribs", Description = "Show public attributes in examine command")] bool ExaminePublicAttributes,
	[property: PennConfig(Name = "wizwall_prefix", Description = "Prefix text for wizard wall messages")] string WizardWallPrefix,
	[property: PennConfig(Name = "rwall_prefix", Description = "Prefix text for royalty wall messages")] string RoyaltyWallPrefix,
	[property: PennConfig(Name = "wall_prefix", Description = "Prefix text for general wall messages")] string WallPrefix,
	[property: PennConfig(Name = "announce_connects", Description = "Announce when players connect to the MUSH")] bool AnnounceConnects,
	[property: PennConfig(Name = "chat_strip_quote", Description = "Remove quote marks from chat messages")] bool ChatStripQuote
);