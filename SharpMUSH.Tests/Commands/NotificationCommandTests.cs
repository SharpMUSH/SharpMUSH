namespace SharpMUSH.Tests.Commands;

public class NotificationCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private OwnRoomPlayers Players => new(WebAppFactoryArg);

	/// <summary>
	/// <c>@message</c> tells each recipient its attribute evaluated with the arguments, or the default
	/// message when it has no such attribute.
	/// </summary>
	[Test]
	[Arguments("MSGFMT", "Got: token")]
	[Arguments("NOSUCHATTR", "Default here")]
	public async ValueTask MessageCommand(string attribute, string expected)
	{
		var player = await Players.PlayerAsync("MessageCmd");
		await Players.RunAsync(player, "&MSGFMT me=Got: %0");

		var heard = await Players.HeardRunning(player, $"@message me=Default here,{attribute},token");

		await Assert.That(heard).IsEquivalentTo([expected]);
	}
}
