using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class EmitMissingArgumentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("@lemit")]
	[Arguments("@nslemit")]
	[Arguments("@remit #0")]
	[Arguments("@nsremit #0")]
	[Arguments("@oemit me")]
	[Arguments("@nsoemit me")]
	[Arguments("@zemit")]
	[Arguments("@nszemit")]
	[Arguments("@nspemit me")]
	public async Task MissingMessageRetainsDiagnosticAndReturnValue(string command)
	{
		var connection = Factory.Services.GetRequiredService<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connection, "MissingEmit");
		var result = await Factory.CommandParser.CommandParse(player.Handle, connection, MarkupText.Plain(command));
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NothingToDo);
		await Assert.That(Factory.Notifications.For(player.DbRef)).Contains(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail);
	}

	[Test]
	public async Task NoSpoofPromptRetainsArgumentCountDiagnostic()
	{
		var connection = Factory.Services.GetRequiredService<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connection, "MissingPrompt");
		var result = await Factory.CommandParser.CommandParse(player.Handle, connection, MarkupText.Plain("@nsprompt me"));
		var expected = string.Format(ErrorMessages.Returns.TooFewCommandArguments, "@NSPROMPT", 2, 1);
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(expected);
		await Assert.That(Factory.Notifications.For(player.DbRef)).Contains(expected);
	}

	[Test]
	[Arguments("@pemit me")]
	[Arguments("@prompt me")]
	[Arguments("@pemit me=")]
	[Arguments("@nspemit me=")]
	public async Task PrivateNoOpRemainsSilent(string command)
	{
		var connection = Factory.Services.GetRequiredService<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connection, "EmptyEmit");
		var before = Factory.Notifications.CountFor(player.DbRef);
		var result = await Factory.CommandParser.CommandParse(player.Handle, connection, MarkupText.Plain(command));
		await Assert.That(result.Message!.ToPlainText()).IsEmpty();
		await Assert.That(Factory.Notifications.For(player.DbRef).Skip(before)).IsEmpty();
	}
}
