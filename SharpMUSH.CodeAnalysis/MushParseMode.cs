namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// Resolves which <see cref="MushAnalysisMode"/> a piece of softcode should be analysed as.
///
/// Two independent channels, one shared rule:
/// <list type="bullet">
/// <item><see cref="ForFileName"/> — the Language Server's channel: a file's extension decides
/// the mode. A real-world <c>.mush</c>/<c>.mu</c> file is one command per line
/// (<see cref="MushAnalysisMode.CommandsPerLine"/>); a <c>.mushfn</c>/<c>.fun</c> file is a
/// function/expression; a <c>.mushcmd</c> file is a single command list.</item>
/// <item><see cref="FromName"/> — the MCP tools' channel: the caller names the mode explicitly.</item>
/// </list>
/// Both fall back to <see cref="MushAnalysisMode.Function"/> when nothing else applies.
/// </summary>
public static class MushParseMode
{
	public static MushAnalysisMode ForFileName(string fileNameOrUri, MushAnalysisMode fallback = MushAnalysisMode.Function)
	{
		var lastDot = fileNameOrUri.LastIndexOf('.');
		if (lastDot < 0) return fallback;

		var extension = fileNameOrUri[(lastDot + 1)..].ToLowerInvariant();
		return extension switch
		{
			"mush" or "mu" => MushAnalysisMode.CommandsPerLine,
			"mushfn" or "fun" => MushAnalysisMode.Function,
			"mushcmd" => MushAnalysisMode.CommandList,
			_ => fallback
		};
	}

	public static MushAnalysisMode FromName(string? name, MushAnalysisMode fallback = MushAnalysisMode.Function)
		=> name?.Trim().ToLowerInvariant() switch
		{
			"function" => MushAnalysisMode.Function,
			"command" => MushAnalysisMode.Command,
			"commandlist" or "command-list" or "command_list" => MushAnalysisMode.CommandList,
			"commandsperline" or "commands-per-line" or "commandperline" or "mushfile"
				=> MushAnalysisMode.CommandsPerLine,
			null or "" => fallback,
			_ => fallback
		};
}
