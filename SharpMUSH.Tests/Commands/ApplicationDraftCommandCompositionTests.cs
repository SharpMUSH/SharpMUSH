using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ApplicationDraftCommandCompositionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task DraftCommandUsesItsCompositionRatherThanTheParsersProvider()
	{
		var commands = (SharpMUSH.Implementation.Commands.Commands)Factory.Services.GetRequiredService<ILibraryProvider<CommandDefinition>>();
		var provider = Substitute.For<IServiceProvider>();
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(provider);
		parser.CurrentState.Returns(ParserState.RootFor(new DBRef(1, 1)) with
		{
			Arguments = new() { ["0"] = new("FORM"), ["1"] = new("new"), ["2"] = new("{}") }
		});
		var result = (await commands.ApplicationDraft(parser, new SharpCommandAttribute { Name = "@APPDRAFT" })).Expect<CallState>();
		await Assert.That(result.Message.ToPlainText()).Contains("Drafts require a bound connection or authenticated form instance.");
		provider.DidNotReceive().GetService(Arg.Any<Type>());
	}
}
