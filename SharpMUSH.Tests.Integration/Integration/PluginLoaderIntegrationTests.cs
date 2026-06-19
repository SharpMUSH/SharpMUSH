using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Integration;

/// <summary>
/// End-to-end proof of the Phase 1 C# plugin loader. The SamplePlugin fixture DLL (+ plugin.json) is
/// copied into the test host's <c>plugins/sample/</c> directory at build time; the booting server's
/// <c>PluginBootstrapService</c> discovers, loads, and registers its <c>[SharpCommand]</c>/<c>[SharpFunction]</c>
/// with IsSystem=true. These tests confirm the plugin command dispatches (including via abbreviation,
/// which only works because the entry lands in the IsSystem command trie) and the function evaluates.
/// </summary>
[NotInParallel]
public class PluginLoaderIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactory.FunctionParser;
	private IConnectionService Connection => WebAppFactory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	private async Task<string?> Cmd(string command) =>
		(await CommandParser.CommandParse(1, Connection, MModule.single(command))).Message?.ToPlainText();

	[Test]
	public async Task PluginFunction_PluginAdd_EvaluatesToSum()
	{
		var result = await Eval("pluginadd(2,3)");
		await Assert.That(result).IsEqualTo("5");
	}

	[Test]
	public async Task PluginCommand_Ping_Dispatches()
	{
		var result = await Cmd("+ping");
		await Assert.That(result).IsEqualTo("Pong from the sample plugin!");
	}

	[Test]
	public async Task PluginCommand_Ping_DispatchesViaAbbreviation()
	{
		// Abbreviated command resolution only succeeds because the plugin entry was registered with
		// IsSystem=true and thus added to the prefix command trie alongside the built-ins.
		var result = await Cmd("+pi");
		await Assert.That(result).IsEqualTo("Pong from the sample plugin!");
	}
}
