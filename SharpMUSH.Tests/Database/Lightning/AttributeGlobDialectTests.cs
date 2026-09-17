using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Pins the attribute-name glob dialect against the general MUSH wildcard. Attribute names are
/// backtick-separated trees, and the two dialects disagree on the one character that matters most:
/// a single <c>*</c> stays inside one level here and crosses levels there. A future "unify the glob
/// implementations" pass that points attribute matching at <c>MushText.Glob.ToRegex</c> would make
/// <c>obj/*</c> start returning grandchildren, which is why these cases exist.
/// </summary>
public class AttributeGlobDialectTests
{
	private static bool Attr(string pattern, string name) =>
		LightningDatabase.GlobToRegex(pattern).IsMatch(name);

	private static bool General(string pattern, string name) =>
		System.Text.RegularExpressions.Regex.IsMatch(
			name, MushText.Glob.ToRegex(pattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

	[Test]
	[Arguments("DESC", "DESC")]
	[Arguments("*", "DESC")]
	[Arguments("DESC*", "DESCRIBE")]
	[Arguments("D?SC", "DESC")]
	[Arguments("DESC`*", "DESC`SHORT")]
	[Arguments("**", "DESC`SHORT`INNER")]
	[Arguments("DESC`**", "DESC`SHORT`INNER")]
	public async Task DialectMatches(string pattern, string name)
		=> await Assert.That(Attr(pattern, name)).IsTrue();

	[Test]
	[Arguments("*", "DESC`SHORT")]
	[Arguments("DESC`*", "DESC`SHORT`INNER")]
	[Arguments("D?SC", "DESC`SHORT")]
	[Arguments("DESC", "DESCRIBE")]
	public async Task DialectDoesNotMatch(string pattern, string name)
		=> await Assert.That(Attr(pattern, name)).IsFalse();

	/// <summary>
	/// The whole point of keeping two dialects: a single <c>*</c> is level-local for attributes and
	/// level-crossing for the general wildcard. If this ever agrees, the dialects have been collapsed.
	/// </summary>
	[Test]
	[Arguments("*", "DESC`SHORT")]
	[Arguments("DESC`*", "DESC`SHORT`INNER")]
	public async Task SingleStarIsLevelLocalUnlikeTheGeneralWildcard(string pattern, string name)
	{
		await Assert.That(Attr(pattern, name)).IsFalse();
		await Assert.That(General(pattern, name)).IsTrue();
	}

	/// <summary><c>**</c> is the dialect's own escape hatch for crossing levels; the general wildcard has no such token.</summary>
	[Test]
	public async Task DoubleStarCrossesLevels()
	{
		await Assert.That(Attr("**", "DESC`SHORT`INNER")).IsTrue();
		await Assert.That(Attr("DESC`**", "DESC`SHORT`INNER")).IsTrue();
	}

	/// <summary>A trailing backtick means "direct children only" — it must match a child and reject the node itself.</summary>
	[Test]
	public async Task TrailingBacktickIsDirectChildrenOnly()
	{
		await Assert.That(Attr("DESC`", "DESC`SHORT")).IsTrue();
		await Assert.That(Attr("DESC`", "DESC")).IsFalse();
		await Assert.That(Attr("DESC`", "DESC`SHORT`INNER")).IsFalse();
	}

	/// <summary>Everything that is a regex metacharacter but not a glob token is literal.</summary>
	[Test]
	[Arguments("A.B", "A.B")]
	[Arguments("A+B", "A+B")]
	[Arguments("A(B)", "A(B)")]
	[Arguments("A[B]", "A[B]")]
	[Arguments("A$B", "A$B")]
	public async Task MetacharactersAreLiteral(string pattern, string name)
		=> await Assert.That(Attr(pattern, name)).IsTrue();

	[Test]
	public async Task MetacharactersDoNotMatchAsRegex()
	{
		await Assert.That(Attr("A.B", "AXB")).IsFalse();
		await Assert.That(Attr("A+B", "AAB")).IsFalse();
	}

	[Test]
	public async Task MatchingIsCaseInsensitive()
		=> await Assert.That(Attr("desc`*", "DESC`SHORT")).IsTrue();
}
