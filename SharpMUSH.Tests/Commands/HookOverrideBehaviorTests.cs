using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests that override hooks receive the arguments produced by the command's own parser.
/// Each test captures the hook argument in the database and clears its global hook in finally.
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

	[Test]
	[Arguments("SAY", "OVERRIDE", "", "ansi(hr,rawr)", "rawr")]
	[Arguments("POSE", "OVERRIDE", "", "ansi(hr,rawr)", "rawr")]
	[Arguments("SAY", "EXTEND", "/custom", "ansi(hr,rawr)", "rawr")]
	[Arguments("TEACH", "OVERRIDE", "", "[add(1,2)]", "[add(1,2)]")]
	[Arguments("@PEMIT", "OVERRIDE", "", "add(1,2)=add(3,4)", "3=7")]
	[Arguments("@FORCE", "OVERRIDE", "", "add(1,2)=[add(3,4)]", "3=7")]
	[Arguments("@DOLIST", "OVERRIDE", "", "add(1,2)=[add(3,4)]", "3=[add(3,4)]")]
	[Arguments("@PEMIT", "OVERRIDE", "", "add(1,2)=", "3=")]
	[Arguments("@DIG", "OVERRIDE", "", "add(1,2)=add(3,4),,add(5,6)", "3=7,,11")]
	[Arguments("SAY", "OVERRIDE", "/noeval", "[add(1,2)]", "[add(1,2)]")]
	[Arguments("SAY", "OVERRIDE", "", "[setq(hook_count,inc(firstof(%q<hook_count>,0)))]%q<hook_count>", "1")]
	public async ValueTask Hook_UsesCommandArgumentParsing(string command, string hookType,
		string switches, string input, string expected)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookParsing");
		try
		{
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&OVR {obj}=${command}{switches} *:&RESULT {obj}=%0"));
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@hook/{hookType} {command}={obj},OVR"));
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"{command}{switches} {input}"));
			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo(expected);
			if (input == "ansi(hr,rawr)")
			{
				var captured = (await Database.GetAttributeAsync(obj, ["RESULT"], CancellationToken.None).LastAsync()).Value;
				await Assert.That(captured.Render(MarkupFormat.Ansi)).Contains("\u001b[");
			}
		}
		finally
		{
			await HookService.ClearHookAsync(command, hookType);
		}
	}

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
			// No re-emit in the override body ⇒ no recursion.
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&OVR {obj}=$(?i)^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/override @EMIT={obj},OVR"));

			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@emit hello world"));

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
				MarkupText.Plain($"&OVR {obj}=$(?i)^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/override @EMIT={obj},OVR"));

			// A wildcard $-command whose body @emits using %0. Fire it so @emit is dispatched with a
			// substitution in scope; %0 should be "hello" by the time @emit runs.
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&WRAP {obj}=${token} *:@emit payload=%0"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} hello"));

			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("payload=hello")
				.Because("the override must receive the EVALUATED @emit argument, not the raw pre-substitution text");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}

	/// <summary>
	/// Same contract as <see cref="Override_CapturesEvaluatedArgument_NotRawSubstitution"/>, but through
	/// <c>@hook/override/inline</c>. Both spellings now reach the matched <c>$</c>-command through one
	/// dispatch path; <c>hook.Inline</c> survives only as a register-handling flag, gating the
	/// <c>/localize</c> save-restore and the <c>/clearregs</c> wipe around that dispatch. This test holds
	/// the two spellings to the same result, so the shared path cannot regress into treating them
	/// differently: nothing about a matched <c>$</c>-command's execution depends on the inline flag.
	/// </summary>
	[Test]
	public async ValueTask OverrideInline_CapturesEvaluatedArgument_SameAsNonInline()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookOvrInline");
		var token = TestIsolationHelpers.GenerateUniqueName("hovi");
		try
		{
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&OVR {obj}=$(?i)^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/override/inline @EMIT={obj},OVR"));

			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&WRAP {obj}=${token} *:@emit payload=%0"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} hello"));

			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo("payload=hello")
				.Because("an inline override must dispatch the matched $-command exactly as a non-inline one does");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}
}
