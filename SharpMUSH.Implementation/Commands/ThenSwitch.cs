using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>What a command given <c>/then</c> says when its right side names nothing to run afterwards.</summary>
	private const string ThenNeedsACommand = "What do you want to run afterwards? Put it after the last comma.";

	/// <summary>
	/// The right side of a command given <c>/then</c>, split at its last comma outside braces, brackets and
	/// parentheses: what comes before it, and the command list to run afterwards, as written. None when
	/// there is no such comma.
	/// </summary>
	private static (MString Before, MString After)? SplitThen(MString text)
	{
		var raw = text.Text;
		var depth = 0;
		var split = -1;
		for (var index = 0; index < raw.Length; index++)
		{
			switch (raw[index])
			{
				case '\\' or '%':
					index++;
					break;
				case '{' or '[' or '(':
					depth++;
					break;
				case '}' or ']' or ')' when depth > 0:
					depth--;
					break;
				case ',' when depth == 0:
					split = index;
					break;
			}
		}

		return split < 0
			? null
			: (text.Substring(0, split), text.Substring(split + 1, raw.Length - split - 1));
	}

	/// <summary>
	/// <paramref name="text"/> without its outer spaces and without the braces at its top level, as a command
	/// argument split without evaluation loses them: <c>{a;b}; c</c> is <c>a;b; c</c>. Braces inside
	/// brackets or a function's parentheses belong to that call and stay.
	/// </summary>
	private static MString Unbrace(MString text)
	{
		var raw = text.Text;
		var start = 0;
		var end = raw.Length;
		while (start < end && raw[start] == ' ') start++;
		while (end > start && raw[end - 1] == ' ') end--;

		var kept = new List<(int Start, int Length)>();
		var depth = 0;
		var nested = 0;
		var from = start;
		for (var index = start; index < end; index++)
		{
			switch (raw[index])
			{
				case '\\' or '%':
					index++;
					break;
				case '[' or '(' when depth == 0:
					nested++;
					break;
				case ']' or ')' when depth == 0 && nested > 0:
					nested--;
					break;
				case '{' or '}' when nested > 0:
					break;
				case '{':
					if (depth++ == 0)
					{
						kept.Add((from, index - from));
						from = index + 1;
					}
					break;
				case '}' when depth > 0:
					if (--depth == 0)
					{
						kept.Add((from, index - from));
						from = index + 1;
					}
					break;
			}
		}

		if (kept.Count == 0) return text.Substring(start, end - start);
		kept.Add((from, end - from));
		return MarkupText.Concat(kept.Where(part => part.Length > 0).Select(part => text.Substring(part.Start, part.Length)));
	}

	/// <summary>The state a <c>/then</c> command list runs with: the caller's, with how many runs it followed as %0.</summary>
	private static ParserState ThenState(ParserState state, int count)
		=> state.ForAttributeArguments(new Dictionary<string, CallState> { ["0"] = new(count.ToString()) });
}
