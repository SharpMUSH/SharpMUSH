using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

internal static class ImportWarningTexts
{
	/// <summary>The warnings' text, for a test that matches what a warning says.</summary>
	public static IEnumerable<string> Texts(this IEnumerable<ImportWarning> warnings) => warnings.Select(warning => warning.Text);
}
