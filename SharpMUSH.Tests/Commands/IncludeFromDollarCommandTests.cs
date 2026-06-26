using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Pins down what SceneRoleplayIntegrationTests actually hit. It inlined the factored INCLUDE`CAPTURE
/// tail and rewrote multi-line verbs to single ';'-separated lines, blaming @include / backticks / %! /
/// q-registers / "multi-line bodies". These tests show none of those is an engine defect:
///
///   * %! resolves to the object inside its $-command.
///   * @include %!/&lt;backtick-attr&gt; from a $-command runs the included body.
///   * a q-register set before a ';'-separated @include is readable inside the included body.
///   * a ';'-separated multi-command body runs every command.
///
/// The scene's real problem was authoring: commands in a command list are separated by ';' — a raw
/// newline is NOT a separator, so a newline-"separated" body has its first verb swallow the rest. The
/// agent's rewrite to ';'-joined lines was the correct fix for malformed softcode, not a bug workaround.
///
/// A $-command body runs with the OBJECT as executor and %# as the enactor (#1, co-located in #0). Each
/// body @pemit's a unique marker to %#; we collect notifications and assert on them.
/// </summary>
[NotInParallel]
public class IncludeFromDollarCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static string? ExtractMessage(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify)) return null;
		var args = call.GetArguments();
		if (args.Length < 2) return null;
		return args[1] switch
		{
			OneOf<MString, string> oneOf => oneOf.Match(m => m.ToString(), s => s),
			string s => s,
			MString m => m.ToString(),
			_ => null
		};
	}

	private async ValueTask Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	/// <summary>Trigger <paramref name="command"/> and return all notification texts it produced.</summary>
	private async ValueTask<List<string>> TriggerAndCollect(string command)
	{
		var pre = NotifyService.ReceivedCalls().Count();
		await Cmd(command);
		return NotifyService.ReceivedCalls().Skip(pre).Select(ExtractMessage).Where(m => m is not null).ToList()!;
	}

	[Test]
	public async ValueTask PercentBang_ResolvesToTheObject_InADollarCommand()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "PbDollar");
		var token = $"pbcmd{tag}";

		await Cmd($"&DO_PB_{tag} {obj}=${token}:@pemit %#=PB_{tag}:%!");
		var msgs = await TriggerAndCollect(token);

		await Assert.That(msgs.Any(m => m.StartsWith($"PB_{tag}:#"))).IsTrue()
			.Because($"%! must resolve to the object's dbref inside its $-command; got: [{string.Join(" | ", msgs)}]");
	}

	[Test]
	public async ValueTask Include_BacktickAttribute_ViaPercentBang_FromDollarCommand()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IncPbBt");
		var token = $"inccmd{tag}";

		await Cmd($"&INC_{tag}`CAPTURE {obj}=@pemit %#=BTCAP_{tag}_done");
		await Cmd($"&DO_INC_{tag} {obj}=${token}:@include %!/INC_{tag}`CAPTURE");

		var msgs = await TriggerAndCollect(token);

		await Assert.That(msgs.Any(m => m.Trim() == $"BTCAP_{tag}_done")).IsTrue()
			.Because($"@include %!/INC_{tag}`CAPTURE from a $-command must run the included body; got: [{string.Join(" | ", msgs)}]");
	}

	[Test]
	public async ValueTask QRegister_SurvivesAcrossSemicolonBoundary_IntoInclude()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IncQReg");
		var token = $"qcmd{tag}";

		await Cmd($"&READ_Q_{tag} {obj}=@pemit %#=QVAL_{tag}:%q0");
		await Cmd($"&DO_Q_{tag} {obj}=${token}:think [setq(0,qreg_{tag})];@include %!/READ_Q_{tag}");

		var msgs = await TriggerAndCollect(token);

		await Assert.That(msgs.Any(m => m.Trim() == $"QVAL_{tag}:qreg_{tag}")).IsTrue()
			.Because($"a q-register set before a ';'-separated @include must be readable in the included body; got: [{string.Join(" | ", msgs)}]");
	}

	[Test]
	public async ValueTask SemicolonSeparatedBody_RunsEveryCommand()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IncSemi");
		var token = $"semicmd{tag}";

		// The CORRECT way to write a multi-command body: ';' separators (a raw newline would not separate).
		await Cmd($"&DO_SEMI_{tag} {obj}=${token}:@pemit %#=FIRST_{tag};@pemit %#=SECOND_{tag}");
		var msgs = await TriggerAndCollect(token);

		await Assert.That(msgs.Any(m => m.Trim() == $"FIRST_{tag}")).IsTrue()
			.Because($"the first ';'-separated command must run; got: [{string.Join(" | ", msgs)}]");
		await Assert.That(msgs.Any(m => m.Trim() == $"SECOND_{tag}")).IsTrue()
			.Because($"the second ';'-separated command must run as its own command; got: [{string.Join(" | ", msgs)}]");
	}
}
