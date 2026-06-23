using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Bare-minimum behavioural tests for <c>@hook/override</c> itself (independent of the scene package).
///
/// The override path (<c>SharpMUSHParserVisitor.ExecuteHookCode</c>) re-runs <c>$</c>-command matching
/// against <c>commandWithSwitches</c>, which is the command's <b>raw, pre-evaluation</b> source
/// (see <c>SharpMUSHParserVisitor.cs</c>: <c>namedRegisters["ARGS"] = src // before evaluation</c>).
/// Consequence: an override that re-uses its captured argument operates on the UNEVALUATED text, so a
/// <c>%0</c>/substitution coming from the surrounding command context survives verbatim into the
/// override body instead of being substituted first.
///
/// These tests pin that behaviour on a plain dummy object so the contract is visible without the scene
/// package. Each test overrides <c>@EMIT</c>, captures what the override saw into an attribute, reads it
/// straight from the database, and ALWAYS clears the hook in a finally (the hook is global session state).
/// </summary>
[NotInParallel]
public class HookOverrideBehaviorTests
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

	/// <summary>
	/// Sanity floor: an <c>@hook/override</c> fires and captures the command's literal argument. Proves the
	/// override plumbing works at all (matching, capture, body execution) before we probe substitution.
	/// </summary>
	[Test]
	public async ValueTask Override_CapturesLiteralCommandArgument()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookOvrLit");
		try
		{
			// $@emit (.*)$ → %1 is everything after "@emit ". Capture it into RESULT (no re-emit ⇒ no recursion).
			await Parser.CommandParse(1, ConnectionService,
				MModule.single($"&OVR {obj}=$(?i)^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/override @EMIT={obj},OVR"));

			await Parser.CommandParse(1, ConnectionService, MModule.single("@emit hello world"));

			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("hello world")
				.Because("the @EMIT override should capture the command's argument");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}

	/// <summary>
	/// The crux: when the overridden command is invoked from a surrounding context that supplies a
	/// substitution (here a wildcard <c>$</c>-command whose body is <c>@emit payload=%0</c>), the override
	/// must see the SUBSTITUTED argument (<c>payload=hello</c>) — the same text a normal <c>@emit</c> would
	/// have emitted. If the override instead captures the raw <c>payload=%0</c>, that is the root cause of
	/// the scene-package $-command regressions (every captured @emit re-emits literal %0).
	/// </summary>
	[Test]
	public async ValueTask Override_CapturesEvaluatedArgument_NotRawSubstitution()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookOvrSub");
		var token = TestIsolationHelpers.GenerateUniqueName("hov");
		try
		{
			await Parser.CommandParse(1, ConnectionService,
				MModule.single($"&OVR {obj}=$(?i)^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@hook/override @EMIT={obj},OVR"));

			// A wildcard $-command whose body @emits using %0. Fire it so @emit is dispatched with a
			// substitution in scope; %0 should be "hello" by the time @emit runs.
			await Parser.CommandParse(1, ConnectionService,
				MModule.single($"&WRAP {obj}=${token} *:@emit payload=%0"));
			await Parser.CommandParse(1, ConnectionService, MModule.single($"{token} hello"));

			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("payload=hello")
				.Because("the override must receive the EVALUATED @emit argument, not the raw pre-substitution text");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}
}
