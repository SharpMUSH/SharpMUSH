namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[property: PennConfig(Name = "money_singular")] string MoneySingular,
	[property: PennConfig(Name = "money_plural")] string MoneyPlural,
	[property: PennConfig(Name = "player_name_spaces")] bool PlayerNameSpaces,
	[property: PennConfig(Name = "ansi_names")] bool AnsiNames,
	[property: PennConfig(Name = "only_ascii_in_names")] bool OnlyAsciiInNames,
	[property: PennConfig(Name = "monikers")] bool Monikers,
	[property: PennConfig(Name = "float_precision")] uint FloatPrecision,
	[property: PennConfig(Name = "comma_exit_list")] bool CommaExitList,
	[property: PennConfig(Name = "count_all")] bool CountAll,
	[property: PennConfig(Name = "page_aliases")] bool PageAliases,
	[property: PennConfig(Name = "flags_on_examine")] bool FlagsOnExamine,
	[property: PennConfig(Name = "ex_public_attribs")] bool ExaminePublicAttributes,
	[property: PennConfig(Name = "wizwall_prefix")] string WizardWallPrefix,
	[property: PennConfig(Name = "rwall_prefix")] string RoyaltyWallPrefix,
	[property: PennConfig(Name = "wall_prefix")] string WallPrefix,
	[property: PennConfig(Name = "announce_connects")] bool AnnounceConnects,
	[property: PennConfig(Name = "chat_strip_quote")] bool ChatStripQuote
);