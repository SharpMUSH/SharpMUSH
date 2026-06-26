using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Behavioural PROOF of <c>@hook/ignore</c> (independent of the scene package).
///
/// Contract (<c>SharpMUSHParserVisitor.cs</c> ~1494): the <c>/ignore</c> attribute is evaluated BEFORE the
/// built-in command. If it returns a FALSE value (empty / <c>"0"</c> / <c>"#-1"</c>) the dispatcher
/// returns early (<c>CallState.Empty</c>) and the command — plus everything downstream of the gate
/// (before/override/built-in) — is SKIPPED. A TRUE value falls through and the command runs.
///
/// We prove BOTH branches on <c>@EMIT</c> using a downstream <c>@hook/override @EMIT</c> as the observable:
/// the override writes a marker attribute, and it can only fire if execution got PAST the <c>/ignore</c>
/// gate. A <c>GATE</c> attribute toggles the ignore result between <c>1</c> (true) and <c>0</c> (false).
/// Both hooks are always cleared in <c>finally</c> (hooks are global session state). This project runs the
/// bare engine (no bundled packages), so there is no competing scene <c>@EMIT</c> hook.
/// </summary>
[NotInParallel]
public class HookIgnoreBehaviorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private async Task<string> ReadAttributeAsync(DBRef obj, string attribute) =>
		(await Database.GetAttributeAsync(obj, attribute.Split('`'), CancellationToken.None).LastOrDefaultAsync())
			?.Value.ToPlainText() ?? "";

	/// <summary>Sets up an @EMIT override that writes <c>RAN=yes</c>, plus an @EMIT /ignore reading GATE.</summary>
	private async Task ArmAsync(DBRef obj, string gateValue)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&OVR {obj}=$(?i)^@emit (.*)$:&RAN {obj}=yes"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/OVR=regexp"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/override @EMIT={obj},OVR"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&GATE {obj}={gateValue}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/ignore @EMIT={obj},GATE"));
	}

	[Test]
	public async ValueTask Ignore_ReturnsTrue_CommandProceeds()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookIgnTrue");
		try
		{
			await ArmAsync(obj, "1");

			await Parser.CommandParse(1, ConnectionService, MModule.single("@emit hello"));

			await Assert.That(await ReadAttributeAsync(obj, "RAN")).IsEqualTo("yes")
				.Because("a TRUE /ignore lets @emit proceed — execution reaches the downstream override");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "IGNORE");
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}

	[Test]
	public async ValueTask Ignore_ReturnsFalse_CommandSkipped()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookIgnFalse");
		try
		{
			await ArmAsync(obj, "0");

			await Parser.CommandParse(1, ConnectionService, MModule.single("@emit hello"));

			await Assert.That(await ReadAttributeAsync(obj, "RAN")).IsEqualTo("")
				.Because("a FALSE /ignore SKIPS @emit entirely — execution never reaches the override or built-in");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "IGNORE");
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}
}
