using System.Text.RegularExpressions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>
/// PennMUSH's <c>check_attr_value</c> (<c>src/atr_tab.c:504-600</c>): what an attribute's
/// <c>@attribute/limit</c> regexp and <c>@attribute/enum</c> choices make of a value about to be set.
/// <c>do_set_atr</c> runs it before every player-facing set, and <c>valid(attrvalue, …)</c> asks it the
/// same question.
/// </summary>
public static class AttributeValueRestriction
{
	/// <summary>
	/// The value to store — <paramref name="value"/> itself, or the enum choice it names spelled as the
	/// enum spells it — or the message Penn gives for refusing it.
	/// </summary>
	/// <remarks>
	/// The limit regexp is caseless, as Penn compiles it with <c>PCRE2_CASELESS</c>, and one that does not
	/// compile limits nothing, as in Penn. An enum match is caseless too: an exact choice wins, otherwise
	/// the first choice <paramref name="value"/> begins. An empty value, or one holding the delimiter, names
	/// no choice.
	/// </remarks>
	public static Result<string> Check(SharpAttributeEntry entry, string value)
	{
		if (!string.IsNullOrEmpty(entry.Limit) && !MatchesLimit(entry.Limit, value))
			return new Error<string>(ErrorMessages.Notifications.AttributeValueFailsLimit);

		if (entry.Enum is not { Length: > 0 } choices)
			return value;

		var choice = value.Length == 0 || value.Contains(entry.EnumDelimiter)
			? null
			: choices.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase))
				?? choices.FirstOrDefault(c => c.StartsWith(value, StringComparison.OrdinalIgnoreCase));

		return choice is not null
			? choice
			: new Error<string>(string.Format(ErrorMessages.Notifications.AttributeValueNotInEnumFormat,
				entry.Name, string.Join(entry.EnumDelimiter, choices)));
	}

	private static bool MatchesLimit(string limit, string value)
	{
		try
		{
			return SoftcodeRegex.IsMatch(SoftcodeRegex.Create(limit, RegexOptions.IgnoreCase), value);
		}
		catch (ArgumentException)
		{
			return true;
		}
	}
}
