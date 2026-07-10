using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// How a piece of softcode should be analysed. Unlike a raw <see cref="ParseType"/>, this also
/// captures whether the buffer is parsed as one unit or line-by-line.
/// </summary>
public enum MushAnalysisMode
{
	/// <summary>The whole buffer is one function/expression.</summary>
	Function,

	/// <summary>The whole buffer is one command list (e.g. <c>;</c>-separated commands).</summary>
	CommandList,

	/// <summary>The whole buffer is a single command.</summary>
	Command,

	/// <summary>
	/// Each non-blank line is its own single command — how a real-world <c>.mush</c> upload /
	/// quote file is executed (one command per line). Diagnostics are produced per line and
	/// offset back to the file.
	/// </summary>
	CommandsPerLine
}

public static class MushAnalysisModeExtensions
{
	/// <summary>
	/// The per-unit <see cref="ParseType"/> this mode parses with. For
	/// <see cref="MushAnalysisMode.CommandsPerLine"/> each line is a single
	/// <see cref="ParseType.Command"/>.
	/// </summary>
	public static ParseType ToParseType(this MushAnalysisMode mode) => mode switch
	{
		MushAnalysisMode.CommandList => ParseType.CommandList,
		MushAnalysisMode.Command => ParseType.Command,
		MushAnalysisMode.CommandsPerLine => ParseType.Command,
		_ => ParseType.Function
	};
}
