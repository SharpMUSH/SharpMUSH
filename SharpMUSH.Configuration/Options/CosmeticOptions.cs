namespace SharpMUSH.Configuration.Options;

public record CosmeticOptions(
	[PennConfig(Name = "money_singular")] string MoneySingular,
	[PennConfig(Name = "money_plural")] string MoneyPlural,
	[PennConfig(Name = "player_name_spaces")] bool PlayerNameSpaces,
	[PennConfig(Name = "ansi_names")] bool AnsiNames,
	[PennConfig(Name = "only_ascii_in_names")] bool OnlyAsciiInNames,
	[PennConfig(Name = "monikers")] bool Monikers,
	[PennConfig(Name = "float_precision")] uint FloatPrecision,
	[PennConfig(Name = "comma_exit_list")] bool CommaExitList,
	[PennConfig(Name = "count_all")] bool CountAll,
	[PennConfig(Name = "page_aliases")] bool PageAliases,
	[PennConfig(Name = "flags_on_examine")] bool FlagsOnExamine,
	[PennConfig(Name = "ex_public_attribs")] bool ExaminePublicAttributes,
	[PennConfig(Name = "wizwall_prefix")] string WizardWallPrefix,
	[PennConfig(Name = "rwall_prefix")] string RoyaltyWallPrefix,
	[PennConfig(Name = "wall_prefix")] string WallPrefix,
	[PennConfig(Name = "announce_connects")] bool AnnounceConnects,
	[PennConfig(Name = "chat_strip_quote")] bool ChatStripQuote
);