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
	[Arguments("think %d", 4242L, "4242")]
	[Arguments("think %D", 4243L, "4243")]
	public async Task ResolvesSourcePortWhenHandlePresent(string code, long handle, string expected)
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var parser = WebAppFactoryArg.CommandParserFor(executor, handle);

		await parser.CommandParse(MModule.single(code));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage(expected), TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task ResolvesEmptyWhenNoHandle()
	{
		var result = await Parser.FunctionParse(MModule.single("%d"));

		await Assert.That(result).IsNotNull();
		await Assert.That(MModule.plainText(result!.Message!)).IsEqualTo(string.Empty);
	}
}
