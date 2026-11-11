using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// RhostMUSH commands the import writes as the SharpMUSH command that takes their arguments
/// (<see cref="RhostCommandRewriter"/>).
/// </summary>
public class RhostCommandRewriterTests
{
	[Test]
	[Arguments("think Off;join Bob", "think Off;@teleport me=[loc(pmatch(Bob))]")]
	[Arguments("{grab %0}", "{@teleport [pmatch(%0)]=here}")]
	[Arguments("$+summon *:grab %0;@pemit %#=Done.", "$+summon *:@teleport [pmatch(%0)]=here;@pemit %#=Done.")]
	[Arguments("@switch %0=1,{join #12}", "@switch %0=1,{@teleport me=[loc(pmatch(#12))]}")]
	[Arguments("@freeze 123", "@queue/pause 123")]
	[Arguments("@thaw 456", "@queue/resume 456")]
	[Arguments("@thaw/del 456", "@halt/pid 456")]
	[Arguments("@dynhelp history/lesson=%0", "@pemit %0=[textfile(history,lesson)]")]
	[Arguments("@dynhelp history/lesson", "@pemit %#=[textfile(history,lesson)]")]
	[Arguments("@dynhelp history", "@pemit %#=[textfile(history,help)]")]
	[Arguments("@switch %0=1,@freeze %1,@thaw %1", "@switch %0=1,@queue/pause %1,@queue/resume %1")]
	[Arguments("@lfunction double=me/DOUBLE", "@function/local double=me,DOUBLE")]
	[Arguments("@lfunction/privilege double=#5/FN_DOUBLE", "@function/local double=#5,FN_DOUBLE")]
	[Arguments("@lfunction/delete double", "@function/local/delete double")]
	[Arguments("@lfunction/list", "@function/local")]
	[Arguments("wmail/wipe Jerk", "@mail/debug clear=Jerk")]
	[Arguments("@zone/add #3=#2", "@chzone/add #3=#2")]
	[Arguments("@zone/del #3=#2", "@chzone/remove #3=#2")]
	[Arguments("@zone/purge #3", "@chzone #3=")]
	[Arguments("wmail/fix", "@mail/debug fix")]
	[Arguments("wmail wipe Jerk", "@mail/debug clear=Jerk")]
	[Arguments("@dynhelp history/lesson=Bob", "@pemit *Bob=[textfile(history,lesson)]")]
	[Arguments("@dolist %0=grab ##", "@dolist %0=@teleport [pmatch(##)]=here")]
	[Arguments("$+tp *:@switch %0=1,join %1", "$+tp *:@switch %0=1,@teleport me=[loc(pmatch(%1))]")]
	[Arguments("@wait 5=@freeze [first(lpids(me))]", "@wait 5=@queue/pause [first(lpids(me))]")]
	public async Task WritesTheCommand(string body, string expected)
	{
		var notes = new List<string>();

		await Assert.That(RhostCommandRewriter.Rewrite(body, notes)).IsEqualTo(expected);
		await Assert.That(notes).IsNotEmpty();
	}

	/// <summary>A form with no SharpMUSH counterpart is left as written, and said.</summary>
	[Test]
	[Arguments("@dynhelp/search history/war*")]
	[Arguments("@dynhelp txt^help")]
	[Arguments("@lfunction/max double=2")]
	[Arguments("@lfunction _ladd=me/MY_ADD")]
	[Arguments("@zone/replace #3=#2/#4")]
	[Arguments("@zone #3")]
	[Arguments("wmail/size Bob=10")]
	[Arguments("@totemdef/set fire=wizard")]
	[Arguments("@thaw/bogus 1")]
	[Arguments("think a;join")]
	[Arguments("@thaw/delete")]
	[Arguments("@lfunction/delete #12_double")]
	[Arguments("wmail/lfix")]
	[Arguments("wmail/clean")]
	[Arguments("@dynhelp news/a,b")]
	public async Task LeavesWhatHasNoCounterpart(string body)
	{
		var notes = new List<string>();

		await Assert.That(RhostCommandRewriter.Rewrite(body, notes)).IsEqualTo(body);
		await Assert.That(notes).HasSingleItem();
		await Assert.That(notes[0]).StartsWith("check by hand:");
	}

	/// <summary>The names are commands only where a command starts; elsewhere they are text.</summary>
	[Test]
	[Arguments("@pemit %#=join the club")]
	[Arguments("say I will grab it")]
	[Arguments("think [join(a,b)]")]
	[Arguments("@emit You can join <player> here.")]
	[Arguments("Grab a chair and sit.")]
	[Arguments("Join us at the inn.")]
	[Arguments("@pemit %#=@freeze stops a job")]
	[Arguments("$+help freeze:@pemit %#=@freeze <pid> stops a job")]
	[Arguments("@switch %0=1,{@freeze <pid>}")]
	[Arguments("The bar is busy; grab a seat.")]
	[Arguments("Welcome!;join us at the inn.")]
	[Arguments("@switch %0=1,@pemit %#=@freeze is how")]
	public async Task LeavesTextAlone(string body)
	{
		var notes = new List<string>();

		await Assert.That(RhostCommandRewriter.Rewrite(body, notes)).IsEqualTo(body);
		await Assert.That(notes).IsEmpty();
	}

	[Test]
	public async Task FindsLocalFunctionDefinitions()
	{
		var names = RhostCommandRewriter.LocalFunctionNames("@lfunction double=me/DOUBLE;@lfunction/pre half=#4/HALF;@lfunction/delete gone");

		await Assert.That(names).IsEquivalentTo(["double", "half"]);
	}

	[Test]
	[Arguments("think [double(7)] [half()]", "think [localfun(double,7)] [localfun(half)]")]
	[Arguments("think [redouble(7)] [lit(double(7))] \\double(7)", "think [redouble(7)] [lit(double(7))] \\double(7)")]
	[Arguments("think double(7)", "think localfun(double,7)")]
	[Arguments("$double(*):think [double(%0)]", "$double(*):think [localfun(double,%0)]")]
	[Arguments("Type double(7) to see. x.double(1)", "Type double(7) to see. x.double(1)")]
	public async Task CallsLocalFunctionsThroughLocalfun(string body, string expected)
	{
		var notes = new List<string>();

		await Assert.That(RhostCommandRewriter.RewriteLocalFunctionCalls(body, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "double", "half" }, notes))
			.IsEqualTo(expected);
	}
}
