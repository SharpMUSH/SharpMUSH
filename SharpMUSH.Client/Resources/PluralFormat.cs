using System.Globalization;
using Jeffijoe.MessageFormat;
using Microsoft.Extensions.Localization;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// Renders count-bearing resource values written as ICU MessageFormat, so a locale gets every plural
/// category its grammar needs rather than English's two.
/// </summary>
/// <remarks>
/// <para>
/// <c>.resx</c> holds one string per key and <c>string.Format</c> substitutes positionally; neither knows
/// anything about grammatical number. English needs two forms, so the portal used to fake it with
/// <c>"{0} pose(s)"</c> and one/many key pairs. Neither survives translation: Russian, Croatian and
/// Romanian need three categories, Polish four, and Chinese one — so English's two are actively wrong at
/// both ends. <c>PkgUninstallConfirmBody</c> carries two independent counts in one sentence, which key
/// pairs cannot express at all (sixteen combinations in Polish).
/// </para>
/// <para>
/// Use this for any value whose wording changes with a number. Use the plain indexer for everything
/// else — most <c>{0}</c> placeholders interpolate a name or an error message, not a count, and wrapping
/// those in a plural pattern would be noise.
/// </para>
/// </remarks>
public static class PluralFormat
{
	// MessageFormatter is documented as thread-safe for formatting and caches parsed patterns, so one
	// shared instance is both correct and the point — reparsing on every render would be wasteful.
	private static readonly MessageFormatter Formatter = new(useCache: true);

	/// <summary>
	/// Renders <paramref name="key"/>'s ICU MessageFormat value against the current UI culture with a
	/// single named argument.
	/// </summary>
	public static string Plural(
		this IStringLocalizer<SharedResource> loc, string key, string argName, object value)
		=> Format(loc[key].Value, new Dictionary<string, object?> { [argName] = value });

	/// <summary>
	/// Two named arguments, for a value carrying two independent counts in one sentence.
	/// </summary>
	public static string Plural(
		this IStringLocalizer<SharedResource> loc,
		string key,
		string firstName,
		object firstValue,
		string secondName,
		object secondValue)
		=> Format(loc[key].Value, new Dictionary<string, object?>
		{
			[firstName] = firstValue,
			[secondName] = secondValue
		});

	/// <summary>
	/// Arbitrary named arguments, for a value mixing counts with plain substitutions.
	/// </summary>
	public static string Plural(
		this IStringLocalizer<SharedResource> loc, string key, IReadOnlyDictionary<string, object?> arguments)
		=> Format(loc[key].Value, arguments);

	// CurrentUICulture, matching IStringLocalizer: the same culture that chose which satellite the pattern
	// came from must choose the plural rules applied to it, or a Russian value gets English categories.
	/// <summary>
	/// Renders a pattern directly, bypassing the resx. For tests that need to prove category selection
	/// for a locale with no satellite yet — going through the localizer would fetch the English
	/// two-category value and apply the other locale's rules to it, which cannot observe a third form.
	/// </summary>
	public static string Render(string pattern, string argName, object value)
		=> Format(pattern, new Dictionary<string, object?> { [argName] = value });

	private static string Format(string pattern, IReadOnlyDictionary<string, object?> arguments)
		=> Formatter.FormatMessage(pattern, arguments, CultureInfo.CurrentUICulture);
}
