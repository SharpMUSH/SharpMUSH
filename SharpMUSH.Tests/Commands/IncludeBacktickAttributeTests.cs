using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Reproduces the @include case that SceneRoleplayIntegrationTests worked around by inlining the
/// factored INCLUDE`CAPTURE tail. The agent claimed "@include can't resolve a backtick attribute name".
/// Backticks are NOT parser-special — they are ordinary characters in an attribute (tree) name — so this
/// test exercises @include against a backtick-named attribute and a plain-named control to show whether
/// @include actually mishandles the backtick name (and if so, where).
///
/// Each included body sets a marker attribute on the executor (#1); we then read that marker with get().
/// If @include ran the body the marker is set; if @include silently no-ops the marker stays empty.
/// </summary>
[NotInParallel]
public class IncludeBacktickAttributeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<string> Eval(string expression)
		=> (await Parser.CommandParse(1, ConnectionService, MModule.single($"think {expression}"))).Message?.ToPlainText()?.Trim() ?? "";

	private async ValueTask Cmd(string command)
		=> await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	[Test]
	public async ValueTask Include_PlainAttribute_RunsIncludedBody()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclPlain");
		var marker = $"INCL_PLAIN_RESULT_{tag}";

		await Cmd($"&INCLUDEPLAIN_{tag} {obj}=&{marker} #1=ran_plain");

		await Assert.That(await Eval($"get({obj}/INCLUDEPLAIN_{tag})")).IsEqualTo($"&{marker} #1=ran_plain")
			.Because("the plain attribute should be readable by get()");

		await Cmd($"@include {obj}/INCLUDEPLAIN_{tag}");

		await Assert.That(await Eval($"get(#1/{marker})")).IsEqualTo("ran_plain")
			.Because("@include of a plain-named attribute runs its body (control)");
	}

	[Test]
	public async ValueTask Include_BacktickAttribute_RunsIncludedBody()
	{
		var tag = Guid.NewGuid().ToString("N")[..8];
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclBacktick");
		var marker = $"INCL_BT_RESULT_{tag}";

		await Cmd($"&INCLUDE`CAPTURE {obj}=&{marker} #1=ran_backtick");

		await Assert.That(await Eval($"get({obj}/INCLUDE`CAPTURE)")).IsEqualTo($"&{marker} #1=ran_backtick")
			.Because("the backtick attribute should be readable by get() (backticks are valid attr-name chars)");

		await Cmd($"@include {obj}/INCLUDE`CAPTURE");

		await Assert.That(await Eval($"get(#1/{marker})")).IsEqualTo("ran_backtick")
			.Because("@include of a backtick-named attribute must run its body, exactly as the plain-named control does");
	}
}
