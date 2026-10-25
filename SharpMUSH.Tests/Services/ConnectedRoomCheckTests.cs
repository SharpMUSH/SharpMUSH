using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH's check_connected_rooms (<c>src/destroy.c:1219</c>): a room no exit path reaches from the base room or a
/// FLOATING room is disconnected, unless it is NO_WARN or, with exits_connect_rooms, has exits of its own.
/// </summary>
public class ConnectedRoomCheckTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private SharpMUSHOptions Options => Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;

	private async Task<int> Dig(string name) =>
		DBRef.Parse((await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {name}")))
			.Message!.ToPlainText().Trim()).Number;

	private async Task Admin(string command) =>
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));

	private async Task<HashSet<int>> Disconnected(bool exitsConnectRooms) =>
		await ConnectedRoomCheck.DisconnectedRoomsAsync(Mediator, new DBRef((int)Options.Database.BaseRoom), exitsConnectRooms)
			.Select(room => room.Object.DBRef.Number).ToHashSetAsync();

	[Test]
	public async Task FloatingRoomsAndWhatTheyLeadToAreConnected()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Island");
		var lonely = await Dig($"{name}_Lonely");
		var floating = await Dig($"{name}_Floating");
		var beyond = await Dig($"{name}_Beyond");
		var warned = await Dig($"{name}_NoWarn");
		var exited = await Dig($"{name}_Exited");
		await Admin($"@set #{floating}=FLOATING");
		await Admin($"@set #{warned}=NO_WARN");
		await Admin($"@open {name}_Bridge=#{beyond},,#{floating}");
		await Admin($"@open {name}_Nowhere=#{lonely},,#{exited}");

		var disconnected = await Disconnected(exitsConnectRooms: false);
		var withExits = await Disconnected(exitsConnectRooms: true);

		await Assert.That(disconnected).Contains(lonely);
		await Assert.That(disconnected).Contains(exited);
		await Assert.That(disconnected).DoesNotContain(floating);
		await Assert.That(disconnected).DoesNotContain(beyond);
		await Assert.That(disconnected).DoesNotContain(warned);
		await Assert.That(withExits).DoesNotContain(exited);
		await Assert.That(withExits).Contains(lonely);
	}
}
