using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// Resolves which <see cref="ParseType"/> a piece of softcode should be analysed as.
///
/// Two independent channels, one shared rule:
/// <list type="bullet">
/// <item><see cref="ForFileName"/> — the Language Server's channel: a file's extension decides
/// the mode (a <c>.mush</c>/<c>.mu</c> batch is a command list; a <c>.mushfn</c>/<c>.fun</c> file
/// is a function/expression).</item>
/// <item><see cref="FromName"/> — the MCP tools' channel: the caller names the mode explicitly.</item>
/// </list>
/// Both fall back to <see cref="ParseType.Function"/> when nothing else applies.
/// </summary>
public static class MushParseMode
{
	public static ParseType ForFileName(string fileNameOrUri, ParseType fallback = ParseType.Function)
	{
		var lastDot = fileNameOrUri.LastIndexOf('.');
		if (lastDot < 0) return fallback;

		var extension = fileNameOrUri[(lastDot + 1)..].ToLowerInvariant();
		return extension switch
		{
			"mush" or "mu" => ParseType.CommandList,
			"mushfn" or "fun" => ParseType.Function,
			_ => fallback
		};
	}

	public static ParseType FromName(string? name, ParseType fallback = ParseType.Function)
		=> name?.Trim().ToLowerInvariant() switch
		{
			"function" => ParseType.Function,
			"command" => ParseType.Command,
			"commandlist" or "command-list" or "command_list" => ParseType.CommandList,
			null or "" => fallback,
			_ => fallback
		};
}
