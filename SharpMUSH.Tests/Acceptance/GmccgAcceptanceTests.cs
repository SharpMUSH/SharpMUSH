using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Tests.Acceptance;

/// <summary>GMCCG in a world of its own.</summary>
public class GmccgAcceptanceFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>
/// Acceptance gate for TinyMUX games: Thenomain's GMCCG stat system (checked out by CI at a pinned commit, or
/// <c>SHARPMUSH_GMCCG_PATH</c>) installs its data dictionary, support functions, stat setter, roller and sheet into a
/// world set up as a TinyMUX import sets one up, its roller rolls, and the post-import report names nothing beyond the
/// gaps listed here. A gap that closes comes off the list; a new one fails the test.
/// </summary>
public partial class GmccgAcceptanceTests
{
	[ClassDataSource<GmccgAcceptanceFactory>(Shared = SharedType.PerTestSession)]
	public required GmccgAcceptanceFactory Factory { get; init; }

	/// <summary>The parts installed, in the suite's own order.</summary>
	private static readonly string[] Parts = ["1 - Data Dictionary", "2 - Support Functions", "3 - Stat Setter", "6 - Roller", "7 - Sheet"];

	/// <summary>What the report says GMCCG asks for and SharpMUSH lacks today.</summary>
	private static readonly HashSet<string> KnownGaps =
	[
		// Global functions from Thenomain's other code, which a game running GMCCG installs first.
		"isstaff()", "titlestr()", "divider()", "vcolumns()", "footer()", "header()", "crumple()", "wfooter()",
		"wheader()", "msg()", "isapproved() with 2 arguments",
		// GMCCG's own @functions, registered at startup under computed names (2d - User-Defined Functions).
		"getstat()", "setstat()", "shiftstat()", "statvalidate()", "computed @function names",
		// Calls the scan reads with an argument count the function does not take; to be looked at one by one.
		"_()", "if() with 1 argument", "if() with 6 arguments", "grab() with 4 arguments",
		"localize() with 3 arguments", "not() with 2 arguments", "setdiff() with 1 argument",
		// Attributes of parts not installed here: chargen, health, spend and regain, and Thenomain's jobs.
		"the attribute D.CHARGEN", "the attribute DISPLAY.HEALTH-BAR", "the attribute HEALTH.TYPES",
		"the attribute F.POOL.CANCHANGE", "the attribute F.POOL.CHANGESTAT", "the attribute FN_FIND-JOB",
		"the attribute FN_GUEST", "the attribute FN_HASATTR", "the attribute FN_MYACCESSCHECK", "the attribute IS_PUBLIC",
		// Called on the character sheet or another object the code is handed.
		"the attribute SET", "the attribute VALUES"
	];

	[GeneratedRegex(@"^\d+( \d+)*:")]
	private static partial Regex Roll();

	[Test]
	public async Task InstallsRollsAndReportsOnlyKnownGaps()
	{
		var suite = SoftcodeSuiteInstaller.Locate("SHARPMUSH_GMCCG_PATH", "GMCCG");
		await SoftcodeSuiteInstaller.ConfigureAsync(Factory, TinyMuxDatabaseReader.CompatibilityOptions());
		await SoftcodeSuiteInstaller.InstallAsync(Factory, Parts.SelectMany(part =>
			Directory.GetFiles(Path.Join(suite, part), "*.txt").Order(StringComparer.Ordinal)), SoftcodeSuiteInstaller.MuxRewrite);
		await SoftcodeSuiteInstaller.HearCommandsAsync(Factory);

		var roll = await SoftcodeSuiteInstaller.RunAsync(Factory, "think [u(v(d.nr)/f.roller,3,10)]");
		await Assert.That(roll.Any(line => Roll().IsMatch(line))).IsTrue()
			.Because($"the roller answered {string.Join(" | ", roll)}");

		var report = await SoftcodeSuiteInstaller.ReportAsync(Factory, SoftcodeDialects.Mux.Key);
		await Assert.That(report.Where(key => !KnownGaps.Contains(key)).ToList()).IsEmpty()
			.Because("GMCCG needs something new; fix it, or list it with the reason in KnownGaps");
		await Assert.That(KnownGaps.Where(key => !report.Contains(key)).ToList()).IsEmpty()
			.Because("a known gap closed; take it off KnownGaps");
	}
}
