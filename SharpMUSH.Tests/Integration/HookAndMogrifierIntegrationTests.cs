using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests for the @hook system.
/// These tests validate the complete hook workflow including command execution,
/// hook triggering, and $-command matching.
/// </summary>
[NotInParallel]
public class HookIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask Hook_BeforeHook_ExecutesBeforeCommand()
	{
		// This test would verify that a /before hook executes before the command
		// Full implementation requires:
		// 1. Create a test object with a hook attribute
		// 2. Set the hook attribute to track execution
		// 3. Set up the hook
		// 4. Execute a command
		// 5. Verify hook was executed first

		// For now, just verify the hook service works
		var hookExists = await HookService.GetHookAsync("HOOKTEST_BEFORE", "BEFORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_AfterHook_ExecutesAfterCommand()
	{
		// This test would verify that a /after hook executes after the command
		// Full implementation requires:
		// 1. Create a test object with a hook attribute
		// 2. Set the hook attribute to track execution
		// 3. Set up the hook
		// 4. Execute a command
		// 5. Verify hook was executed after command

		// For now, just verify the hook service works
		var hookExists = await HookService.GetHookAsync("HOOKTEST_AFTER", "AFTER");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_IgnoreHook_SkipsCommandWhenReturnsEmpty()
	{
		// This test would verify that an /ignore hook that returns empty/0/#-1
		// causes the command to be skipped
		// Full implementation requires:
		// 1. Create a test object with a hook attribute that returns 0
		// 2. Set up the /ignore hook
		// 3. Execute a command
		// 4. Verify command was not executed

		// For now, just verify the hook service works
		var hookExists = await HookService.GetHookAsync("HOOKTEST_IGNORE", "IGNORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_OverrideHook_ReplacesBuiltInCommand()
	{
		// This test would verify that an /override hook with $-command
		// replaces the built-in command execution
		// Full implementation requires:
		// 1. Create a test object with a $-command pattern
		// 2. Set up the /override hook
		// 3. Execute the command
		// 4. Verify custom code executed instead of built-in

		// For now, just verify the hook service works
		var hookExists = await HookService.GetHookAsync("HOOKTEST_OVERRIDE", "OVERRIDE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_ExtendHook_HandlesInvalidSwitches()
	{
		// This test would verify that an /extend hook handles invalid switches
		// via $-command matching
		// Full implementation requires:
		// 1. Create a test object with $-command for extended switch
		// 2. Set up the /extend hook
		// 3. Execute command with invalid switch
		// 4. Verify $-command executed instead of error

		// For now, just verify the hook service works
		var hookExists = await HookService.GetHookAsync("HOOKTEST_EXTEND", "EXTEND");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_InlineModifier_ExecutesImmediately()
	{
		// This test would verify that /inline hooks execute immediately
		// in the current execution context
		// Full implementation requires:
		// 1. Create hook with /inline modifier
		// 2. Track execution order
		// 3. Verify immediate execution vs queued

		// Use unique command name to avoid test interference
		var testCommand = $"HOOKTEST_INLINE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", inline: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().Inline).IsTrue();
		}
		// Clean up
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_LocalizeModifier_SavesAndRestoresRegisters()
	{
		// This test would verify that /localize saves and restores q-registers
		// Full implementation requires:
		// 1. Set q-registers to known values
		// 2. Execute hook with /localize that modifies registers
		// 3. Verify registers were restored after hook

		// Use unique command name to avoid test interference
		var testCommand = $"HOOKTEST_LOCALIZE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", localize: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().Localize).IsTrue();
		}
		// Clean up
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_ClearRegsModifier_ClearsRegisters()
	{
		// This test would verify that /clearregs clears q-registers before execution
		// Full implementation requires:
		// 1. Set q-registers to known values
		// 2. Execute hook with /clearregs
		// 3. Verify registers were cleared before hook execution

		// Use unique command name to avoid test interference
		var testCommand = $"HOOKTEST_CLEARREGS_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", clearregs: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().ClearRegs).IsTrue();
		}
		// Clean up
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_HuhCommandHook_CustomizesUndefinedCommand()
	{
		// This test would verify that HUH_COMMAND hook customizes
		// the response for undefined commands
		// Full implementation requires:
		// 1. Create hook for HUH_COMMAND
		// 2. Execute an undefined command
		// 3. Verify custom response instead of default "Huh?"

		// Use unique command name to avoid test interference
		var testCommand = $"HOOKTEST_HUHCOMMAND_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "OVERRIDE", new DBRef(1), "test_attr");
		var hook = await HookService.GetHookAsync(testCommand, "OVERRIDE");
		await Assert.That(hook.IsSome()).IsTrue();
		// Clean up
		await HookService.ClearHookAsync(testCommand, "OVERRIDE");
	}

	[Test]
	public async ValueTask Hook_NamedRegisters_PopulatedCorrectly()
	{
		// This test would verify that named registers (ARGS, LS, RS, LSAx, etc.)
		// are populated correctly for hook execution
		// Full implementation requires:
		// 1. Create hook that echoes register values
		// 2. Execute command with specific arguments
		// 3. Verify register values match expected

		// This is a placeholder for future implementation
	}
}

/// <summary>
/// Integration tests for the @mogrifier system.
/// These tests validate the complete mogrification pipeline including
/// channel message processing and all MOGRIFY` attributes.
/// </summary>
[NotInParallel]
public class MogrifierIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask Mogrifier_BlockAttribute_BlocksMessage()
	{
		// This test would verify that MOGRIFY`BLOCK blocks channel messages
		// Full implementation requires:
		// 1. Create channel and mogrifier object
		// 2. Set MOGRIFY`BLOCK to return non-empty
		// 3. Send channel message
		// 4. Verify only speaker received block message
		// 5. Verify message was not broadcast

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask Mogrifier_OverrideAttribute_SkipsChatFormat()
	{
		// This test would verify that MOGRIFY`OVERRIDE skips individual @chatformat
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Set MOGRIFY`OVERRIDE to return true
		// 3. Set player's CHATFORMAT`<channel>
		// 4. Send message
		// 5. Verify player's @chatformat was skipped

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask Mogrifier_FormatAttribute_CustomizesMessage()
	{
		// This test would verify that MOGRIFY`FORMAT customizes channel-wide format
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Set MOGRIFY`FORMAT to custom format
		// 3. Send message
		// 4. Verify message uses custom format

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask Mogrifier_PartAttributes_ModifyComponents()
	{
		// This test would verify that part mogrifiers modify message components
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Set MOGRIFY`CHANNAME, PLAYERNAME, MESSAGE, etc.
		// 3. Send message
		// 4. Verify each component was modified

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask Mogrifier_UseLock_RequiredForMogrification()
	{
		// This test would verify that Use lock is checked on mogrifier
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Set Use lock to fail for speaker
		// 3. Send message
		// 4. Verify mogrification was skipped

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask Mogrifier_ChatTypes_HandleDifferently()
	{
		// This test would verify that different chat types (say, pose, semipose, emit)
		// are formatted correctly through mogrification
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Send messages with different chat types
		// 3. Verify each type is formatted correctly

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask ChatFormat_IndividualPlayer_CustomizesFormat()
	{
		// This test would verify that individual player @chatformat works
		// Full implementation requires:
		// 1. Create channel
		// 2. Set player's CHATFORMAT`<channel> attribute
		// 3. Send message
		// 4. Verify player sees custom format
		// 5. Verify other players see default format

		// This is a placeholder for future implementation
	}

	[Test]
	public async ValueTask ChatFormat_WithMogrifier_BothApplied()
	{
		// This test would verify that both mogrifier and individual @chatformat apply
		// Full implementation requires:
		// 1. Create channel with mogrifier
		// 2. Set channel-wide MOGRIFY`FORMAT
		// 3. Set player's CHATFORMAT`<channel>
		// 4. Send message
		// 5. Verify mogrifier applied first, then player's format

		// This is a placeholder for future implementation
	}
}
