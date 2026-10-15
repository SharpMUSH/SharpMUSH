using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The RhostMUSH import's softcode rewrites besides colour: <c>%&lt;nnn&gt;</c> character codes and
/// <c>timefmt()</c> formats written as literals.
/// </summary>
public class RhostSoftcodeRewriterTests
{
	[Test]
	[Arguments("think %<065>BC", "think ABC")]
	[Arguments("think A%<032>B", "think A%bB")]
	[Arguments("think A%<010>B", "think A%rB")]
	[Arguments("think A%<009>B", "think A%tB")]
	[Arguments("think %<091>x%<093>", "think \\[x\\]")]
	[Arguments("think 100%<037>", "think 100\\%")]
	[Arguments("think %<u00e9>t%<U00E9>", "think été")]
	[Arguments("think %<007>", "think [chr(7)]")]
	public async Task WritesTheCharacter(string body, string expected)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(result.Changed).IsTrue();
		await Assert.That(result.Notes.Count).IsGreaterThanOrEqualTo(1);
	}

	/// <summary>What RhostMUSH does not read as a character is left, and said.</summary>
	[Test]
	[Arguments("think %<300>")]
	[Arguments("think %<65>")]
	[Arguments("think %<abc>")]
	public async Task LeavesWhatIsNotACharacterCode(string body)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(body);
		await Assert.That(result.Changed).IsFalse();
		await Assert.That(result.Notes.Count).IsEqualTo(1);
	}

	[Test]
	public async Task LeavesAnEscapedCodeAndPlainTextAlone()
	{
		var escaped = RhostSoftcodeRewriter.Rewrite("think \\%<065>");
		var plain = RhostSoftcodeRewriter.Rewrite("think [name(me)] %0");

		await Assert.That(escaped.Text).IsEqualTo("think \\%<065>");
		await Assert.That(escaped.Notes).IsEmpty();
		await Assert.That(plain.Text).IsEqualTo("think [name(me)] %0");
		await Assert.That(plain.Notes).IsEmpty();
	}

	[Test]
	[Arguments("think timefmt($02h:$02T:$02S,secs())", "think timefmt($H:$M:$S,secs())", false)]
	[Arguments("think timefmt($A $B $02D $Y,secs())", "think timefmt($A $B $d $Y,secs())", false)]
	[Arguments("think timefmt($02H $P,secs())", "think timefmt($I $p,secs())", false)]
	[Arguments("think timefmt($H:$T,secs())", "think timefmt($I:$M,secs())", true)]
	[Arguments("think [timefmt($M/$D/$y)]", "think [timefmt($m/$d/$y)]", true)]
	[Arguments("think timefmt($W $03J $$,secs())", "think timefmt($w $j $$,secs())", false)]
	public async Task RewritesALiteralTimeFormat(string body, string expected, bool padded)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(expected);
		await Assert.That(result.Notes.Count).IsEqualTo(1);
		await Assert.That(result.Notes[0].Contains("leading zero")).IsEqualTo(padded);
	}

	/// <summary>A computed format, or a code SharpMUSH has no counterpart for, is left as written and reported.</summary>
	[Test]
	[Arguments("think timefmt(%0,secs())")]
	[Arguments("think timefmt([v(fmt)],secs())")]
	[Arguments("think timefmt($Z,secs())")]
	[Arguments("think timefmt($p,secs())")]
	[Arguments("think timefmt($-H,secs())")]
	[Arguments("think timefmt($3H,secs())")]
	public async Task LeavesWhatItCannotTranslate(string body)
	{
		var result = RhostSoftcodeRewriter.Rewrite(body);

		await Assert.That(result.Text).IsEqualTo(body);
		await Assert.That(result.Notes.Count).IsEqualTo(1);
		await Assert.That(result.Notes[0]).Contains("left as written");
	}

	[Test]
	public async Task OnlyTouchesTimefmtItself()
	{
		var result = RhostSoftcodeRewriter.Rewrite("think mytimefmt($H) [u(timefmt_attr)]");

		await Assert.That(result.Text).IsEqualTo("think mytimefmt($H) [u(timefmt_attr)]");
		await Assert.That(result.Notes).IsEmpty();
	}
}
