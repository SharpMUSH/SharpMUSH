using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// An attribute in TinyMUX's dialect tests flags and powers by TinyMUX's names and letters: <c>HALTED</c>,
/// <c>COMMANDS</c> (not NO_COMMAND), <c>HAS_STARTUP</c>, <c>comm_all</c>, and <c>s</c> for SAFE.
/// </summary>
public class SoftcodeDialectFlagTests : ServerTestBase
{
	private async Task<DBRef> MuxThingAsync(string code)
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectFlag")}"));
		await Cmd($"&MUX {thing}={code}");
		await Cmd($"@set {thing}/MUX=mux");
		await Cmd($"&PLAIN {thing}={code}");
		return thing;
	}

	private async Task<DBRef> TargetAsync()
		=> DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("DialectTarget")}"));

	[Test]
	public async Task HasFlagReadsTinyMuxNames()
	{
		var target = await TargetAsync();
		var thing = await MuxThingAsync(
			$"[hasflag({target},HALTED)][hasflag({target},COMMANDS)][hasflag({target},SPOOF)][hasflag({target},HAS_STARTUP)]");
		await Cmd($"@set {target}=HALT");
		await Cmd($"@set {target}=!NO_COMMAND");

		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("1110");
		await Assert.That(await Eval($"u({thing}/PLAIN)")).IsEqualTo("0000");

		await Cmd($"@set {target}=NO_COMMAND");
		await Cmd($"@set {target}=NOSPOOF");
		await Cmd($"&STARTUP {target}=think hello");

		// HAS_STARTUP is God's alone, as in TinyMUX: the thing running the code is not told.
		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("1000");

		using var dialect = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { Dialect = "mux" }
		});
		await Assert.That(await Eval($"hasflag({target},HAS_STARTUP)")).IsEqualTo("1");
	}

	[Test]
	public async Task ListFlagFunctionsReadTinyMuxNames()
	{
		var target = await TargetAsync();
		var thing = await MuxThingAsync($"[andlflags({target},HALTED COMMANDS)] [orlflags({target},!SPOOF ASCII)]");
		await Cmd($"@set {target}=HALT");

		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("0 0");

		await Cmd($"@set {target}=!NO_COMMAND");
		await Cmd($"@set {target}=NOSPOOF");

		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("1 1");
	}

	[Test]
	public async Task FlagLettersAreTinyMuxLetters()
	{
		var thing = await MuxThingAsync("[flags(me)] [andflags(me,sT)] [andflags(me,X)] [orflags(me,!s)] [andflags(me,!P!R)] [orflags(me,s!)]");
		await Cmd($"@set {thing}=SAFE");
		await Cmd($"@set {thing}=DEBUG");

		// NO_COMMAND is n, SAFE s and TRACE (DEBUG) T in TinyMUX, X is ANSI; a thing has no type letter there. A
		// trailing ! is false, even in an OR that has already matched.
		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("nsT 1 0 0 1 0");
		// SharpMUSH's own letters still hold outside the dialect: X is SAFE.
		await Assert.That(await Eval($"andflags({thing},X)")).IsEqualTo("1");
	}

	/// <summary>The example in <c>help tinymux flags</c>.</summary>
	[Test]
	public async Task HelpExample()
	{
		var thing = await MuxThingAsync("[flags(me)] [hasflag(me,COMMANDS)]");
		await Cmd($"@set {thing}=SAFE");
		await Cmd($"@set {thing}=TERSE");

		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("nsq 0");
	}

	[Test]
	public async Task HasPowerReadsTinyMuxNames()
	{
		var thing = await MuxThingAsync("[haspower(me,comm_all)][haspower(me,siteadmin)][andlpowers(me,comm_all)]");
		await Cmd($"@power {thing}=Chat_Privs");

		await Assert.That(await Eval($"u({thing}/MUX)")).IsEqualTo("101");
		await Assert.That(await Eval($"u({thing}/PLAIN)")).IsEqualTo("000");
	}
}
