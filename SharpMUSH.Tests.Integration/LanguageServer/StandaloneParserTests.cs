using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using MarkupString;

namespace SharpMUSH.Tests.Integration.LanguageServer;

public class StandaloneParserTests
{
	private sealed class DefaultOptions : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = SharpMUSHOptions.Default();
	}

	[Test]
	public async Task SyntaxAnalysis_WithoutGameServices_AcceptsValidAndRejectsBrokenExpressions()
	{
		using var services = new ServiceCollection().BuildServiceProvider();
		var parser = new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance,
			new LibraryService<string, FunctionDefinition>(), new LibraryService<string, CommandDefinition>(),
			new DefaultOptions(), services);

		await Assert.That(parser.GetDiagnostics(MarkupText.Plain("add(1,2)"))).IsEmpty();
		await Assert.That(parser.GetDiagnostics(MarkupText.Plain("add(1,2"))).IsNotEmpty();
		await Assert.That(parser.GetSemanticTokensData(MarkupText.Plain("add(1,2)")).Data).IsNotEmpty();
	}
}
