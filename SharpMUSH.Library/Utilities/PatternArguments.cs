using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Utilities;

/// <summary>Captures styled positional arguments using plain-text match offsets. Wildcards start
/// at %0 with their first capture; regexps retain the whole match at %0 and named groups.</summary>
public static class PatternArguments
{
	public static Dictionary<string, CallState> Capture(Regex regex, bool isRegex, MString text)
		=> SoftcodeRegex.Match(regex, text.ToPlainText()) is { Success: true } match ? Capture(match, isRegex, text) : [];

	public static Dictionary<string, CallState> Capture(Match match, bool isRegex, MString text)
	{
		var arguments = new Dictionary<string, CallState>();
		foreach (var (index, group) in match.Groups.Values.Index().Skip(isRegex ? 0 : 1))
		{
			var captured = text.Substring(group.Index, group.Length);
			if (isRegex)
			{
				arguments.TryAdd(index.ToString(), new CallState(captured, 0));
				arguments.TryAdd(group.Name, new CallState(captured, 0));
			}
			else arguments.TryAdd((index - 1).ToString(), new CallState(captured, 0));
		}
		return arguments;
	}
}
