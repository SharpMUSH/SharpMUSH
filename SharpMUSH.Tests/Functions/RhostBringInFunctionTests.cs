using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The RhostMUSH functions and commands SharpMUSH takes in for games that use them: keeptype() and remtype(),
/// keepflags() and remflags(), strfunc(), escapex(), globalroom(), the first-character tests, the hyperbolic
/// functions, fbetween(), nameqm(), move(), @eval and @pipe.
/// </summary>
public class RhostBringInFunctionTests : ServerTestBase
{
	[Test]
	[Arguments("keeptype(#0 #1 #-1,player)", "#1")]
	[Arguments("keeptype(#0 #1 #-1,p r)", "#0 #1")]
	[Arguments("remtype(#0 #1 #-1,player room)", "#-1")]
	[Arguments("keeptype(#0|#1|#-1,room other,|,-)", "#0-#-1")]
	[Arguments("keeptype(#0 #1,widget)", "#0 #1")]
	[Arguments("keeptype(me,player,,,1)", "#1")]
	public async Task KeepAndRemoveByType(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task KeepTypeGivesObjids()
	{
		await Assert.That(await Eval("keeptype(#1,player,,,2)")).IsEqualTo(await Eval("objid(#1)"));
	}

	[Test]
	[Arguments("keepflags(#1 #0,&W)", "#1")]
	[Arguments("keepflags(#1 #0,|WR)", "#1 #0")]
	[Arguments("keepflags(#1 #0,&WR)", "")]
	[Arguments("remflags(#1 #0,&W)", "#0")]
	[Arguments("keepflags(#1|#0,|R,|,-)", "#0")]
	[Arguments("keepflags(#1 #0,W)", "#-1 FUNCTION REQUIRES '&' OR '|' FOR FLAG MATCHES")]
	public async Task KeepAndRemoveByFlags(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task KeepFlagsGivesObjids()
	{
		await Assert.That(await Eval("keepflags(#1,&W,,,1)")).IsEqualTo(await Eval("objid(#1)"));
	}

	[Test]
	[Arguments("strfunc(add,1 2 3 4)", "10")]
	[Arguments("strfunc(add,1@2@3@4,@)", "10")]
	[Arguments("strfunc(cat,a\\[b\\]|c,|)", "a[b] c")]
	[Arguments("strfunc(cat,a\\,b c)", "a,b c")]
	[Arguments("strfunc(strlen,%bfoo,|)", "4")]
	[Arguments("strfunc(strlen,foo%b%b,|)", "5")]
	[Arguments("strfunc(no such,a)", "#-1 FUNCTION (NO SUCH) NOT FOUND")]
	public async Task StrFunc(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("escapex(\\[add\\(1\\,1\\)\\])", "\\\\[add\\(1\\,1\\)\\]")]
	[Arguments("escapex(\\[add\\(1\\,1\\)\\],f)", "\\[add\\(1\\,1\\)\\]")]
	[Arguments("escapex(\\[add\\(1\\,1\\)\\],f\\[\\])", "[add\\(1\\,1\\)]")]
	[Arguments("escapex(\\%b\\[x\\],fs)", "%b\\[x\\]")]
	[Arguments("escapex(\\%b\\[x\\],fsr)", "\\%b[x]")]
	[Arguments("escapex(\\%q0\\%b,fx,q)", "%q0\\%b")]
	public async Task EscapeX(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task GlobalRoomIsForWizardsAndRoyalty()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "GlobalRoom");
		try
		{
			await Assert.That(await Eval("globalroom()")).Matches("^#[0-9]+$");
			await Assert.That(await EvalAs(mortal.DbRef, "globalroom()")).IsEqualTo("#-1 PERMISSION DENIED: NEEDS GAME.WIZARD OR GAME.ROYALTY");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	[Arguments("isalpha(a123)", "1")]
	[Arguments("isalpha(1abc)", "0")]
	[Arguments("isalpha()", "0")]
	[Arguments("isalnum(9)", "1")]
	[Arguments("isdigit(7up)", "1")]
	[Arguments("islower(aB)", "1")]
	[Arguments("isupper(aB)", "0")]
	[Arguments("ispunct(!x)", "1")]
	[Arguments("ispunct(x!)", "0")]
	[Arguments("isspace(%bx)", "1")]
	[Arguments("isxdigit(f00)", "1")]
	[Arguments("isxdigit(g00)", "0")]
	[Arguments("isalpha(é)", "0")]
	public async Task FirstCharacterTests(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cosh(0)", "1")]
	[Arguments("sinh(0)", "0")]
	[Arguments("tanh(0)", "0")]
	[Arguments("round(cosh(1),6)", "1.543081")]
	[Arguments("round(sinh(180,d),6)", "11.548739")]
	[Arguments("cosh(x)", "#-1 ARGUMENT MUST BE NUMBER")]
	public async Task Hyperbolic(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fbetween(10.01,15.01,14.5)", "1")]
	[Arguments("fbetween(10.5,15.20,15.20)", "0")]
	[Arguments("fbetween(10.5,15.20,15.20,1)", "1")]
	[Arguments("fbetween(1,2,1.0000001)", "0")]
	[Arguments("fbetween(1,2,x)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task FBetween(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("[setqm(0,red,1,blue)][nameqm(0,nqfirst,1,nqsecond)]%q<nqfirst> %q<nqsecond>", "red blue")]
	[Arguments("[setq(0,red)][nameqm(0,)]", "#-1 ARGUMENTS EXPECTED")]
	[Arguments("nameqm(0,a,1)", "#-1 FUNCTION (NAMEQM) EXPECTS AN EVEN NUMBER OF ARGUMENTS")]
	public async Task NameQM(string code, string expected)
	{
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task MoveSendsAnObjectHome()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("MoveThing")}"));
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("MoveRoom")}"));
		await Cmd($"@tel {thing}={room}");
		await Assert.That(await Eval($"num(loc({thing}))")).IsEqualTo($"#{room.Number}");

		await Eval($"move({thing},home)");

		await Assert.That(await Eval($"num(loc({thing}))")).IsEqualTo(await Eval($"num(home({thing}))"));
	}

	[Test]
	public async Task MoveNeedsControl()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MoveMortal");
		try
		{
			await Assert.That(await EvalAs(mortal.DbRef, "move(#1,home)")).IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED");
		}
		finally
		{
			await ConnectionService.Disconnect(mortal.Handle);
		}
	}

	[Test]
	public async Task EvalRunsSideEffectsAndShowsNothing()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "EvalCmd");
		var token = TestIsolationHelpers.GenerateUniqueName("evaltoken");
		var value = TestIsolationHelpers.GenerateUniqueName("evalvalue");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@eval [pemit(me,{token})][cat({value})]");
			await WebAppFactoryArg.QueueBarrierAsync();

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.Contains(token, StringComparison.Ordinal)))
				.IsEquivalentTo([token]);
			await Assert.That(Notifications.For(actor.DbRef).Any(message => message.Contains(value, StringComparison.Ordinal)))
				.IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task PipeTakesOutputIntoAnAttribute()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PipeOn");
		await AloneAsync(actor);
		var first = TestIsolationHelpers.GenerateUniqueName("pipefirst");
		var second = TestIsolationHelpers.GenerateUniqueName("pipesecond");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/on LOG");
			await CmdAs(actor.DbRef, actor.Handle, $"think {first}");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/status");
			await CmdAs(actor.DbRef, actor.Handle, $"think {second}");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/off");
			await WebAppFactoryArg.QueueBarrierAsync();

			var heard = Notifications.For(actor.DbRef);
			await Assert.That(heard.Any(message => message.Contains(first, StringComparison.Ordinal)
				|| message.Contains(second, StringComparison.Ordinal))).IsFalse();
			await Assert.That(heard).Contains("@pipe: what you are told now goes to LOG.");
			await Assert.That(heard).Contains("@pipe: piping to LOG.");
			await Assert.That(heard).Contains("@pipe: written to LOG.");
			await Assert.That(await Eval($"get({actor.DbRef}/LOG)")).IsEqualTo($"{first}\n{second}");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task PipeTeeShowsOutputAndAddsToWhatTheAttributeHeld()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PipeTee");
		await AloneAsync(actor);
		var token = TestIsolationHelpers.GenerateUniqueName("pipetee");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, "&LOG me=before");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/tee/quiet LOG");
			await CmdAs(actor.DbRef, actor.Handle, $"think {token}");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/off/quiet");
			await WebAppFactoryArg.QueueBarrierAsync();

