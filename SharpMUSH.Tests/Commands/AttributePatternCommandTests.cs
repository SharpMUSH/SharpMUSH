using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Attribute patterns on the left of <c>@set</c> and <c>set()</c> (PennMUSH's <c>do_attrib_flags</c>,
/// <c>src/set.c:594</c>, which walks <c>atr_iter_get</c>), the value of <c>@set obj=attr:value</c>
/// keeping its commas, and the wildcard source <c>@cpattr</c>/<c>@mvattr</c> accept beyond PennMUSH.
/// </summary>
public class AttributePatternCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private readonly List<TestIsolationHelpers.TestPlayer> _players = [];

	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var player in _players)
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Uid() => Guid.NewGuid().ToString("N")[..8].ToUpper();

	private Task God(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	/// <summary>A player in a room of its own, so what it hears is only what its own commands caused.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> CreateIsolatedPlayer(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_players.Add(player);
		var actor = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var origin = await actor.Location.WithCancellation(CancellationToken.None);
		var roomId = await Mediator.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("AttrPatternRoom"), actor));
		var room = (await Mediator.Send(new GetObjectNodeQuery(roomId))).Expect<SharpRoom>();
		await Mediator.Send(new MoveObjectCommand(actor, room, origin.Object().DBRef, IsSilent: true));
		return player;
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private async Task<SharpAttribute?> Read(DBRef who, string attribute)
	{
		var obj = (await Mediator.Send(new GetObjectNodeQuery(who))).Expect<AnySharpObject>();
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		return await AttributeService.GetAttributeAsync(god, obj, attribute, IAttributeService.AttributeMode.Read, false)
			is SharpAttribute[] chain
			? chain.Last()
			: null;
	}

	private async Task<bool> HasFlag(DBRef who, string attribute, string flag)
		=> await Read(who, attribute) is { } attr
			&& attr.Flags.Any(f => f.Name.Equals(flag, StringComparison.OrdinalIgnoreCase));

	[Test]
	public async ValueTask SetFlags_PatternChangesEveryMatchingAttribute()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternSet");
		await God($"&PA{uid}_1 {obj}=one");
		await God($"&PA{uid}_2 {obj}=two");
		await God($"&PB{uid} {obj}=other");

		await God($"@set {obj}/PA{uid}_*=case");

		await Assert.That(await HasFlag(obj, $"PA{uid}_1", "case")).IsTrue();
		await Assert.That(await HasFlag(obj, $"PA{uid}_2", "case")).IsTrue();
		await Assert.That(await HasFlag(obj, $"PB{uid}", "case")).IsFalse()
			.Because("an attribute the pattern does not match is left alone");
	}

	/// <summary>The help's example: a pattern under a branch changes the branch's children.</summary>
	[Test]
	public async ValueTask SetFlags_PatternMatchesUnderABranch()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternSetTree");
		await God($"&ST{uid} {obj}=root");
		await God($"&ST{uid}`A {obj}=a");
		await God($"&ST{uid}`B {obj}=b");

		await God($"@set {obj}/ST{uid}`*=no_inherit");

		await Assert.That(await HasFlag(obj, $"ST{uid}`A", "no_inherit")).IsTrue();
		await Assert.That(await HasFlag(obj, $"ST{uid}`B", "no_inherit")).IsTrue();
		await Assert.That(await HasFlag(obj, $"ST{uid}", "no_inherit")).IsFalse()
			.Because("the branch itself does not match ST`*");
	}

	/// <summary>
	/// <c>af_helper</c> refuses one attribute and goes on to the next (<c>src/set.c:509-514</c>).
	/// </summary>
	[Test]
	public async ValueTask SetFlags_PatternSkipsAnAttributeThePlayerMayNotChange()
	{
		var uid = Uid();
		var mortal = await CreateIsolatedPlayer("AttrPatternDeny");
		var name = (await Mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<AnySharpObject>().Object().Name;
		var parser = WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle);

		await parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&PD{uid}_1 me=one"));
		await parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&PD{uid}_2 me=two"));
		await God($"@set {mortal.DbRef}/PD{uid}_1=wizard");

		var messages = await MessagesWhile(mortal.DbRef, () =>
			parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@set me/PD{uid}_*=regexp")).AsTask());

		await Assert.That(messages).Contains($"You cannot change that flag on {name}/PD{uid}_1");
		await Assert.That(messages).Contains($"{name}/PD{uid}_2 - regexp set.");
		await Assert.That(await HasFlag(mortal.DbRef, $"PD{uid}_1", "regexp")).IsFalse();
		await Assert.That(await HasFlag(mortal.DbRef, $"PD{uid}_2", "regexp")).IsTrue();
	}

	[Test]
	public async ValueTask SetFlags_PatternMatchingNothingSaysSo()
	{
		var uid = Uid();
		var mortal = await CreateIsolatedPlayer("AttrPatternNone");
		var parser = WebAppFactoryArg.CommandParserFor(mortal.DbRef, mortal.Handle);

		var messages = await MessagesWhile(mortal.DbRef, () =>
			parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@set me/PN{uid}_*=regexp")).AsTask());

		await Assert.That(messages).Contains("No attribute found to change.");
	}

	/// <summary><c>fun_set</c> is <c>do_set(executor, args[0], args[1])</c> (<c>src/fundb.c:2251</c>).</summary>
	[Test]
	public async ValueTask SetFunction_TakesAnAttributePattern()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternFun");
		await God($"&PF{uid}_1 {obj}=one");
		await God($"&PF{uid}_2 {obj}=two");

		await FunctionParser.EvaluateAsync(MarkupText.Plain($"set({obj}/PF{uid}_*,case)"));

		await Assert.That(await HasFlag(obj, $"PF{uid}_1", "case")).IsTrue();
		await Assert.That(await HasFlag(obj, $"PF{uid}_2", "case")).IsTrue();
	}

	/// <summary>
	/// <c>cmd_set</c> hands <c>do_set</c> the whole right side (<c>src/cmds.c:1410</c>), so the value
	/// after the colon keeps its commas.
	/// </summary>
	[Test]
	public async ValueTask SetAttributeValue_KeepsItsCommas()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternComma");

		await God($"@set {obj}=CM{uid}:a,b,c");

		await Assert.That((await Read(obj, $"CM{uid}"))?.Value.ToPlainText()).IsEqualTo("a,b,c");
	}

	[Test]
	public async ValueTask CopyAttribute_PatternCopiesEachMatchUnderItsOwnName()
	{
		var uid = Uid();
		var source = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpSrc");
		var first = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpDst1");
		var second = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpDst2");
		await God($"&CP{uid}_1 {source}=one");
		await God($"&CP{uid}_2 {source}=two");
		await God($"&CQ{uid} {source}=other");
		await God($"@set {source}/CP{uid}_2=case");

		await God($"@cpattr {source}/CP{uid}_*={first},{second}");

		foreach (var destination in (DBRef[])[first, second])
		{
			await Assert.That((await Read(destination, $"CP{uid}_1"))?.Value.ToPlainText()).IsEqualTo("one");
			await Assert.That((await Read(destination, $"CP{uid}_2"))?.Value.ToPlainText()).IsEqualTo("two");
			await Assert.That(await HasFlag(destination, $"CP{uid}_2", "case")).IsTrue()
				.Because("flags are copied with each attribute unless /noflagcopy");
			await Assert.That(await Read(destination, $"CQ{uid}")).IsNull();
		}

		await Assert.That((await Read(source, $"CP{uid}_1"))?.Value.ToPlainText()).IsEqualTo("one")
			.Because("@cpattr leaves the source in place");
	}

	/// <summary>The help's example: <c>CMD`**</c> copies the branch's descendants to an object without the branch.</summary>
	[Test]
	public async ValueTask CopyAttribute_PatternCopiesATreeBranch()
	{
		var uid = Uid();
		var source = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpTreeSrc");
		var destination = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpTreeDst");
		await God($"&CT{uid} {source}=root");
		await God($"&CT{uid}`A {source}=a");
		await God($"&CT{uid}`B`C {source}=c");

		await God($"@cpattr {source}/CT{uid}`**={destination}");

		await Assert.That((await Read(destination, $"CT{uid}`A"))?.Value.ToPlainText()).IsEqualTo("a");
		await Assert.That((await Read(destination, $"CT{uid}`B`C"))?.Value.ToPlainText()).IsEqualTo("c");
	}

	[Test]
	public async ValueTask MoveAttribute_PatternMovesEachMatch()
	{
		var uid = Uid();
		var source = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternMvSrc");
		var destination = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternMvDst");
		await God($"&MV{uid}_1 {source}=one");
		await God($"&MV{uid}_2 {source}=two");

		var messages = await MessagesWhile(WebAppFactoryArg.ExecutorDBRef, () => God($"@mvattr {source}/MV{uid}_*={destination}"));

		await Assert.That((await Read(destination, $"MV{uid}_1"))?.Value.ToPlainText()).IsEqualTo("one");
		await Assert.That((await Read(destination, $"MV{uid}_2"))?.Value.ToPlainText()).IsEqualTo("two");
		await Assert.That(await Read(source, $"MV{uid}_1")).IsNull();
		await Assert.That(await Read(source, $"MV{uid}_2")).IsNull();
		await Assert.That(messages).Contains($"Attribute MV{uid}_1 moved to 1 destination.");
	}

	/// <summary>A tree moves leaves first, so no branch is cleared while it still has children.</summary>
	[Test]
	public async ValueTask MoveAttribute_PatternMovesATree()
	{
		var uid = Uid();
		var source = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternMvTreeSrc");
		var destination = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternMvTreeDst");
		await God($"&MT{uid} {source}=root");
		await God($"&MT{uid}`LEAF {source}=leaf");

		await God($"@mvattr {source}/MT{uid}**={destination}");

		await Assert.That((await Read(destination, $"MT{uid}"))?.Value.ToPlainText()).IsEqualTo("root");
		await Assert.That((await Read(destination, $"MT{uid}`LEAF"))?.Value.ToPlainText()).IsEqualTo("leaf");
		await Assert.That(await Read(source, $"MT{uid}`LEAF")).IsNull();
		await Assert.That(await Read(source, $"MT{uid}")).IsNull();
	}

	[Test]
	public async ValueTask CopyAttribute_PatternRefusesADestinationAttributeName()
	{
		var uid = Uid();
		var source = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpName");
		var destination = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternCpNameDst");
		await God($"&CN{uid}_1 {source}=one");

		await God($"@cpattr {source}/CN{uid}_*={destination}/RENAMED{uid}");

		await Assert.That(await Read(destination, $"RENAMED{uid}")).IsNull();
		await Assert.That(await Read(destination, $"CN{uid}_1")).IsNull()
			.Because("the whole command is refused, not just the named destination");
	}

	/// <summary>
	/// do_atrchown sets the attribute's owner to the new owner (<c>AL_CREATOR(ptr) = Owner(new_owner)</c>,
	/// <c>src/attrib.c:2605</c>), not to whoever ran the command; a pattern does it for every match.
	/// </summary>
	[Test]
	public async ValueTask AtrChown_PatternGivesEveryMatchToTheNewOwner()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternChown");
		var heir = await CreateIsolatedPlayer("AttrPatternHeir");
		await God($"&AC{uid}_1 {obj}=one");
		await God($"&AC{uid}_2 {obj}=two");
		await God($"&AD{uid} {obj}=other");

		await God($"@atrchown {obj}/AC{uid}_*={heir.DbRef}");

		foreach (var name in (string[])[$"AC{uid}_1", $"AC{uid}_2"])
		{
			var owner = await (await Read(obj, name))!.Owner.WithCancellation(CancellationToken.None);
			await Assert.That(owner?.Object.DBRef.Number).IsEqualTo(heir.DbRef.Number);
		}

		var untouched = await (await Read(obj, $"AD{uid}"))!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(untouched?.Object.DBRef.Number).IsNotEqualTo(heir.DbRef.Number);
	}

	[Test]
	public async ValueTask AtrChown_PlainNameGivesTheAttributeToTheNewOwner()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternChownOne");
		var heir = await CreateIsolatedPlayer("AttrPatternHeirOne");
		await God($"&AO{uid} {obj}=one");

		await God($"@atrchown {obj}/AO{uid}={heir.DbRef}");

		var owner = await (await Read(obj, $"AO{uid}"))!.Owner.WithCancellation(CancellationToken.None);
		await Assert.That(owner?.Object.DBRef.Number).IsEqualTo(heir.DbRef.Number);
	}

	[Test]
	public async ValueTask AtrLock_PatternLocksAndUnlocksEveryMatch()
	{
		var uid = Uid();
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrPatternLock");
		await God($"&AL{uid}_1 {obj}=one");
		await God($"&AL{uid}_2 {obj}=two");
		await God($"&AM{uid} {obj}=other");

		var messages = await MessagesWhile(WebAppFactoryArg.ExecutorDBRef, () => God($"@atrlock {obj}/AL{uid}_*=on"));

		await Assert.That(await HasFlag(obj, $"AL{uid}_1", "locked")).IsTrue();
		await Assert.That(await HasFlag(obj, $"AL{uid}_2", "locked")).IsTrue();
		await Assert.That(await HasFlag(obj, $"AM{uid}", "locked")).IsFalse();
		await Assert.That(messages).Contains($"Attribute AL{uid}_1 locked.");

		var state = await MessagesWhile(WebAppFactoryArg.ExecutorDBRef, () => God($"@atrlock {obj}/AL{uid}_*"));
		await Assert.That(state).Contains($"Attribute AL{uid}_2 is locked.");

		await God($"@atrlock {obj}/AL{uid}_*=off");

		await Assert.That(await HasFlag(obj, $"AL{uid}_1", "locked")).IsFalse();
		await Assert.That(await HasFlag(obj, $"AL{uid}_2", "locked")).IsFalse();
	}
}
