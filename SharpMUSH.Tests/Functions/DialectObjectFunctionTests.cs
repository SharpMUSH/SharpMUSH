using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;

namespace SharpMUSH.Tests.Functions;

/// <summary>TinyMUX's and RhostMUSH's functions about objects, quota, speech, dice, debugging and locks.</summary>
public class DialectObjectFunctionTests : ServerTestBase
{
	private async Task<DBRef> CreateAsync(string prefix)
		=> DBRef.Parse(await Eval($"create({TestIsolationHelpers.GenerateUniqueName(prefix)})"));

	[Test]
	public async Task LastCreateRecordsWhatAnObjectMade()
	{
		var maker = await CreateAsync("LcMaker");
		await Cmd($"&MAKE {maker}=create({TestIsolationHelpers.GenerateUniqueName("LcThing")})");
		await Cmd($"&DIG {maker}=dig({TestIsolationHelpers.GenerateUniqueName("LcRoom")})");

		await Assert.That(await Eval($"lastcreate({maker})")).IsEqualTo("#-1");

		var thing = (await Eval($"u({maker}/MAKE)")).Split(':')[0];
		var room = (await Eval($"u({maker}/DIG)")).Split(':')[0];

		await Assert.That(await Eval($"lastcreate({maker},thing)")).IsEqualTo(thing);
		await Assert.That(await Eval($"lastcreate({maker},r)")).IsEqualTo(room);
		await Assert.That(await Eval($"lastcreate({maker})")).IsEqualTo(room);
		await Assert.That(await Eval($"lastcreate({maker},e)")).IsEqualTo("#-1");
		await Assert.That(await Eval($"lastcreate({TestIsolationHelpers.GenerateUniqueName("LcNobody")})")).IsEqualTo("#-1 NO MATCH");
	}

	[Test]
	public async Task HasQuotaAnswersForAPlayerNothingLimits()
	{
		await Assert.That(await Eval("hasquota(#1)")).IsEqualTo("1");
		await Assert.That(await Eval("hasquota(#1,500,r)")).IsEqualTo("1");
		await Assert.That(await Eval("hasquota(#1,1,x)")).IsEqualTo("#-1 BAD TYPE SPECIFIER");
		await Assert.That(await Eval("hasquota(#1,0)")).IsEqualTo("#-1 ARGUMENT MUST BE POSITIVE INTEGER");
	}

	[Test]
	public async Task PoseFormatsSpeechWithoutEmittingIt()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("PoseSpeaker");
		var speaker = DBRef.Parse(await Eval($"create({name})"));

		await Assert.That(await Eval($"pose({speaker},:waves.)")).IsEqualTo($"{name} waves.");
		await Assert.That(await Eval($"pose({speaker},;'s hat.)")).IsEqualTo($"{name}'s hat.");
		await Assert.That(await Eval($"pose({speaker},Hello,>)")).IsEqualTo($">{name} says, \"Hello\"");
	}

	[Test]
	public async Task SuccessesCountsDiceThatReachTheDifficulty()
	{
		int[] faces = [10, 6, 5, 1, 7];
		var next = 0;

		await Assert.That(SharpMUSH.Implementation.Functions.Functions.CountSuccesses(5, 6, () => faces[next++])).IsEqualTo(2);
		await Assert.That(await Eval("successes(0,6)")).IsEqualTo("0");
		await Assert.That(await Eval("successes(-1,6)")).IsEqualTo("#-1 NUMBER OF DICE SHOULD BE > 0");
		await Assert.That(await Eval("successes(101,6)")).IsEqualTo("#-1 TOO MANY DICE FOR ME TO ROLL");
		await Assert.That(await Eval("successes(a,6)")).IsEqualTo("#-1 ARGUMENTS MUST BE INTEGERS");
		await Assert.That(await Eval("isint(successes(5,6,2))")).IsEqualTo("1");
		await Assert.That(await Eval("successes(5,0)")).IsEqualTo("5");
		await Assert.That(await Eval("successes(5,11)")).IsEqualTo("0");
		await Assert.That(SharpMUSH.Implementation.Functions.Functions.Successes(5, 3, 2, () => 1)).IsEqualTo(0);
		await Assert.That(SharpMUSH.Implementation.Functions.Functions.Successes(5, 3, 2, () => 2)).IsEqualTo(1);
		await Assert.That(SharpMUSH.Implementation.Functions.Functions.Successes(5, 3, 1, () => 1)).IsEqualTo(-5);
		await Assert.That(SharpMUSH.Implementation.Functions.Functions.Successes(3, 6, 2, () => 1)).IsEqualTo(-3);
	}

	[Test]
	public async Task TraceShowsDebugOutputAndGivesTheResult()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(), "TracePlayer");
		var number = Random.Shared.Next(100000, 900000);
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var result = (await parser.EvaluateAsync(MarkupText.Plain($"trace(add(1,{number}))"))).ToPlainText();

		await Assert.That(result).IsEqualTo((number + 1).ToString());
		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef))
			.Contains(line => line.EndsWith($"add(1,{number}) => {number + 1}"));
	}

	[Test]
	public async Task LookupSiteOnAPortWithNoConnectionIsNothing()
		=> await Assert.That(await Eval("lookup_site(#1,999999)")).IsEqualTo("#-1");

	[Test]
	public async Task EncodedLocksDecodeAndCheck()
	{
		var thing = await CreateAsync("LockOther");

		await Assert.That(await Eval("lockcheck(lockencode(=#1),#1)")).IsEqualTo("1");
		await Assert.That(await Eval($"lockcheck(lockencode(=#1),{thing})")).IsEqualTo("0");
		await Assert.That(await Eval("lockdecode(lockencode(=#1))")).Contains("#1");
		await Assert.That(await Eval("lockdecode(not base64!)")).IsEqualTo("#-1 UNDEFINED KEY");
		await Assert.That(await Eval("lockencode()")).IsEqualTo("#-1 UNDEFINED KEY");
	}

	[Test]
	public async Task RhostTraceSetsTheDebugFlag()
	{
		var result = RhostSoftcodeRewriter.Rewrite("[trace(1)][chktrace()]");

		await Assert.That(result.Text).IsEqualTo("[set(%!,if(trunc(1),,!)DEBUG)][hasflag(%!,DEBUG)]");
	}
}
