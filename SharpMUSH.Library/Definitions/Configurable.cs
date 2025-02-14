namespace SharpMUSH.Library.Definitions;

public static class Configurable
{
	public static readonly Dictionary<string, string[]> FunctionAliases = new()
	{
		{ "atrlock", ["attrlock"] },
		{ "iter", ["parse"] },
		{ "lsearch", ["search"] },
		{ "lstats", ["stats"] },
		{ "lthings", ["lobjects"] },
		{ "lvthings", ["lvobjects"] },
		{ "modulo", ["mod", "modulus"] },
		{ "nattr", ["attrcnt"] },
		{ "nattrp", ["attrpcnt"] },
		{ "nthings", ["nobjects"] },
		{ "nvthings", ["nvobjects"] },
		{ "randword", ["pickrand"] },
		{ "soundslike", ["soundlike"] },
		{ "textfile", ["dynhelp"] },
		{ "trunc", ["val"] },
		{ "ufun", ["u"] },
		{ "xthings", ["xobjects"] },
		{ "xvthings", ["xvobjects"] }
	};

	public static readonly Dictionary<string, string[]> CommandAliases = new()
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
	};
}