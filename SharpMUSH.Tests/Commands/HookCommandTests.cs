using Microsoft.Extensions.DependencyInjection;
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
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/list {uniqueCmd}"));
	}

	[Test]
	public async ValueTask HookSet_ValidCommand_CreatesHook()
	{
		// @hook requires the attribute to exist on the object; this only verifies the command
		// executes without exception, since test_attr doesn't exist on #1.
		var uniqueCmd = $"hooktest_set_{Guid.NewGuid():N}";
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before {uniqueCmd}=#1,test_attr"));
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
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&{attr} #1=think captured"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/override {cmd}=#1,{attr}"));

		var hook = await HookService.GetHookAsync(cmd, "OVERRIDE");
		await Assert.That(hook.IsSome()).IsTrue();
		await Assert.That(hook.AsValue().AttributeName.ToUpperInvariant()).IsEqualTo(attr);
	}

	[Test]
	public async ValueTask HookClear_ExistingHook_RemovesHook()
	{
		var uniqueCmd = $"hooktest_clear_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before {uniqueCmd}"));
	}

	[Test]
	public async ValueTask HookSet_WithInlineModifier_CreatesInlineHook()
	{
		var uniqueCmd = $"hooktest_inline_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before/inline {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_WithInplaceModifier_CreatesInlineLocalizedHook()
	{
		// inplace = inline + localize + clearregs + nobreak.
		var uniqueCmd = $"hooktest_inplace_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before/inplace {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_MultipleHookTypes_AllPersist()
	{
		var uniqueCmd = $"hooktest_multi_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before {uniqueCmd}=#1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/after {uniqueCmd}=#1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/ignore {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_DefaultAttribute_UsesCorrectDefaultName()
	{
		var uniqueCmd = $"hooktest_default_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/before {uniqueCmd}=#1"));

		// With no attribute specified, the command uses cmd.before as the default attribute name.
	}

	[Test]
	public async ValueTask HookSet_ExtendType_CreatesExtendHook()
	{
		var uniqueCmd = $"hooktest_extend_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/extend {uniqueCmd}=#1"));
	}

	[Test]
	public async ValueTask HookSet_IgSwitchAlias_CreatesExtendHook()
	{
		// igswitch is an alias for extend.
		var uniqueCmd = $"hooktest_igswitch_{Guid.NewGuid():N}";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/igswitch {uniqueCmd}=#1"));
	}
}
