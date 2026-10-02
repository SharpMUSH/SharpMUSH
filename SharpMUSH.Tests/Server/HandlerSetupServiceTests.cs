using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="HandlerSetupService.ClashesAsync"/>: building the bundled packages onto an object the game already
/// has keeps the object's own value wherever it has one, so that part of a package would not run as shipped.
/// The wizard shows exactly those attributes before anything changes.
/// </summary>
public class HandlerSetupServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	/// <summary>A room of its own, with the attributes given, standing in for a game's own handler.</summary>
	private async Task<int> ObjectWithAsync(params (string Name, string Value)[] attributes)
	{
		var god = (await Get<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var room = await Get<IMediator>().Send(new CreateRoomCommand("handler-test-" + Guid.NewGuid().ToString("N"), god));
		foreach (var (name, value) in attributes)
		{
			await Get<IMediator>().Send(new SetAttributeCommand(room, name.Split('`'), MarkupText.Plain(value), god));
		}

		return room.Number;
	}

	[Test]
	public async Task AnAttributeTheObjectAlreadyHas_IsAClash()
	{
		var number = await ObjectWithAsync(("GET", "@pemit %#=my own router"), ("FN`MINE", "unrelated"));

		var clashes = (await Get<HandlerSetupService>().ClashesAsync(HandlerKinds.Http, number, CancellationToken.None))
			.Expect<IReadOnlyList<HandlerClash>>();

		await Assert.That(clashes).Contains(new HandlerClash("http-handler", "GET"));
		await Assert.That(clashes.Any(c => c.Attribute == "FN`MINE")).IsFalse()
			.Because("an attribute no package writes is the game's alone");
	}

	[Test]
	public async Task AnObjectWithNoneOfThePackagesAttributes_HasNoClashes()
	{
		var number = await ObjectWithAsync(("DESCRIBE", "A plain room."));

		var clashes = (await Get<HandlerSetupService>().ClashesAsync(HandlerKinds.Event, number, CancellationToken.None))
			.Expect<IReadOnlyList<HandlerClash>>();

		await Assert.That(clashes).IsEmpty();
	}

	[Test]
	public async Task AnObjectThatDoesNotExist_IsReported()
	{
		var result = await Get<HandlerSetupService>().ClashesAsync(HandlerKinds.Http, int.MaxValue - 1, CancellationToken.None);

		await Assert.That(result is Error<string>).IsTrue();
	}
}
