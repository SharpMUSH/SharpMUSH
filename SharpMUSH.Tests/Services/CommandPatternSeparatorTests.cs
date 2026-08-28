using System.Text.RegularExpressions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The <c>$</c>/<c>^</c> pattern separator, against PennMUSH's own semantics: <c>set_cmd_flags</c>
/// (<c>src/attrib.c:844-856</c>) decides where the pattern ends, and <c>atr_single_match_r</c>
/// (<c>src/attrib.c:1786-1798</c>) decides what the pattern then <em>is</em>. SharpMUSH had the first
/// half and not the second, which is invisible until a pattern contains a colon.
///
/// <para>Pure string work, no database: these are the two functions the command scanner, the listen
/// handler, the highlighter and the package planner all now share, so a divergence here is a
/// divergence in every one of them at once.</para>
/// </summary>
public class CommandPatternSeparatorTests
{
	private static string? Pattern(Regex regex, string value)
	{
		var match = regex.Match(value);
		return match.Success ? match.Groups["pattern"].Value : null;
	}

	/// <summary>
	/// The three cases PennMUSH pins for <c>strchr_unescaped</c> itself
	/// (<c>src/strutil.c:2049-2057</c>), read as "where does the pattern end".
	/// </summary>
	[Test]
	public async Task CommandPatternRegex_SplitsWherePennSplits()
	{
		await Assert.That(Pattern(CommandDiscoveryService.CommandPatternRegex(), @"$foo\:bar:there"))
			.IsEqualTo(@"foo\:bar")
			.Because(@"\: is an escaped colon, so the terminator is the one before ""there""");

		await Assert.That(Pattern(CommandDiscoveryService.CommandPatternRegex(), @"$foo\:noescape"))
			.IsNull()
			.Because("the only colon is escaped, so this value defines no command at all");

		await Assert.That(Pattern(CommandDiscoveryService.CommandPatternRegex(), @"$foo\\:noescape"))
			.IsEqualTo(@"foo\\")
			.Because(@"\\ escapes the backslash, leaving the colon a real terminator");
	}

	/// <summary>
	/// <c>set_cmd_flags</c> falls <c>^</c> through into the <c>$</c> case and runs one scan for both,
	/// so the listen dialect is not merely similar here - it is the same code. This was a naive
	/// <c>[^:]+</c>, which cut at the escaped colon.
	/// </summary>
	[Test]
	public async Task ListenPatternRegex_SplitsWhereTheCommandOneDoes()
	{
		await Assert.That(Pattern(CommandDiscoveryService.ListenPatternRegex(), @"^foo\:bar:there"))
			.IsEqualTo(@"foo\:bar");

		await Assert.That(Pattern(CommandDiscoveryService.ListenPatternRegex(), @"^foo\:noescape"))
			.IsNull();

		await Assert.That(Pattern(CommandDiscoveryService.ListenPatternRegex(), @"^foo\\:noescape"))
			.IsEqualTo(@"foo\\");
	}

	/// <summary>
	/// Penn's scan starts at the sigil, so a colon immediately after it terminates an empty pattern.
	/// Useless in practice - it matches only empty input - but it is a command, and Penn compiles it.
	/// </summary>
	[Test]
	public async Task PatternRegexes_AcceptTheEmptyPattern()
	{
		await Assert.That(Pattern(CommandDiscoveryService.CommandPatternRegex(), "$:@pemit %#=ok")).IsEqualTo("");
		await Assert.That(Pattern(CommandDiscoveryService.ListenPatternRegex(), "^:@pemit %#=ok")).IsEqualTo("");
	}

	/// <summary>
	/// <c>\:</c> collapses; every other backslash is passed through untouched, because the wildcard and
	/// regex compilers downstream have their own uses for it and Penn hands them the escape intact.
	/// </summary>
	[Test]
	public async Task UnescapePatternSeparator_CollapsesOnlyTheSeparatorEscape()
	{
		await Assert.That(CommandDiscoveryService.UnescapePatternSeparator(@"foo\:bar")).IsEqualTo("foo:bar");
		await Assert.That(CommandDiscoveryService.UnescapePatternSeparator(@"foo\\")).IsEqualTo(@"foo\\")
			.Because(@"Penn emits both characters of \\ verbatim");
		await Assert.That(CommandDiscoveryService.UnescapePatternSeparator(@"say \*hi\*")).IsEqualTo(@"say \*hi\*")
			.Because(@"\* is the wildcard compiler's escape for a literal asterisk, not ours to consume");
		await Assert.That(CommandDiscoveryService.UnescapePatternSeparator(@"trailing\")).IsEqualTo(@"trailing\")
			.Because("a backslash with nothing after it escapes nothing");
		await Assert.That(CommandDiscoveryService.UnescapePatternSeparator("nothing to do")).IsEqualTo("nothing to do");
	}

	/// <summary>
	/// The scan must step over the character a backslash escapes, or the second backslash of a
	/// <c>\\</c> pair reads as the start of a new escape and eats the colon that follows it.
	/// </summary>
	[Test]
	public async Task UnescapePatternSeparator_DoesNotReadTheTailOfAPairAsAFreshEscape()
		=> await Assert.That(CommandDiscoveryService.UnescapePatternSeparator(@"a\\\:b")).IsEqualTo(@"a\\:b");

	/// <summary>
	/// The point of all of it: a regexp <c>$</c>-command's non-capturing group. Without the unescape
	/// .NET is handed <c>(?\:</c>, <see cref="Regex"/> throws, and <c>CommandAttributeScanner</c>'s
	/// catch drops the attribute - so the pattern does not error, it just stops existing.
	/// </summary>
	[Test]
	public async Task UnescapePatternSeparator_MakesANonCapturingGroupCompile()
	{
		const string stored = @"^look (?\:at|toward) (.+)$";

		await Assert.That(() => new Regex(stored)).Throws<ArgumentException>()
			.Because(@"(?\: is not a .NET regex construct");

		var compiled = new Regex(CommandDiscoveryService.UnescapePatternSeparator(stored));
		var match = compiled.Match("look toward the door");

		await Assert.That(match.Success).IsTrue();
		await Assert.That(match.Groups.Count).IsEqualTo(2)
			.Because("the group must be non-capturing, so %1 is the (.+) and not the alternation");
		await Assert.That(match.Groups[1].Value).IsEqualTo("the door");
	}
}
