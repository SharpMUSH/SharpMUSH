using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Each engine resolves the function and command aliases its own configuration names. The alias
/// tables were process-wide statics every host overwrote from its options as it started, so an engine
/// built after another host had started took that host's aliases rather than its own — the same shape
/// <c>float_precision</c> had (#1245). Starting a second engine here is what an import world, the
/// readiness tests or the telnet tests do alongside the shared host.
/// </summary>
public class EngineAliasesTests
{
	private const string FunctionAlias = "fdivbyanothername";
	private const string CommandAlias = "thinkbyanothername";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task AnotherEngineStartingDoesNotChangeWhichAliasesAnEngineResolves()
	{
		var configured = ReadPennMushConfig.Create(Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var extraAliases = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		extraAliases.CurrentValue.Returns(configured with
		{
			Alias = configured.Alias with
			{
				FunctionAliases = new(configured.Alias.FunctionAliases) { ["fdiv"] = [FunctionAlias] },
				CommandAliases = new(configured.Alias.CommandAliases) { ["THINK"] = [CommandAlias] }
			}
		});

		await using var aliased = await IsolatedImportWorld.CreateAsync(services =>
		{
			services.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
			services.AddSingleton(extraAliases);
		});
		using (var startup = ActivatorUtilities.CreateInstance<StartupHandler>(aliased.Services))
			await startup.StartAsync(CancellationToken.None);

		// Built after the aliased engine started, with the shipped configuration.
		await using var plain = await IsolatedImportWorld.CreateAsync();

		await Assert.That(await Evaluate(aliased, extraAliases)).IsEqualTo("0.5");
		await Assert.That(Commands(aliased).ContainsKey(CommandAlias)).IsTrue();

		var plainOptions = plain.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		await Assert.That(await Evaluate(plain, plainOptions)).IsEqualTo($"#-1 FUNCTION ({FunctionAlias.ToUpperInvariant()}) NOT FOUND");
		await Assert.That(Commands(plain).ContainsKey(CommandAlias)).IsFalse();

		var shared = WebAppFactoryArg.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));
		await Assert.That((await shared.FunctionParse(MarkupText.Plain($"[{FunctionAlias}(1,2)]")))!.Message!.ToPlainText())
			.IsEqualTo($"#-1 FUNCTION ({FunctionAlias.ToUpperInvariant()}) NOT FOUND");
		await Assert.That(WebAppFactoryArg.Services.GetRequiredService<LibraryService<string, CommandDefinition>>().ContainsKey(CommandAlias))
			.IsFalse();
	}

	private static LibraryService<string, CommandDefinition> Commands(IsolatedImportWorld world)
		=> world.Services.GetRequiredService<LibraryService<string, CommandDefinition>>();

	private static async Task<string> Evaluate(IsolatedImportWorld world, IOptionsWrapper<SharpMUSHOptions> options)
	{
		var parser = new MUSHCodeParser(
			world.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			world.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			world.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			options,
			world.Services,
			ParserState.RootFor(new DBRef(1)));
		return (await parser.FunctionParse(MarkupText.Plain($"[{FunctionAlias}(1,2)]")))!.Message!.ToPlainText();
	}
}
