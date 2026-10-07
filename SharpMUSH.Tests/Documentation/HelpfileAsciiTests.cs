using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Shipped help is ASCII throughout, so it reads the same on a client without UTF-8. Where output
/// would hold a character outside ASCII, the help describes it in words, and an example that needs
/// one as input builds it with <c>chr()</c> or <c>accent()</c>. The article metadata counts too: a
/// JSON escape such as <c>\u2014</c> in a section heading is the same character spelled in ASCII.
/// </summary>
public partial class HelpfileAsciiTests
{
	[GeneratedRegex(@"\\u(?!00[0-7][0-9a-fA-F])[0-9a-fA-F]{4}")]
	private static partial Regex NonAsciiEscape();

	[Test]
	public async Task EveryHelpfileIsAscii()
	{
		var offending = TestPaths.Helpfiles
			.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.SelectMany(file => File.ReadLines(file.FullName)
				.Select((line, index) => (line, number: index + 1))
				.Where(entry => entry.line.Any(c => c > '\u007f') || NonAsciiEscape().IsMatch(entry.line))
				.Select(entry => $"{file.Name}:{entry.number}: {entry.line}"))
			.ToList();

		await Assert.That(offending).IsEmpty();
	}
}
