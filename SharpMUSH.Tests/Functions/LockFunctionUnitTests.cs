using SharpMUSH.Library.Definitions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class LockFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test, NotInParallel]
	[Arguments("@LOCK", "lock(me,#FALSE)")]
	[Arguments("@LSET", "lset(me,visual)")]
	[Arguments("@ATRLOCK", "atrlock(me/ATRLOCKGATE,on)")]
	public async Task LockFunctionsRespectTheirCommandLock(string command, string expression)
	{
		var attribute = Parser.CommandLibrary[command].LibraryInformation.Attribute;
		var previous = attribute.CommandLock;
		try
		{
			attribute.CommandLock = "#FALSE";
			var result = await Parser.FunctionParse(MarkupText.Plain(expression));
			await Assert.That(result?.Message?.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED");
		}
		finally
		{
			attribute.CommandLock = previous;
		}
	}

	[Test]
	[Arguments("lock(me,#FALSE)")]
	[Arguments("lset(me/Basic,visual)")]
	public async Task GaggedOwnerCannotMutateLocksThroughOwnedThing(string expression)
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var ownerRef = await mediator.Send(new CreatePlayerCommand($"GaggedLock{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var owner = (await mediator.Send(new GetObjectNodeQuery(ownerRef))).Expect<SharpPlayer>();
		var room = (await mediator.Send(new GetObjectNodeQuery(new DBRef(0)))).Expect<SharpRoom>();
		var thing = await mediator.Send(new CreateThingCommand("GaggedLockPuppet", room, owner, room));
		var puppet = Parser.Push(Parser.CurrentState with { Executor = thing, Caller = ownerRef, Enactor = ownerRef });
		await puppet.FunctionParse(MarkupText.Plain("lock(me,#TRUE)"));
		var connections = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		await WebAppFactoryArg.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@set {ownerRef}=GAGGED"));
		var result = await puppet.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That((await puppet.FunctionParse(MarkupText.Plain("lock(me)")))!.Message!.ToPlainText()).IsEqualTo("#TRUE");
		await Assert.That((await puppet.FunctionParse(MarkupText.Plain("lockflags(me)")))!.Message!.ToPlainText()).IsEqualTo("i");
	}

	[Test]
	public async Task MortalCannotReadAbsentOrPrivateLocksOnAnotherOwner()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"LockRead{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var mortal = Parser.Push(Parser.CurrentState with { Executor = player, Caller = player, Enactor = player });
		var created = await Parser.FunctionParse(MarkupText.Plain("create(PrivateLock_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		var absent = await mortal.FunctionParse(MarkupText.Plain($"lock({target})"));
		await Assert.That(absent?.Message?.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Parser.FunctionParse(MarkupText.Plain($"lock({target},#TRUE)"));
		var creator = await mortal.FunctionParse(MarkupText.Plain($"lockowner({target})"));
		var flags = await mortal.FunctionParse(MarkupText.Plain($"llockflags({target})"));
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo("#-1 NO SUCH LOCK");
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("#-1 NO SUCH LOCK");
	}

	/// <summary>
	/// A mortal locking an attribute of their own succeeds, and reads the lock back.
	/// </summary>
	/// <remarks>
	/// Driven as a MORTAL on purpose: <c>CanSet</c>
	/// (<c>SharpMUSH.Library/Services/PermissionService.cs:52-100</c>) answers true for God before
	/// any per-flag gate is consulted, so a God-driven <c>atrlock()</c> would pass whatever the gate
	/// did and says nothing about the path a player takes. This is the only test that reaches the
	/// gate the function and <c>@ATRLOCK</c> now share.
	/// </remarks>
	[Test]
	public async Task MortalCanLockAndUnlockTheirOwnAttribute()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"AtrLock{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var mortal = Parser.Push(Parser.CurrentState with { Executor = player, Caller = player, Enactor = player });

		await mortal.FunctionParse(MarkupText.Plain("attrib_set(me/MORTALATRLOCK,value)"));
		await Assert.That((await mortal.FunctionParse(MarkupText.Plain("atrlock(me/MORTALATRLOCK)")))!.Message!.ToPlainText())
			.IsEqualTo("0").Because("a freshly set attribute is not locked");

		var locked = await mortal.FunctionParse(MarkupText.Plain("atrlock(me/MORTALATRLOCK,on)"));
		await Assert.That(locked!.Message!.ToPlainText()).IsEqualTo("")
			.Because("the side-effect form answers nothing on success");
		await Assert.That((await mortal.FunctionParse(MarkupText.Plain("atrlock(me/MORTALATRLOCK)")))!.Message!.ToPlainText())
			.IsEqualTo("1").Because("a mortal may lock an attribute they can set");

		await mortal.FunctionParse(MarkupText.Plain("atrlock(me/MORTALATRLOCK,off)"));
		await Assert.That((await mortal.FunctionParse(MarkupText.Plain("atrlock(me/MORTALATRLOCK)")))!.Message!.ToPlainText())
			.IsEqualTo("0").Because("and may take the lock back off again");
	}

	[Test]
	public async Task MortalCanSetOwnVisualFlagButCannotSetWizardFlag()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var player = await mediator.Send(new CreatePlayerCommand($"LockFlag{Guid.NewGuid():N}"[..20], "password", new DBRef(0), new DBRef(0), 100));
		var mortal = Parser.Push(Parser.CurrentState with { Executor = player, Caller = player, Enactor = player });
		await mortal.FunctionParse(MarkupText.Plain("lock(me,#TRUE)"));
		var missingName = await mortal.FunctionParse(MarkupText.Plain("lset(me,visual)"));
		await Assert.That(missingName?.Message?.ToPlainText()).IsEqualTo("");
		var unchanged = await mortal.FunctionParse(MarkupText.Plain("lockflags(me)"));
		await Assert.That(unchanged?.Message?.ToPlainText()).IsEqualTo("i");
		await mortal.FunctionParse(MarkupText.Plain("lset(me/Basic,visual)"));
		await mortal.FunctionParse(MarkupText.Plain("lset(me/Basic,wizard)"));
		var flags = await mortal.FunctionParse(MarkupText.Plain("lockflags(me)"));
		var creator = await mortal.FunctionParse(MarkupText.Plain("lockowner(me)"));
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("vi");
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo($"#{player.Number}");
	}

	[Test]
	[Arguments("lockflags()", "vicw+")]
	[Arguments("lockflags(null())", "vicw+")]
	[Arguments("llockflags(null())", "visual no_inherit no_clone wizard locked")]
	[Arguments("llockflags()", "visual no_inherit no_clone wizard locked")]
	[Arguments("testlock(=me,#1)", "1")]
	[Arguments("testlock(=me,#0)", "0")]
	[Arguments("lockfilter(=#1,#0 #1)", "#1")]
	[Arguments("lockfilter(#TRUE,#0|#1,|)", "#0|#1")]
	[Arguments("listset(a b c,2,x)", "a x c")]
	public async Task PennLockFunctionReadbacks(string expression, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task LockSetterAndLsetShareStoredMetadata()
	{
		var created = await Parser.FunctionParse(MarkupText.Plain("create(LockMetadata_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		var set = await Parser.FunctionParse(MarkupText.Plain($"lock({target},=me)"));
		await Assert.That(set?.Message?.ToPlainText()).IsEqualTo("=#1");
		await Parser.FunctionParse(MarkupText.Plain($"lset({target}/Basic,visual)"));
		await Parser.FunctionParse(MarkupText.Plain($"lset({target}/Basic,!no_inherit)"));
		var flags = await Parser.FunctionParse(MarkupText.Plain($"lockflags({target}/Basic)"));
		var names = await Parser.FunctionParse(MarkupText.Plain($"llockflags({target}/Basic)"));
		var creator = await Parser.FunctionParse(MarkupText.Plain($"lockowner({target}/Basic)"));
		await Assert.That(flags?.Message?.ToPlainText()).IsEqualTo("v");
		await Assert.That(names?.Message?.ToPlainText()).IsEqualTo("visual");
		await Assert.That(creator?.Message?.ToPlainText()).IsEqualTo("#1");
	}

	[Test]
	public async Task ListsBuiltinAndCustomLocks()
	{
		var builtins = await Parser.FunctionParse(MarkupText.Plain("locks()"));
		await Assert.That(builtins?.Message?.ToPlainText().Split(' ')).Contains("Basic");
		var created = await Parser.FunctionParse(MarkupText.Plain("create(CustomLock_" + Guid.NewGuid().ToString("N") + ")"));
		var target = created!.Message!.ToPlainText();
		await Parser.FunctionParse(MarkupText.Plain($"lock({target}/user:example,#TRUE)"));
		var listed = await Parser.FunctionParse(MarkupText.Plain($"locks({target})"));
		await Assert.That(listed?.Message?.ToPlainText()).IsEqualTo("USER:EXAMPLE");
	}

	[Test]
	public async Task LsetRejectsListReplacementArguments()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("lset(a b c,2,x)"));
		await Assert.That(result?.Message?.ToPlainText()).StartsWith("#-1");
	}

	[Test]
	[Arguments("testlock(#1,%#)", "1")]
	[Arguments("testlock(#FALSE,%#)", "0")]
	[Arguments("testlock(#TRUE,%#)", "1")]
	public async Task Testlock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lockowner(%#)", "")]
	public async Task Lockowner(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lockfilter(#0,basic)", "")]
	public async Task Lockfilter(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llocks(%#)", "")]
	public async Task Llocks(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("llockflags(%#/basic)", "")]
	public async Task Llockflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	/// <summary>
	/// <c>atrlock()</c> is an attribute-flag function, not a boolexp-lock one despite its name:
	/// <c>fun_atrlock</c> (<c>src/fundb.c:2409</c>) takes <c>&lt;object&gt;/&lt;attribute&gt;</c> in
	/// argument 0 and answers <c>AF_Locked</c>. SharpMUSH read argument 0 as an attribute name and
	/// argument 1 as an object, then evaluated a synthetic <c>&lt;attr&gt;`LOCK</c> attribute as a
	/// boolexp — a lock that nothing in the engine ever writes, so it answered 0 always, and the
	/// two-argument side-effect form did not exist. Its only test asserted IsNotNull.
	/// </summary>
	[Test, NotInParallel]
	public async Task AtrlockReadsAndWritesTheAttributeLockFlag()
	{
		var thing = (await Parser.FunctionParse(MarkupText.Plain(
			$"[setq(t,create(AtrlockProbe{Guid.NewGuid():N}))][attrib_set(%q<t>/PROBE,value)]%q<t>")))!
			.Message!.ToPlainText();

		await Assert.That(await Eval($"atrlock({thing}/PROBE)")).IsEqualTo("0");
		await Assert.That(await Eval($"atrlock({thing}/PROBE,on)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"atrlock({thing}/PROBE)")).IsEqualTo("1");
		await Assert.That(await Eval($"atrlock({thing}/PROBE,off)")).IsEqualTo(string.Empty);
		await Assert.That(await Eval($"atrlock({thing}/PROBE)")).IsEqualTo("0");
	}

	/// <summary>
	/// No slash is <c>#-1 ARGUMENT MUST BE OBJ/ATTR</c> (<c>src/fundb.c:2437,2441</c>), and an
	/// attribute that is not there is a bare <c>#-1</c> (<c>:2456</c>).
	/// </summary>
	[Test]
	[Arguments("atrlock(me)", "#-1 ARGUMENT MUST BE OBJ/ATTR")]
	[Arguments("atrlock()", "#-1 ARGUMENT MUST BE OBJ/ATTR")]
	[Arguments("atrlock(me/ATRLOCKNOSUCHATTRIBUTE)", "#-1")]
	public async Task AtrlockArgumentFormat(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	/// <summary>
	/// <c>do_atrlock</c> refuses anyone who does not control the object
	/// (<c>src/attrib.c:2506-2510</c>). The fixture's handle 1 is God, so this has to be driven by a
	/// mortal or it proves nothing.
	/// </summary>
	[Test]
	public async Task AtrlockRefusesAMortalWhoDoesNotControlTheObject()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var connections = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, mediator, connections, "AtrlockMortal");

		var thing = (await Parser.FunctionParse(MarkupText.Plain(
			$"[setq(t,create(AtrlockOther{Guid.NewGuid():N}))][attrib_set(%q<t>/PROBE,value)]%q<t>")))!
			.Message!.ToPlainText();

		var refused = await WebAppFactoryArg.FunctionParserFor(mortal.DbRef)
			.FunctionParse(MarkupText.Plain($"atrlock({thing}/PROBE,on)"));

		await Assert.That(refused!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await Eval($"atrlock({thing}/PROBE)")).IsEqualTo("0")
			.Because("the refusal has to have left the flag alone");
	}

	private async Task<string> Eval(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))?.Message!.ToPlainText() ?? "<null>";

	[Test]
	public async Task LockReturnsUnlocked()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MarkupText.Plain("create(LockFunc_UnlockedTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MarkupText.Plain($"lock({dbref})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("*UNLOCKED*");
	}

	[Test]
	public async Task ElockNoLockPasses()
	{
		// Create a dedicated object to avoid parallel test interference
		var createResult = (await Parser.FunctionParse(MarkupText.Plain("create(LockFunc_ElockTest)")))?.Message!;
		var dbref = createResult.ToPlainText();

		var result = (await Parser.FunctionParse(MarkupText.Plain($"elock({dbref}/Basic,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}
}
