using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// $-command and ^-pattern matching, and the locks read around it, as PennMUSH's
/// <c>atr_comm_match</c> / <c>one_comm_match</c> (<c>src/attrib.c:1857-2181</c>) and <c>controls</c>
/// (<c>src/predicat.c:416</c>) do them. Every test drives players and objects of its own, each player
/// standing in a room of its own, and reads what it was told by a unique marker.
/// </summary>
public class CommandMatchParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private IPermissionService Permissions => WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
	private ILockService Locks => WebAppFactoryArg.Services.GetRequiredService<ILockService>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private const string Huh = "Huh?  (Type \"help\" for help.)";

	private static string Unique() => Guid.NewGuid().ToString("N")[..10].ToLowerInvariant();

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix, bool wizard = false)
	{
		var god = (await Node(God)).Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
		if (wizard) await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=WIZARD"));
		return player;
	}

	/// <summary>A thing <paramref name="owner"/> creates, so it is in their hands and theirs to set; NO_COMMAND off.</summary>
	private async Task<DBRef> Thing(TestIsolationHelpers.TestPlayer owner, string prefix)
	{
		var created = await TestIsolationHelpers.CreateObjectCommandAsync(Parser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName(prefix), owner.Handle);
		var dbref = DBRef.Parse(created.Message!.ToPlainText());
		await TestIsolationHelpers.ClearNoCommandAsync(Parser, ConnectionService, dbref);
		return dbref;
	}

	private async Task Run(TestIsolationHelpers.TestPlayer player, string command)
		=> await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>
	/// What <paramref name="player"/> is told by <paramref name="command"/> and anything it queued: an
	/// <c>@wait 0</c> barrier queued after it is heard only once the entries before it have run.
	/// </summary>
	private async Task<List<string>> Told(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Run(player, command);
		var barrier = TestIsolationHelpers.GenerateUniqueName("QueueBarrier");
		await Run(player, $"@wait 0=@pemit me={barrier}");
		await WebAppFactoryArg.Notifications.WaitForAsync(player.DbRef, barrier);
		return [.. WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before)];
	}

	/// <summary>
	/// atr_comm_match checks @lock/command and @lock/use on the object once a pattern matched
	/// (<c>src/attrib.c:2003-2018</c>). A refusal is no match; process_command then runs the object's
	/// COMMAND_LOCK`FAILURE triad (<c>fail_commands</c>, <c>src/game.c:2777-2790</c>, with
	/// <c>fail_lock</c>'s Command_Lock even when it was the use lock that refused) and says Huh? only when
	/// there was none (<c>src/game.c:1366-1372</c>).
	/// </summary>
	[Test]
	public async Task CommandAndUseLocks_RefuseTheCommand_WithCommandLockFailure()
	{
		var player = await Player("CmdLock");
		var id = Unique();
		var thing = await Thing(player, "CmdLockThing");
		await Run(player, $"&CMD {thing}=$zap{id}:@pemit %#=ran{id}");

		await Assert.That(await Told(player, $"zap{id}")).Contains($"ran{id}");

		await Run(player, $"@lock/command {thing}=#FALSE");
		var refused = await Told(player, $"zap{id}");
		await Assert.That(refused).DoesNotContain($"ran{id}");
		await Assert.That(refused).Contains(Huh);

		await Run(player, $"&COMMAND_LOCK`FAILURE {thing}=nope{id}");
		var failed = await Told(player, $"zap{id}");
		await Assert.That(failed).Contains($"nope{id}");
		await Assert.That(failed).DoesNotContain(Huh);

		await Run(player, $"@lock/command {thing}=#TRUE");
		await Run(player, $"@lock/use {thing}=#FALSE");
		var useRefused = await Told(player, $"zap{id}");
		await Assert.That(useRefused).DoesNotContain($"ran{id}");
		await Assert.That(useRefused).Contains($"nope{id}");
	}

	/// <summary>
	/// The command lock is read with <c>getlock</c> on the child (<c>src/attrib.c:2009</c>), so a parent's
	/// lock set <c>!no_inherit</c> refuses the child's commands; a parent's default (no_inherit) lock does not.
	/// </summary>
	[Test]
	public async Task CommandLock_IsReadOnTheChild_ThroughItsParent()
	{
		var player = await Player("CmdLockParent");
		var id = Unique();
		var parent = await Thing(player, "CmdLockP");
		var child = await Thing(player, "CmdLockC");
		await Run(player, $"@parent {child}={parent}");
		await Run(player, $"&CMD {parent}=$zip{id}:@pemit %#=ran{id}");
		await Run(player, $"@lock/command {parent}=#FALSE");

		// The parent itself refuses; the child, whose inherited lock is still private, runs it.
		var told = await Told(player, $"zip{id}");
		await Assert.That(told.Count(line => line == $"ran{id}")).IsEqualTo(1);

		await Run(player, $"@lset {parent}/command=!no_inherit");
		var refused = await Told(player, $"zip{id}");
		await Assert.That(refused).DoesNotContain($"ran{id}");
	}

	/// <summary>
	/// On a parent, atr_comm_match tests no_command before the seen check (<c>src/attrib.c:1960-1995</c>):
	/// a parent's no_command FOO masks a grandparent's FOO`BAR even though the child has a FOO of its own.
	/// </summary>
	[Test]
	public async Task ParentNoCommand_MasksFartherTree_EvenWhenChildShadowsTheRoot()
	{
		var player = await Player("NoCmdMask");
		var id = Unique();
		var child = await Thing(player, "NoCmdC");
		var parent = await Thing(player, "NoCmdP");
		var grandparent = await Thing(player, "NoCmdG");
		await Run(player, $"@parent {child}={parent}");
		await Run(player, $"@parent {parent}={grandparent}");
		await Run(player, $"&FOO {child}=data");
		await Run(player, $"&FOO {parent}=x");
		await Run(player, $"&FOO {grandparent}=x");
		await Run(player, $"&FOO`BAR {grandparent}=$gp{id}:@pemit %#=gp{id}");

		var unmasked = await Mediator.Send(new GetCommandAttributesQuery(await Node(child)));
		await Assert.That(unmasked.Select(c => c.Attribute.LongName)).Contains("FOO`BAR");

		await Run(player, $"@set {parent}/FOO=no_command");
		var masked = await Mediator.Send(new GetCommandAttributesQuery(await Node(child)));
		await Assert.That(masked.Select(c => c.Attribute.LongName)).DoesNotContain("FOO`BAR");
	}

	/// <summary>
	/// <c>run_cmd_hook</c> (<c>src/command.c:2459-2465</c>): an /override hook naming an attribute tries only
	/// that attribute (<c>one_comm_match</c>), refused when it is no_command; without an attribute it tries
	/// every $-command on the object (<c>atr_comm_match</c>).
	/// </summary>
	[Test]
	public async Task OverrideHook_WithAttribute_MatchesOnlyThatAttribute()
	{
		var wizard = await Player("HookOne", wizard: true);
		var id = Unique();
		var command = $"ZH{id}".ToUpperInvariant();
		var machine = await Thing(wizard, "HookMachine");
		try
		{
			await Run(wizard, $"&ONE {machine}=${command} *:@pemit %#=one{id} %0");
			await Run(wizard, $"&TWO {machine}=${command} *:@pemit %#=two{id} %0");
			await Run(wizard, $"@command/add {command}");
			await Run(wizard, $"@hook/override {command}={machine},ONE");

			var named = await Told(wizard, $"{command} x");
			await Assert.That(named).Contains($"one{id} x");
			await Assert.That(named).DoesNotContain($"two{id} x");

			await Run(wizard, $"@set {machine}/ONE=no_command");
			var refused = await Told(wizard, $"{command} y");
			await Assert.That(refused).DoesNotContain($"one{id} y");
			await Assert.That(refused).DoesNotContain($"two{id} y");

			await Run(wizard, $"@hook/override {command}={machine}");
			var whole = await Told(wizard, $"{command} z");
			await Assert.That(whole).Contains($"two{id} z");
			await Assert.That(whole).DoesNotContain($"one{id} z");
		}
		finally
		{
			await HookService.ClearHookAsync(command, "OVERRIDE");
		}
	}

	/// <summary>
	/// <c>controls()</c> reads the Control lock with <c>getlock_noparent</c> (<c>src/predicat.c:416</c>): a
	/// parent's control lock, even set !no_inherit, grants nothing over its children.
	/// </summary>
	[Test]
	public async Task ControlLock_IsNotInherited()
	{
		var owner = await Player("CtlOwner");
		var other = await Player("CtlOther");
		var parent = await Thing(owner, "CtlP");
		var child = await Thing(owner, "CtlC");
		await Run(owner, $"@parent {child}={parent}");
		await Run(owner, $"@lock/control {parent}=#{other.DbRef.Number}");
		await Run(owner, $"@lset {parent}/control=!no_inherit");

		var who = await Node(other.DbRef);
		await Assert.That(await Permissions.Controls(who, await Node(parent))).IsTrue();
		await Assert.That(await Permissions.Controls(who, await Node(child))).IsFalse();
	}

	/// <summary>
	/// <c>Can_Forward</c> asks <c>getlock(x, Forward_Lock) != TRUE_BOOLEXP</c> (<c>hdrs/mushdb.h:124-127</c>),
	/// and <c>getlock</c> inherits: a parent's !no_inherit forward lock counts as set on the child.
	/// </summary>
	[Test]
	public async Task SetLockCheck_SeesAnInheritedLock()
	{
		var owner = await Player("FwdOwner");
		var other = await Player("FwdOther");
		var parent = await Thing(owner, "FwdP");
		var child = await Thing(owner, "FwdC");
		await Run(owner, $"@parent {child}={parent}");
		var who = await Node(other.DbRef);

		await Assert.That(await Permissions.PassesSetLock(who, await Node(child), LockType.Forward)).IsFalse();

		await Run(owner, $"@lock/forward {parent}=#{other.DbRef.Number}");
		await Run(owner, $"@lset {parent}/forward=!no_inherit");
		await Assert.That(await Permissions.PassesSetLock(who, await Node(child), LockType.Forward)).IsTrue();
	}

	/// <summary>
	/// An eval lock reads its attribute with <c>fetch_ufun_attrib</c>, an <c>atr_get</c> that inherits
	/// (<c>check_attrib_lock</c>, <c>src/boolexp.c:1983</c>).
	/// </summary>
	[Test]
	public async Task EvalLock_ReadsTheAttributeThroughParents()
	{
		var owner = await Player("EvalLock");
		var parent = await Thing(owner, "EvalP");
		var child = await Thing(owner, "EvalC");
		await Run(owner, $"@parent {child}={parent}");
		await Run(owner, $"&CANGET {parent}=1");
		await Run(owner, $"@lock {child}=CANGET/1");

		await Assert.That(await Locks.Evaluate(LockType.Basic, await Node(child), await Node(owner.DbRef))).IsTrue();
	}

	/// <summary>
	/// @sweep's "[commands]" is <c>Commer</c> (<c>src/game.c:1592-1601</c>): the object's own attributes
	/// only, so a child that merely inherits a $-command does not respond to commands.
	/// </summary>
	[Test]
	public async Task Commer_LooksAtOwnAttributesOnly()
	{
		var owner = await Player("Commer");
		var id = Unique();
		var parent = await Thing(owner, "CommerP");
		var child = await Thing(owner, "CommerC");
		await Run(owner, $"@parent {child}={parent}");
		await Run(owner, $"&CMD {parent}=$xyzzy{id}:think hi");

		await Assert.That(await (await Node(parent)).HasActiveCommands()).IsTrue();
		await Assert.That(await (await Node(child)).HasActiveCommands()).IsFalse();
	}

	/// <summary>The dbref numbers <paramref name="search"/>, evaluated by <paramref name="player"/>, returns.</summary>
	private async Task<int[]> Found(TestIsolationHelpers.TestPlayer player, string search)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("Found");
		var told = await Told(player, $"think {marker} [{search}]");
		return [.. told.Single(line => line.StartsWith(marker)).Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
			.Select(entry => DBRef.Parse(entry).Number)];
	}

	/// <summary>
	/// <c>raw_search</c> tests <c>command=</c> and <c>listen=</c> with <c>atr_comm_match</c>
	/// (<c>src/wiz.c:2568-2586</c>): $-commands through the @parent chain, ^-patterns through it only for a
	/// LISTEN_PARENT object.
	/// </summary>
	[Test]
	public async Task Search_CommandAndListen_SeeInheritedPatterns()
	{
		var owner = await Player("SearchCmd");
		var id = Unique();
		var parent = await Thing(owner, "SearchP");
		var child = await Thing(owner, "SearchC");
		await Run(owner, $"@parent {child}={parent}");
		await Run(owner, $"&CMD {parent}=$xyzzy{id}:think x");
		await Run(owner, $"&HEAR {parent}=^plugh{id} *:think y");

		var commands = await Found(owner, $"lsearch(me,command,xyzzy{id})");
		await Assert.That(commands).Contains(parent.Number).And.Contains(child.Number);

		var plain = await Found(owner, $"lsearch(me,listen,plugh{id} now)");
		await Assert.That(plain).Contains(parent.Number).And.DoesNotContain(child.Number);

		await Run(owner, $"@set {child}=LISTEN_PARENT");
		await Assert.That(await Found(owner, $"lsearch(me,listen,plugh{id} now)")).Contains(child.Number);
	}
}
