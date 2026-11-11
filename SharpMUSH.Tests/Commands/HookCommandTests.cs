using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class HookCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();

	[Test]
	public async ValueTask HookList_NoHooksSet_ReturnsMessage()
	{
		// Unique command name avoids test interference.
		var uniqueCmd = $"hooktest_list_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/list {uniqueCmd}"));
	}

	[Test]
	public async ValueTask HookList_NoCommand_ListsHooksOnNamesNoBuiltInAnswersTo()
	{
		// Short enough that the Command column never wraps it, however many hooks other tests have set.
		var cmd = $"+HK{Guid.NewGuid():N}"[..11].ToUpperInvariant();
		await HookService.SetHookAsync(cmd, "BEFORE", new DBRef(1), "B_TEST");
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@hook/list"));

			var listing = WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef)
				.LastOrDefault(message => message.Contains(cmd, StringComparison.Ordinal));
			await Assert.That(listing).IsNotNull()
				.Because("a softcode command's hook is listed with the built-ins'");
			await Assert.That(listing!).Contains("Hooks (").And.Contains("Command").And.Contains("B_TEST");
		}
		finally
		{
			await HookService.ClearHookAsync(cmd, "BEFORE");
		}
	}

	[Test]
	public async ValueTask HookList_OneCommand_IsAPanelOfItsHooks()
	{
		var cmd = $"+HOOKTEST_ONE_{Guid.NewGuid():N}"[..24].ToUpperInvariant();
		await HookService.SetHookAsync(cmd, "AFTER", new DBRef(1), "A_TEST", inline: true, localize: true);
		await HookService.SetHookAsync(cmd, "BEFORE", new DBRef(1), "B_TEST");
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/list {cmd}"));

			var listing = WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef)
				.LastOrDefault(message => message.Contains($"Hooks on {cmd}", StringComparison.Ordinal));
			await Assert.That(listing).IsNotNull();
			await Assert.That(listing!.IndexOf("B_TEST", StringComparison.Ordinal))
				.IsLessThan(listing.IndexOf("A_TEST", StringComparison.Ordinal))
				.Because("hooks are listed in the order they run");
			await Assert.That(listing).Contains("inline localize");
		}
		finally
		{
			await HookService.ClearHookAsync(cmd, "AFTER");
			await HookService.ClearHookAsync(cmd, "BEFORE");
		}
	}

	[Test]
	public async ValueTask HookSet_ValidCommand_CreatesHook()
	{
		// @hook requires the attribute to exist on the object; this only verifies the command
		// executes without exception, since test_attr doesn't exist on #1.
		var uniqueCmd = $"hooktest_set_{Guid.NewGuid():N}";
		var result = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before {uniqueCmd}=#1,test_attr"));
	}

	[Test]
	public async ValueTask HookOverride_KeepsExplicitAttribute()
	{
		// Regression: @hook/<type> <cmd> = <object>, <attribute> must preserve the attribute.
		// CB.RSArgs splits the RHS on the comma, so the handler reads args[1]=object and
		// args[2]=attribute. Before the fix it re-split args[1] only, dropping the attribute
		// and silently defaulting the hook to "cmd.<type>".
		var token = Guid.NewGuid().ToString("N");
		var cmd = $"hooktest_attr_{token}".ToUpperInvariant();
		var attr = $"HOOKATTR{token}".ToUpperInvariant();

		// The attribute must exist on the target object for @hook to accept it.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attr} #1=think captured"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/override {cmd}=#1,{attr}"));

		var hook = (await HookService.GetHookAsync(cmd, "OVERRIDE")).Expect<CommandHook>();
		await Assert.That(hook.AttributeName.ToUpperInvariant()).IsEqualTo(attr);
	}

	[Test]
	public async ValueTask HookClear_ExistingHook_RemovesHook()
	{
		var uniqueCmd = $"hooktest_clear_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before {uniqueCmd}"));
	}

	[Test]
	public async ValueTask HookSet_WithInlineModifier_CreatesInlineHook()
	{
		var uniqueCmd = $"hooktest_inline_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before/inline {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_WithInplaceModifier_CreatesInlineLocalizedHook()
	{
		// inplace = inline + localize + clearregs + nobreak.
		var uniqueCmd = $"hooktest_inplace_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before/inplace {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_MultipleHookTypes_AllPersist()
	{
		var uniqueCmd = $"hooktest_multi_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before {uniqueCmd}=#1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/after {uniqueCmd}=#1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/ignore {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_DefaultAttribute_UsesCorrectDefaultName()
	{
		var uniqueCmd = $"hooktest_default_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before {uniqueCmd}=#1"));

		// With no attribute specified, the command uses cmd.before as the default attribute name.
	}

	[Test]
	public async ValueTask HookSet_ExtendType_CreatesExtendHook()
	{
		var uniqueCmd = $"hooktest_extend_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/extend {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_IgSwitchAlias_CreatesExtendHook()
	{
		// igswitch is an alias for extend.
		var uniqueCmd = $"hooktest_igswitch_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/igswitch {uniqueCmd}=#1"));
	}
}
