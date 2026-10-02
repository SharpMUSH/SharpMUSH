using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A PennMUSH player's aliases are its ALIAS attribute, which the import writes like any other attribute
/// (#1499). PennMUSH builds its player list from that attribute when it loads (<c>src/game.c:682-686</c>),
/// so an imported player answers to its aliases from the start.
/// </summary>
public class PennMUSHImportPlayerAliasTests
{
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Player Alias Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject
			{
				DBRef = 3, Name = "Alice", Type = PennMUSHObjectType.Player, Owner = 3, Location = 0, Link = 0,
				Attributes = [new PennMUSHAttribute { Name = "ALIAS", Owner = 3, Value = "Wonder;Dreamer", Flags = [] }]
			}
		]
	};

	[Test]
	public async Task AnImportedPlayerAnswersToItsAliases()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Fixture());
		await Assert.That(result.Errors).IsEmpty();

		var byAlias = await world.Mediator.CreateStream(new GetPlayerQuery("dreamer")).ToArrayAsync();

		await Assert.That(byAlias.Select(player => player.Object.Name)).IsEquivalentTo(["Alice"]);
		await Assert.That(byAlias[0].Aliases ?? []).IsEquivalentTo(["Wonder", "Dreamer"]);
	}
}
