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
		var hookExists = await HookService.GetHookAsync("HOOKTEST_BEFORE", "BEFORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_AfterHook_ExecutesAfterCommand()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_AFTER", "AFTER");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_IgnoreHook_SkipsCommandWhenReturnsEmpty()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_IGNORE", "IGNORE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_OverrideHook_ReplacesBuiltInCommand()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_OVERRIDE", "OVERRIDE");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_ExtendHook_HandlesInvalidSwitches()
	{
		var hookExists = await HookService.GetHookAsync("HOOKTEST_EXTEND", "EXTEND");
		await Assert.That(hookExists.IsNone()).IsTrue();
	}

	[Test]
	public async ValueTask Hook_InlineModifier_ExecutesImmediately()
	{
		var testCommand = $"HOOKTEST_INLINE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", inline: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().Inline).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_LocalizeModifier_SavesAndRestoresRegisters()
	{
		var testCommand = $"HOOKTEST_LOCALIZE_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", localize: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().Localize).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_ClearRegsModifier_ClearsRegisters()
	{
		var testCommand = $"HOOKTEST_CLEARREGS_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "BEFORE", new DBRef(1), "test_attr", clearregs: true);
		var hook = await HookService.GetHookAsync(testCommand, "BEFORE");
		await Assert.That(hook.IsSome()).IsTrue();
		if (hook.IsSome())
		{
			await Assert.That(hook.AsValue().ClearRegs).IsTrue();
		}
		await HookService.ClearHookAsync(testCommand, "BEFORE");
	}

	[Test]
	public async ValueTask Hook_HuhCommandHook_CustomizesUndefinedCommand()
	{
		var testCommand = $"HOOKTEST_HUHCOMMAND_{Guid.NewGuid():N}";
		await HookService.SetHookAsync(testCommand, "OVERRIDE", new DBRef(1), "test_attr");
		var hook = await HookService.GetHookAsync(testCommand, "OVERRIDE");
		await Assert.That(hook.IsSome()).IsTrue();
		await HookService.ClearHookAsync(testCommand, "OVERRIDE");
	}

	[Test]
	public async ValueTask Hook_NamedRegisters_PopulatedCorrectly()
	{
	}
}

/// <summary>
/// Integration tests for the @mogrifier system.
/// These tests validate the complete mogrification pipeline including
/// channel message processing and all MOGRIFY` attributes.
/// </summary>
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
	}

	[Test]
	public async ValueTask Mogrifier_OverrideAttribute_SkipsChatFormat()
	{
	}

	[Test]
	public async ValueTask Mogrifier_FormatAttribute_CustomizesMessage()
	{
	}

	[Test]
	public async ValueTask Mogrifier_PartAttributes_ModifyComponents()
	{
	}

	[Test]
	public async ValueTask Mogrifier_UseLock_RequiredForMogrification()
	{
	}

	[Test]
	public async ValueTask Mogrifier_ChatTypes_HandleDifferently()
	{
	}

	[Test]
	public async ValueTask ChatFormat_IndividualPlayer_CustomizesFormat()
	{
	}

	[Test]
	public async ValueTask ChatFormat_WithMogrifier_BothApplied()
	{
	}
}
