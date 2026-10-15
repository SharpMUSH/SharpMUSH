using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Substitutions;

public class SourcePortSubstitutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	[Test]
	[Arguments("think %j", 4242L, "4242")]
	[Arguments("think %J", 4243L, "4243")]
	public async Task ResolvesSourcePortWhenHandlePresent(string code, long handle, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var parser = WebAppFactoryArg.CommandParserFor(executor, handle);

		await parser.CommandParse(MarkupText.Plain(code));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(expected), TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task ResolvesEmptyWhenNoHandle()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("%j"));

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo(string.Empty);
	}

	/// <summary>A bare <c>%d</c> is no substitution, as in PennMUSH: the letter.</summary>
	[Test]
	public async Task BareDIsTheLetter()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("%d"));

		await Assert.That(result!.Message.ToPlainText()).IsEqualTo("d");
	}
}
