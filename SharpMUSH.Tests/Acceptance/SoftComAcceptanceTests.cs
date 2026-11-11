using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Tests.Acceptance;

/// <summary>SoftCom, RhostMUSH's channel system, in a world of its own.</summary>
public class SoftComAcceptanceFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>
/// Acceptance gate for RhostMUSH games: SoftCom (<c>Mushcode/comsys.notag</c> in RhostMUSH's repository, checked out
/// by CI at a pinned commit, or <c>SHARPMUSH_SOFTCOM_PATH</c>) installs into a world set up as a RhostMUSH import sets
/// one up, its staff commands run, and the post-import report names nothing beyond the gaps listed here. A gap that
/// closes comes off the list; a new one fails the test.
/// </summary>
public class SoftComAcceptanceTests
{
	[ClassDataSource<SoftComAcceptanceFactory>(Shared = SharedType.PerTestSession)]
	public required SoftComAcceptanceFactory Factory { get; init; }

	/// <summary>What the report says SoftCom asks for and SharpMUSH lacks today.</summary>
	private static readonly HashSet<string> KnownGaps =
	[
		// Not yet in SharpMUSH; listed for the compatibility work that brings each in.
		"parenmatch()",
		"chan()",
		"elist() with 6 arguments",
		"npemit() with 3 arguments",
		"visible() with 1 argument",
		// RhostMUSH takes a switch by any unique start of its name; SharpMUSH wants it whole.
		"@function/priv",
		"@function/pres",
		"@switch/f",
		"@include/command",
		// Left out on purpose (help compatibility left out).
		"execscript()",
		// SoftCom calls these and defines neither.
		"the attribute DO_ALIAS_NOTIFY",
		"the attribute DO_CHANDELETE"
	];

	[Test]
	public async Task InstallsRunsAndReportsOnlyKnownGaps()
	{
		var installer = SoftcodeSuiteInstaller.Locate("SHARPMUSH_SOFTCOM_PATH", Path.Join("RhostMUSH", "Mushcode", "comsys.notag"));
		await SoftcodeSuiteInstaller.ConfigureAsync(Factory, RhostDatabaseTranslator.CompatibilityOptions());
		await SoftcodeSuiteInstaller.InstallAsync(Factory, [installer], SoftcodeSuiteInstaller.RhostRewrite);
		await SoftcodeSuiteInstaller.HearCommandsAsync(Factory);

		var help = string.Join("\n", await SoftcodeSuiteInstaller.RunAsync(Factory, "comhelp"));
		await Assert.That(help).Contains("ComSys: Help for Version");
		var added = string.Join("\n", await SoftcodeSuiteInstaller.RunAsync(Factory, "comcfg/chanadd Gate=Acceptance gate"));
		await Assert.That(added).Contains("New channel 'Gate' created");

		var report = await SoftcodeSuiteInstaller.ReportAsync(Factory, SoftcodeDialects.Rhost.Key);
		await Assert.That(report.Where(key => !KnownGaps.Contains(key)).ToList()).IsEmpty()
			.Because("SoftCom needs something new; fix it, or list it with the reason in KnownGaps");
		await Assert.That(KnownGaps.Where(key => !report.Contains(key)).ToList()).IsEmpty()
			.Because("a known gap closed; take it off KnownGaps");
	}
}
