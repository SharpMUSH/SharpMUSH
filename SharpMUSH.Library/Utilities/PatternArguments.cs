using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Utilities;

/// <summary>Captures styled positional arguments using plain-text match offsets. Wildcards start
/// at %0 with their first capture; regexps retain the whole match at %0, number their groups as PCRE
/// does (<see cref="SoftcodeRegex.PcreGroupNumbers"/>), and keep named groups by name.</summary>
public static class PatternArguments
{
	public static Dictionary<string, CallState> Capture(Regex regex, bool isRegex, MString text)
		=> SoftcodeRegex.Match(regex, text.ToPlainText()) is { Success: true } match ? Capture(regex, match, isRegex, text) : [];

	public static Dictionary<string, CallState> Capture(Regex regex, Match match, bool isRegex, MString text)
	{
		var arguments = new Dictionary<string, CallState>();
		foreach (var (index, group) in Groups(regex, match, isRegex).Index().Skip(isRegex ? 0 : 1))
		{
			var captured = text.Substring(group.Index, group.Length);
			if (isRegex)
			{
				arguments.TryAdd(index.ToString(), new CallState(captured, 0));
				if (!int.TryParse(group.Name, out _)) arguments.TryAdd(group.Name, new CallState(captured, 0));
			}
			else arguments.TryAdd((index - 1).ToString(), new CallState(captured, 0));
		}
		return arguments;
	}

	/// <summary>
	/// <paramref name="match"/>'s groups in the order softcode numbers them: PCRE's for a regexp, and
	/// for a wildcard, whose groups are all unnamed, the same order .NET gives them.
	/// </summary>
	public static IEnumerable<Group> Groups(Regex regex, Match match, bool isRegex)
		=> isRegex
			? SoftcodeRegex.PcreGroupNumbers(regex).Select(number => match.Groups[number])
			: match.Groups.Values;
}
