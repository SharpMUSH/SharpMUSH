using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
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

	/// <summary>
	/// Runs <paramref name="list"/> as #1 from the queue and waits for it and everything it queued. A command
	/// parsed on the test thread would race the queue consumer that runs what it queues.
	/// </summary>
	private async Task RunQueued(string list)
	{
		await WebAppFactoryArg.Services.GetRequiredService<IMediator>().Send(new AdmitCommandListRequest(
			MString.Plain(list),
			WebAppFactoryArg.CommandParserFor(new DBRef(1), 1).CurrentState,
			new DbRefAttribute(new DBRef(1), ["QUEUED_TEST"]),
			-1));
		await DrainQueue();
	}

	/// <summary>A non-<c>/inline</c> hook's matched body is its own queue entry; wait for it to run.</summary>
	private ValueTask DrainQueue() => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();

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
	[Arguments("SAY", "OVERRIDE", "", @"\[add(1,2)\] \\", @"[add(1,2)] \")]
	[Arguments("@EMIT", "OVERRIDE", "", @"\%# \[x\]", "%# [x]")]
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
			await DrainQueue();
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
			await DrainQueue();

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
			await DrainQueue();

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
	/// <c>@hook/override/inline</c>. The inline flag decides only WHEN the matched <c>$</c>-command runs (in
	/// place, or as its own queue entry; see <see cref="HookedMatch_IsQueuedUnlessTheHookIsInline"/>), not
	/// what argument it sees, so both spellings capture the same text.
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

	/// <summary>
	/// <c>run_cmd_hook</c> (<c>src/command.c:2454</c>) hands the hook's <c>inplace</c> flags to
	/// <c>atr_comm_match</c> as the queue type, so an OVERRIDE/EXTEND hook's matched <c>$</c>-command runs in
	/// place only when the hook was set with <c>/inline</c>. Otherwise <c>parse_que_attr</c> queues it: it
	/// runs after the rest of the action list, and with its own, empty q-registers (#1284).
	/// </summary>
	/// <remarks>
	/// PennMUSH 1.8.8 p0 (rev <c>80a1d5b</c>), with <c>@set Hk=!no_command</c>, <c>&amp;LIST Hk=think setq(0,parent);@emit x;&amp;ORDER
	/// Hk=[get(Hk/ORDER)]list</c> and <c>&amp;OVR Hk=$@emit *:&amp;ORDER Hk=[get(Hk/ORDER)]hook(%q0)</c>, then
	/// <c>@trigger Hk/LIST</c>: <c>@hook/override</c> leaves <c>listhook()</c>, <c>@hook/override/inline</c>
	/// leaves <c>hook(parent)list</c>. The EXTEND rows are the same transcript through <c>say/x y</c>:
	/// <c>listext()</c> and <c>ext(parent)list</c>.
	/// </remarks>
	[Test]
	[Arguments("@EMIT", "OVERRIDE", "override", "@emit *", "@emit x", "listhook()")]
	[Arguments("@EMIT", "OVERRIDE", "override/inline", "@emit *", "@emit x", "hook(parent)list")]
	[Arguments("SAY", "EXTEND", "extend", "say/x *", "say/x y", "listhook()")]
	[Arguments("SAY", "EXTEND", "extend/inline", "say/x *", "say/x y", "hook(parent)list")]
	public async ValueTask HookedMatch_IsQueuedUnlessTheHookIsInline(string command, string hookType,
		string hookSwitches, string pattern, string invocation, string expected)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookQueue");
		try
		{
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&OVR {obj}=${pattern}:&ORDER {obj}=[get({obj}/ORDER)]hook(%q0)"));
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&LIST {obj}=think setq(0,parent);{invocation};&ORDER {obj}=[get({obj}/ORDER)]list"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/{hookSwitches} {command}={obj},OVR"));

			await RunQueued($"@trigger {obj}/LIST");

			await Assert.That(await ReadAttributeAsync(obj, "ORDER")).IsEqualTo(expected);
		}
		finally
		{
			await HookService.ClearHookAsync(command, hookType);
		}
	}

	/// <summary>
	/// A regexp <c>$</c>-command is matched against the command line AFTER evaluation, so a <c>%r</c> in
	/// what the player typed is a REAL line break by the time the pattern runs. <c>.</c> does not cross
	/// one without the <c>s</c> flag, which makes the difference between a pattern that captures a
	/// multi-line emit and one that does not fire at all — and "does not fire" is invisible, because the
	/// built-in still runs when no <c>$</c>-command matches. That is exactly how multi-line poses went
	/// missing from the scene archive while still reaching the room.
	/// </summary>
	/// <remarks>
	/// The two patterns are held in one test so the contrast is the assertion. Both are written the way
	/// the bundled scene package writes them, which is what this is here to protect.
	/// </remarks>
	[Test]
	[Arguments("(?i)", "UNSET")]
	[Arguments("(?is)", "alpha\nbeta")]
	public async ValueTask RegexpOverride_SpansANewlineOnlyWithTheSingleLineFlag(string flags, string expected)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HookOvrMulti");
		try
		{
			await Parser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"&OVR {obj}=${flags}^@emit (.*)$:&RESULT {obj}=%1"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/OVR=regexp"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/override @EMIT={obj},OVR"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&RESULT {obj}=UNSET"));

			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@emit alpha%rbeta"));
			await DrainQueue();

			await Assert.That(await ReadAttributeAsync(obj, "RESULT")).IsEqualTo(expected)
				.Because($"'{flags}' decides whether the override sees a two-line emit at all");
		}
		finally
		{
			await HookService.ClearHookAsync("@EMIT", "OVERRIDE");
		}
	}
}
