using System.Globalization;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// Validates SharpMUSH configuration options by delegating to the code-generated validator, plus the
/// hand-written checks the generator cannot express.
/// </summary>
public class ValidateSharpOptions : IValidateOptions<SharpMUSHOptions>
{
	private readonly ValidateSharpMUSHOptions _generatedValidator = new();

	public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
	{
		var generated = _generatedValidator.Validate(name, options);

		var failures = new List<string>();
		if (generated.Failed && generated.Failures is not null) failures.AddRange(generated.Failures);

		// wiki_default_locale is the terminal step of wiki locale resolution, so an unusable value would
		// otherwise surface as a CultureNotFoundException inside a page render. The ValidationPattern on
		// the attribute is a client-side syntax check only; a regex cannot know which tags actually exist.
		if (!IsRealCulture(options.Wiki.DefaultLocale))
		{
			failures.Add(
				$"Wiki.DefaultLocale (wiki_default_locale) is '{options.Wiki.DefaultLocale}', which is not a "
				+ "recognised BCP-47 locale. Use a tag such as 'en', 'fr' or 'pt-BR'.");
		}

		return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
	}

	/// <summary>
	/// The same rule as <c>WikiHelpers.NormalizeLocale</c>, restated here because
	/// <c>SharpMUSH.Contracts</c> (where that helper lives) references <em>this</em> project, so the
	/// dependency cannot run the other way. A test asserts the two agree.
	/// </summary>
	private static bool IsRealCulture(string? locale)
	{
		if (string.IsNullOrWhiteSpace(locale)) return false;

		try
		{
			return CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true).Name.Length > 0;
		}
		catch (CultureNotFoundException)
		{
			return false;
		}
	}
}
