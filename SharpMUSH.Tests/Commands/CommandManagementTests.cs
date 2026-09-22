using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Runtime <c>@command</c> management — PennMUSH's <c>cmd_command</c>, <c>do_command_add</c>,
/// <c>do_command_clone</c> and <c>do_command_delete</c> (<c>src/command.c:1923-2200</c>) and the
/// examples in <c>help @command3</c>. Every test works on a command it adds or clones under a fresh
/// name, so the shared command table other tests use is left as it was. What a test reads back is
/// typed by a wizard of its own: God's notifications are shared by every test running alongside, so a
/// "Huh?" there may be anyone's. God is used only for what only God may do.
/// </summary>
public class CommandManagementTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private const string Huh = "Huh?  (Type \"help\" for help.)";

	private static string CommandName() => $"ZC{Guid.NewGuid():N}"[..12].ToUpperInvariant();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	private Task<List<string>> AsGod(string command)
		=> MessagesWhile(God, async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)));

	private Task<List<string>> As(TestIsolationHelpers.TestPlayer player, string command)
		=> MessagesWhile(player.DbRef, async () => await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)));

	private Task<TestIsolationHelpers.TestPlayer> Mortal(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<TestIsolationHelpers.TestPlayer> Wizard()
	{
		var wizard = await Mortal("CmdWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		return wizard;
	}

	/// <summary>
	/// <c>help @command3</c>'s first example: a /noparse command, overridden by a $-command, receives its
	/// argument unevaluated.
	/// </summary>
	[Test]
	public async ValueTask Add_NoParseCommandHookedToADollarCommand_GetsItsArgumentUnevaluated()
	{
		var wizard = await Wizard();
		var eat = CommandName();
		var machine = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DiningMachine");
		try
		{
			await As(wizard, $"&EAT {machine}=${eat} *:@pemit %#=Bite of %0.");
			var added = await As(wizard, $"@command/add/noparse {eat}");
			await As(wizard, $"@hook/override {eat}={machine},EAT");

			await Assert.That(added).Contains($"Command {eat} added.");
			await Assert.That(await As(wizard, $"{eat} meat loaf")).Contains("Bite of meat loaf.");
			await Assert.That(await As(wizard, $"{eat} randword(apple tomato pear)")).Contains("Bite of randword(apple tomato pear).");
		}
		finally
		{
			await HookService.ClearHookAsync(eat, "OVERRIDE");
		}
	}

	/// <summary>
	/// The second example: a command added without /noparse evaluates its arguments, and gets a /noeval
	/// switch that turns that off.
	/// </summary>
	[Test]
	public async ValueTask Add_ParsedCommand_EvaluatesItsArgumentsUnlessNoeval()
	{
		var wizard = await Wizard();
		var drink = CommandName();
		var machine = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DrinkMachine");
		try
		{
			await As(wizard, $"&DRINK {machine}=$^{drink.ToLowerInvariant()}(/noeval)? (.*)$:@pemit %#=Drinks %2.");
			await As(wizard, $"@set {machine}/DRINK=regexp");
			await As(wizard, $"@command/add {drink}");
			await As(wizard, $"@hook/override {drink}={machine},DRINK");

			await Assert.That(await As(wizard, $"{drink} reverse(tea)")).Contains("Drinks aet.");
			await Assert.That(await As(wizard, $"{drink}/noeval reverse(tea)")).Contains("Drinks reverse(tea).");
		}
		finally
		{
			await HookService.ClearHookAsync(drink, "OVERRIDE");
		}
	}

	/// <summary>An added command nothing hooks does nothing, and says so (<c>cmd_unimplemented</c>).</summary>
	[Test]
	public async ValueTask Add_UnhookedCommand_SaysItIsNotImplemented()
	{
		var wizard = await Wizard();
		var name = CommandName();
		await As(wizard, $"@command/add {name}");

		await Assert.That(await As(wizard, $"{name} anything")).Contains("This command has not been implemented.");
	}

	[Test]
	public async ValueTask Add_ExistingCommand_IsRefused()
	{
		var wizard = await Wizard();
		var messages = await As(wizard, "@command/add think");

		await Assert.That(messages).Contains("Command THINK already exists.");
	}

	[Test]
	public async ValueTask Add_Mortal_IsRefusedAndNothingIsAdded()
	{
		var wizard = await Wizard();
		var mortal = await Mortal("CmdAddMortal");
		var name = CommandName();

		var messages = await As(mortal, $"@command/add {name}");

		await Assert.That(messages).Contains("Permission denied.");
		await Assert.That(await As(wizard, $"{name} x")).Contains(Huh);
	}

	/// <summary><c>@command/alias</c> makes a second name for the same command.</summary>
	[Test]
	public async ValueTask Alias_RunsTheSameCommand_AndDeletingTheAliasLeavesTheCommand()
	{
		var wizard = await Wizard();
		var alias = CommandName();

		var set = await As(wizard, $"@command/alias think={alias}");
		var viaAlias = await As(wizard, $"{alias} aliased hello");
		var deleted = await AsGod($"@command/delete {alias}");
		var afterDelete = await As(wizard, $"{alias} aliased hello");

		await Assert.That(set).Contains("Alias set.");
		await Assert.That(viaAlias).Contains("aliased hello");
		await Assert.That(deleted).Contains($"Removed {alias} from command table.");
		await Assert.That(afterDelete).Contains(Huh);
		await Assert.That(await As(wizard, "think still here")).Contains("still here");
	}

	[Test]
	public async ValueTask Alias_ToAnExistingName_IsRefused()
	{
		var wizard = await Wizard();
		var messages = await As(wizard, "@command/alias think=say");

		await Assert.That(messages).Contains("Unable to set alias.");
	}

	/// <summary>
	/// <c>@command/clone</c> makes a separate copy that works the same and can be restricted apart from
	/// the original: restricting the clone to wizards leaves <c>think</c> open to everyone.
	/// </summary>
	[Test]
	public async ValueTask Clone_WorksLikeTheOriginal_AndIsRestrictedSeparately()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		var mortal = await Mortal("CmdClone");

		var cloned = await As(wizard, $"@command/clone think={clone}");
		await As(wizard, $"@command/restrict {clone}=wizard");

		await Assert.That(cloned).Contains("Command cloned.");
		await Assert.That(await As(wizard, $"{clone} cloned hello")).Contains("cloned hello");
		await Assert.That(await As(mortal, $"{clone} cloned hello")).DoesNotContain("cloned hello");
		await Assert.That(await As(mortal, "think original hello")).Contains("original hello");
	}

	[Test]
	public async ValueTask Clone_OfAnUnknownCommand_IsRefused()
	{
		var wizard = await Wizard();
		var messages = await As(wizard, $"@command/clone {CommandName()}={CommandName()}");

		await Assert.That(messages).Contains("No such command.");
	}

	/// <summary><c>@command/restrict</c> takes a lock as well as Penn's restriction words.</summary>
	[Test]
	public async ValueTask Restrict_TakesALock()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		var mortal = await Mortal("CmdLock");
		await As(wizard, $"@command/clone think={clone}");

		await As(wizard, $"@command/restrict {clone}=#{mortal.DbRef.Number}");

		await Assert.That(await As(mortal, $"{clone} locked hello")).Contains("locked hello");
		var other = await Mortal("CmdLockOther");
		await Assert.That(await As(other, $"{clone} locked hello")).DoesNotContain("locked hello");
	}

	[Test]
	public async ValueTask Restrict_WithNothing_AsksHow()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");

		await Assert.That(await As(wizard, $"@command/restrict {clone}=")).Contains("How do you want to restrict the command?");
	}

	/// <summary>
	/// A disabled command is not a command at all: the line falls through to $-commands and HUH
	/// (<c>src/command.c:1320</c>). Enabling it brings it back.
	/// </summary>
	[Test]
	public async ValueTask DisableAndEnable_TakeTheCommandOutOfTheTableAndBack()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");

		await As(wizard, $"@command/disable {clone}");
		var whileDisabled = await As(wizard, $"{clone} disabled hello");
		await As(wizard, $"@command/enable {clone}");
		var afterEnable = await As(wizard, $"{clone} enabled hello");

		await Assert.That(whileDisabled).Contains(Huh);
		await Assert.That(whileDisabled).DoesNotContain("disabled hello");
		await Assert.That(afterEnable).Contains("enabled hello");
	}

	[Test]
	public async ValueTask Disable_CommandItself_IsAlwaysEnabled()
	{
		var wizard = await Wizard();
		var messages = await As(wizard, "@command/disable @command");

		await Assert.That(messages).Contains("@command is ALWAYS enabled.");
		await Assert.That(await As(wizard, "@command think")).DoesNotContain(Huh);
	}

	[Test]
	public async ValueTask Disable_Mortal_IsRefused()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");
		var mortal = await Mortal("CmdDisable");

		await As(mortal, $"@command/disable {clone}");

		await Assert.That(await As(wizard, $"{clone} still enabled")).Contains("still enabled");
	}

	/// <summary><c>do_command_delete</c>: God only, and never a built-in command.</summary>
	[Test]
	public async ValueTask Delete_ABuiltIn_IsRefused()
	{
		var wizard = await Wizard();
		var messages = await AsGod("@command/delete think");

		await Assert.That(messages).Contains("You can't delete built-in commands. @command/disable instead.");
		await Assert.That(await As(wizard, "think survived")).Contains("survived");
	}

	[Test]
	public async ValueTask Delete_ByAWizardWhoIsNotGod_IsRefused()
	{
		var wizard = await Wizard();
		var name = CommandName();
		await As(wizard, $"@command/add {name}");

		var messages = await As(wizard, $"@command/delete {name}");

		await Assert.That(messages).Contains("Permission denied.");
		await Assert.That(await As(wizard, $"{name} x")).Contains("This command has not been implemented.");
	}

	[Test]
	public async ValueTask Delete_AnAddedCommand_RemovesItAndItsAliases()
	{
		var wizard = await Wizard();
		var name = CommandName();
		var alias = CommandName();
		await As(wizard, $"@command/add {name}");
		await As(wizard, $"@command/alias {name}={alias}");

		var messages = await AsGod($"@command/delete {name}");

		await Assert.That(messages).Contains($"Removed {name} and aliases from command table.");
		await Assert.That(await As(wizard, $"{name} x")).Contains(Huh);
		await Assert.That(await As(wizard, $"{alias} x")).Contains(Huh);
	}

	/// <summary>
	/// "Commands can also give any flag, power or type, to restrict to objects ... of one of those
	/// types" (<c>help restrict2</c>): naming a type is the whole allowed set, so the lock names that
	/// type alone. (Penn's own <c>restrict_command</c> ORs it into a command that already allows every
	/// type, which silently restricts nothing.)
	/// </summary>
	[Test]
	public async ValueTask Restrict_ToAType_LocksToThatTypeAlone()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");

		await As(wizard, $"@command/restrict {clone}=player");

		await Assert.That(await As(wizard, $"@command {clone}")).Contains("  Lock: (TYPE^PLAYER)");
		await Assert.That(await As(wizard, $"{clone} still mine")).Contains("still mine");
	}

	/// <summary>A negated type still subtracts from every type: that is what <c>noplayer</c> is for.</summary>
	[Test]
	public async ValueTask Restrict_WithANegatedType_KeepsTheOthers()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");

		await As(wizard, $"@command/restrict {clone}=noplayer");

		await Assert.That(await As(wizard, $"@command {clone}")).Contains("  Lock: (TYPE^THING|TYPE^ROOM|TYPE^EXIT)");
	}

	/// <summary>
	/// <c>do_command_delete</c> frees the command and its hooks with it (<c>src/command.c:2100-2104</c>),
	/// so a command added under the same name again starts unhooked.
	/// </summary>
	[Test]
	public async ValueTask Delete_TakesTheCommandsHooksWithIt()
	{
		var wizard = await Wizard();
		var name = CommandName();
		var machine = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookedThenDeleted");
		await As(wizard, $"&DO {machine}=${name} *:@pemit %#=Hooked %0.");
		await As(wizard, $"@command/add {name}");
		await As(wizard, $"@hook/override {name}={machine},DO");
		await Assert.That(await As(wizard, $"{name} once")).Contains("Hooked once.").Because("precondition");

		await AsGod($"@command/delete {name}");
		await As(wizard, $"@command/add {name}");

		await Assert.That(await As(wizard, $"{name} twice")).Contains("This command has not been implemented.");
		await Assert.That(await As(wizard, $"{name} twice")).DoesNotContain("Hooked twice.");
	}

	/// <summary>A disabled command is still taken: <c>command_find</c> finds it, so /add refuses the name.</summary>
	[Test]
	public async ValueTask Add_OnADisabledCommandsName_SaysItAlreadyExists()
	{
		var wizard = await Wizard();
		var clone = CommandName();
		await As(wizard, $"@command/clone think={clone}");
		await As(wizard, $"@command/disable {clone}");

		var messages = await As(wizard, $"@command/add {clone}");

		await Assert.That(messages).Contains($"Command {clone} already exists.");
	}

	/// <summary><c>command_find_exact</c> finds a disabled command, so God can delete one without enabling it.</summary>
	[Test]
	public async ValueTask Delete_ADisabledAddedCommand_RemovesIt()
	{
		var wizard = await Wizard();
		var name = CommandName();
		await As(wizard, $"@command/add {name}");
		await As(wizard, $"@command/disable {name}");

		var messages = await AsGod($"@command/delete {name}");

		await Assert.That(messages).Contains($"Removed {name} from command table.");
		await Assert.That(await As(wizard, $"@command/enable {name}")).Contains("No such command.");
		await Assert.That(await As(wizard, $"{name} x")).Contains(Huh);
	}

	/// <summary>Deleting an alias takes that alias's hooks with it, as deleting a command does.</summary>
	[Test]
	public async ValueTask Delete_AnAlias_TakesTheAliasHooksWithIt()
	{
		var wizard = await Wizard();
		var name = CommandName();
		var alias = CommandName();
		var machine = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AliasHooked");
		// The hook is keyed by the typed name (the alias), but its input carries the command's own
		// name, so the $-command matches on that. See #1223.
		await As(wizard, $"&DO {machine}=${name} *:@pemit %#=Aliased %0.");
		await As(wizard, $"@command/add {name}");
		await As(wizard, $"@command/alias {name}={alias}");
		await As(wizard, $"@hook/override {alias}={machine},DO");
		await Assert.That(await As(wizard, $"{alias} once")).Contains("Aliased once.").Because("precondition");

		await AsGod($"@command/delete {alias}");
		await As(wizard, $"@command/alias {name}={alias}");

		await Assert.That(await As(wizard, $"{alias} twice")).Contains("This command has not been implemented.");
		await Assert.That(await As(wizard, $"{alias} twice")).DoesNotContain("Aliased twice.");
	}

	/// <summary><c>=nobody</c> is <c>/disable</c>, so it is refused for the commands the game runs itself.</summary>
	[Test]
	public async ValueTask Restrict_NobodyOnACommandTheGameRuns_IsRefused()
	{
		var wizard = await Wizard();

		var before = WebAppFactoryArg.Notifications.CountFor(wizard.DbRef);
		var result = await Parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain("@command/restrict GOTO=nobody"));
		var messages = WebAppFactoryArg.Notifications.For(wizard.DbRef).Skip(before).ToList();

		await Assert.That(messages).Contains("GOTO is run by the game itself and cannot be disabled.");
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("the refusal is the command's result, not just a message");
		await Assert.That(await As(wizard, "@command GOTO")).Contains("Command: GOTO (Enabled)");
	}

	/// <summary>An added command is registered as a system entry so the trie matches it, but it is not built in.</summary>
	[Test]
	public async ValueTask Info_ForAnAddedCommand_SaysUserDefined()
	{
		var wizard = await Wizard();
		var name = CommandName();
		await As(wizard, $"@command/add {name}");

		await Assert.That(await As(wizard, $"@command {name}")).Contains("  Type: User-defined");
		await Assert.That(await As(wizard, "@command think")).Contains("  Type: Built-in");
	}
}