			var heard = Notifications.For(actor.DbRef);
			await Assert.That(heard.Where(message => message.Contains(token, StringComparison.Ordinal))).IsEquivalentTo([token]);
			await Assert.That(heard.Any(message => message.StartsWith("@pipe:", StringComparison.Ordinal))).IsFalse();
			await Assert.That(await Eval($"get({actor.DbRef}/LOG)")).IsEqualTo($"before\n{token}");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task PipeRefusesASecondPipeAndAStopWithNone()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PipeTwice");
		await AloneAsync(actor);
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/off");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/on/quiet LOG");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/on OTHER");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe/off/quiet");
			await CmdAs(actor.DbRef, actor.Handle, "@pipe");
			await WebAppFactoryArg.QueueBarrierAsync();

			var heard = Notifications.For(actor.DbRef);
			await Assert.That(heard).Contains("@pipe: you are not piping to an attribute.");
			await Assert.That(heard).Contains("@pipe: you are already piping to LOG.");
			await Assert.That(heard).Contains("@pipe: use /on, /tee, /off or /status.");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>Moves <paramref name="actor"/> to a room of its own, so a pipe takes nothing other tests say.</summary>
	private async Task AloneAsync(TestIsolationHelpers.TestPlayer actor)
	{
		var room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("PipeRoom")}"));
		await Cmd($"@tel {actor.DbRef}={room}");
	}
}
