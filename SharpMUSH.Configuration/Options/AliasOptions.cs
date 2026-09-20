namespace SharpMUSH.Configuration.Options;

public record AliasOptions(
	[property: SharpConfig(
		Name = "function_aliases",
		Category = "Alias",
		Description = "Function name aliases mapping",
		Group = "Function Aliases",
		Order = 1)]
	Dictionary<string, string[]> FunctionAliases,

	[property: SharpConfig(
		Name = "command_aliases",
		Category = "Alias",
		Description = "Command name aliases mapping",
		Group = "Command Aliases",
		Order = 1)]
	Dictionary<string, string[]> CommandAliases
)
{
	/// <summary>
	/// The aliases PennMUSH ships, and the one place they are written down. Every entry has a help
	/// topic that says in so many words that it is another name for the function or command it maps
	/// to, so the shipped help and this table are two halves of one contract — which is why
	/// <c>HelpRegistryParityTests</c> compares them.
	/// </summary>
	/// <remarks>
	/// There were three copies of this: here, <c>Configurable</c>'s field initializer, and
	/// <c>ReadPennMushConfig.Create</c>. They had already diverged — eight aliases were documented and
	/// registered nowhere — and the import path silently won, so a game that read a PennMUSH
	/// <c>mush.cnf</c> lost whichever aliases the other two copies had gained.
	/// </remarks>
	public static AliasOptions Default => new(
		FunctionAliases: new Dictionary<string, string[]>
		{
			{ "atrlock", ["attrlock"] },
			{ "e", ["exp"] },
			{ "flip", ["reverse"] },
			{ "host", ["hostname"] },
			{ "iter", ["parse"] },
			{ "lreplace", ["replace"] },
			{ "lsearch", ["search"] },
			{ "lstats", ["stats"] },
			{ "lthings", ["lobjects"] },
			{ "lvthings", ["lvobjects"] },
			{ "match", ["element"] },
			{ "mean", ["avg"] },
			{ "modulo", ["mod", "modulus"] },
			{ "moniker", ["cname"] },
			{ "nattr", ["attrcnt"] },
			{ "nattrp", ["attrpcnt"] },
			{ "nthings", ["nobjects"] },
			{ "nvthings", ["nvobjects"] },
			{ "randword", ["pickrand"] },
			{ "soundslike", ["soundlike"] },
			{ "speak", ["speakpenn"] },
			{ "strdelete", ["delete"] },
			{ "textfile", ["dynhelp"] },
			{ "trunc", ["val"] },
			{ "ufun", ["u"] },
			{ "xthings", ["xobjects"] },
			{ "xvthings", ["xvobjects"] }
		},
		CommandAliases: new Dictionary<string, string[]>
		{
			{ "@ATRLOCK", ["@attrlock"] },
			{ "@ATRCHOWN", ["@attrchown"] },
			{ "@EDIT", ["@gedit"] },
			{ "@IFELSE", ["@if"] },
			{ "@SWITCH", ["@sw"] },
			{ "GET", ["take"] },
			{ "GOTO", ["move"] },
			{ "INVENTORY", ["i"] },
			{ "LOOK", ["l"] },
			{ "PAGE", ["p"] },
			{ "WHISPER", ["w"] }
		});
}
