using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The HALT object flag suppresses a halted object's softcode, matching PennMUSH: process_expression
/// returns PE_NOTHING for a Halted executor (src/parse.c), so any attribute evaluated as that object
/// yields its stored text unevaluated. Verified against a live PennMUSH server — <c>u()</c> of a
/// halted object's attribute returns the raw code, e.g. <c>[add(1,2)]</c>, and clears again when the
/// flag is removed. The flag was already seeded and settable (by <c>@halt</c> and by <c>@chown</c>,
/// which sets it to break ownership loops) but nothing enforced it.
/// </summary>
[NotInParallel]
public class HaltFlagTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private Task Cmd(string command) =>
		CommandParser.CommandParse(1, ConnectionService, MModule.single(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	[Test]
	public async ValueTask HaltedObjectReturnsCodeUnevaluated()
	{
		var suffix = Guid.NewGuid().ToString("N")[..8];
		var dbref = await Eval($"create(HaltObj{suffix})");
		var attr = $"CODE{suffix}";
		await Cmd($"&{attr} {dbref}=[add(1,2)]");

		// Not halted: the attribute evaluates.
		await Assert.That(await Eval($"u({dbref}/{attr})")).IsEqualTo("3");

		// Halted: PE_NOTHING — the stored code comes back verbatim.
		await Cmd($"@set {dbref}=HALT");
		await Assert.That(await Eval($"u({dbref}/{attr})")).IsEqualTo("[add(1,2)]");

		// Unhalted: it evaluates again.
		await Cmd($"@set {dbref}=!HALT");
		await Assert.That(await Eval($"u({dbref}/{attr})")).IsEqualTo("3");
	}
}
