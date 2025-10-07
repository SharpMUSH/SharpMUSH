namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[property: SharpConfig(Name = "money_singular", Category = "Cosmetic", Description = "Singular form of money (e.g., 'penny')")] string MoneySingular,
	[property: SharpConfig(Name = "money_plural", Category = "Cosmetic", Description = "Plural form of money (e.g., 'pennies')")] string MoneyPlural,
	[property: SharpConfig(Name = "player_name_spaces", Category = "Cosmetic", Description = "Allow spaces in player names")] bool PlayerNameSpaces,
	[property: SharpConfig(Name = "ansi_names", Category = "Cosmetic", Description = "Allow ANSI color codes in player names")] bool AnsiNames,
	[property: SharpConfig(Name = "only_ascii_in_names", Category = "Cosmetic", Description = "Restrict names to ASCII characters only")] bool OnlyAsciiInNames,
	[property: SharpConfig(Name = "monikers", Category = "Cosmetic", Description = "Enable moniker (nickname) system for players")] bool Monikers,
	[property: SharpConfig(Name = "float_precision", Category = "Cosmetic", Description = "Number of decimal places for floating-point display", ValidationPattern = @"^\d+$")] uint FloatPrecision,
	[property: SharpConfig(Name = "comma_exit_list", Category = "Cosmetic", Description = "Use commas to separate exit names in lists")] bool CommaExitList,
	[property: SharpConfig(Name = "count_all", Category = "Cosmetic", Description = "Include all objects in @count command results")] bool CountAll,
	[property: SharpConfig(Name = "page_aliases", Category = "Cosmetic", Description = "Allow @page command to use player aliases")] bool PageAliases,
	[property: SharpConfig(Name = "flags_on_examine", Category = "Cosmetic", Description = "Show object flags when using examine command")] bool FlagsOnExamine,
	[property: SharpConfig(Name = "ex_public_attribs", Category = "Cosmetic", Description = "Show public attributes in examine command")] bool ExaminePublicAttributes,
	[property: SharpConfig(Name = "wizwall_prefix", Category = "Cosmetic", Description = "Prefix text for wizard wall messages")] string WizardWallPrefix,
	[property: SharpConfig(Name = "rwall_prefix", Category = "Cosmetic", Description = "Prefix text for royalty wall messages")] string RoyaltyWallPrefix,
	[property: SharpConfig(Name = "wall_prefix", Category = "Cosmetic", Description = "Prefix text for general wall messages")] string WallPrefix,
	[property: SharpConfig(Name = "announce_connects", Category = "Cosmetic", Description = "Announce when players connect to the MUSH")] bool AnnounceConnects,
	[property: SharpConfig(Name = "chat_strip_quote", Category = "Cosmetic", Description = "Remove quote marks from chat messages")] bool ChatStripQuote
);