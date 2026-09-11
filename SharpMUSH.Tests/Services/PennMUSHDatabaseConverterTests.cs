using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Everything that imports a populated database does so into an <see cref="IsolatedImportWorld"/>:
/// the importer reuses #0, #1 and #2, renaming and restamping them, and the shared session world's
/// copies of those are what every other suite runs in.
/// </summary>
public class PennMUSHDatabaseConverterTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPennMUSHDatabaseConverter GetConverter()
	{
		return WebAppFactoryArg.Services.GetRequiredService<IPennMUSHDatabaseConverter>();
	}

	[Test]
	public async ValueTask ConverterServiceIsRegistered()
	{
		var converter = GetConverter();
		await Assert.That(converter).IsNotNull();
	}

	[Test]
	public async ValueTask ConverterCanConvertEmptyDatabase()
	{
		var converter = GetConverter();
		var database = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects = []
		};

		var result = await converter.ConvertDatabaseAsync(database);

		await Assert.That(result).IsNotNull();
		await Assert.That(result.TotalObjects).IsEqualTo(0);
		await Assert.That(result.IsSuccessful).IsTrue();
	}

	[Test]
	public async ValueTask ConversionResultIncludesStatistics()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 1, Name = "Player One", Type = PennMUSHObjectType.Player },
				new PennMUSHObject { DBRef = 2, Name = "Thing Two", Type = PennMUSHObjectType.Thing },
				new PennMUSHObject { DBRef = 3, Name = "Exit Three", Type = PennMUSHObjectType.Exit }
			]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.TotalObjects).IsEqualTo(4);
		await Assert.That(result.RoomsConverted).IsEqualTo(1);
		await Assert.That(result.PlayersConverted).IsEqualTo(1);
		await Assert.That(result.ThingsConverted).IsEqualTo(1);
		await Assert.That(result.ExitsConverted).IsEqualTo(1);
		await Assert.That(result.Duration).IsGreaterThan(TimeSpan.Zero);
	}

	[Test]
	public async ValueTask ConverterUpdatesGodPlayerNameAndPassword()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Custom Limbo", Type = PennMUSHObjectType.Room },
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "CustomGod",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$customhash123"
				}
			]
		});

		await Assert.That(result.IsSuccessful).IsTrue();

		var godPlayer = await world.Database.GetObjectNodeAsync(new DBRef(1));
		await Assert.That(godPlayer.IsPlayer).IsTrue();
		await Assert.That(godPlayer.Expect<SharpPlayer>().Object.Name).IsEqualTo("CustomGod");
		await Assert.That(await CountNamedAsync(world, "CustomGod")).IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH's <c>create_minimal_db</c> makes #0 "Room Zero", a room, and so does the migration seed,
	/// so the source's #0 becomes the seeded one rather than a second Limbo beside it.
	/// </summary>
	[Test]
	public async ValueTask ConverterUpdatesRoom0Name()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		const long createdSeconds = 1_100_000_000L;

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Custom Void",
					Type = PennMUSHObjectType.Room,
					CreationTime = createdSeconds,
					ModificationTime = createdSeconds
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash",
					Location = 0
				}
			]
		});

		await Assert.That(result.Errors).IsEmpty();

		var room0 = await world.Database.GetObjectNodeAsync(new DBRef(0));
		await Assert.That(room0.IsRoom).IsTrue();
		await Assert.That(room0.Expect<SharpRoom>().Object.Name).IsEqualTo("Custom Void");
		await Assert.That(room0.Expect<SharpRoom>().Object.CreationTime).IsEqualTo(createdSeconds * 1000);
		await Assert.That(await CountNamedAsync(world, "Custom Void")).IsEqualTo(1)
			.Because("the import created a second Limbo instead of reusing #0");
	}

	/// <summary>
	/// #2 is PennMUSH's Master Room — a room in <c>create_minimal_db</c>, and one <c>MASTER_ROOM</c> is
	/// required to be — and the migration seeds the same. A source #2 that is a room takes it over.
	/// </summary>
	[Test]
	public async ValueTask ConverterReusesTheMasterRoomForObjectTwo()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		const long createdSeconds = 1_200_000_000L;

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Location = 0 },
				new PennMUSHObject
				{
					DBRef = 2,
					Name = "Global Commands",
					Type = PennMUSHObjectType.Room,
					CreationTime = createdSeconds,
					ModificationTime = createdSeconds
				}
			]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.RoomsConverted).IsEqualTo(2);

		var room2 = await world.Database.GetObjectNodeAsync(new DBRef(2));
		await Assert.That(room2.IsRoom).IsTrue();
		await Assert.That(room2.Expect<SharpRoom>().Object.Name).IsEqualTo("Global Commands");
		await Assert.That(room2.Expect<SharpRoom>().Object.CreationTime).IsEqualTo(createdSeconds * 1000);
		await Assert.That(await CountNamedAsync(world, "Global Commands")).IsEqualTo(1)
			.Because("the import created a new object for #2 instead of reusing the Master Room");
	}

	/// <summary>
	/// The seeded #2 is a room and cannot become anything else, so a source #2 of another type is
	/// created like every other object and the Master Room is left as it was.
	/// </summary>
	[Test]
	public async ValueTask ASourceObjectTwoThatIsNotARoomDoesNotTakeOverTheMasterRoom()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Location = 0 },
				new PennMUSHObject { DBRef = 2, Name = "Widget Two", Type = PennMUSHObjectType.Thing, Location = 0 }
			]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ThingsConverted).IsEqualTo(1);

		var room2 = await world.Database.GetObjectNodeAsync(new DBRef(2));
		await Assert.That(room2.IsRoom).IsTrue();
		await Assert.That(room2.Expect<SharpRoom>().Object.Name).IsEqualTo("Master Room");

		var widget = await FindNamedAsync(world, "Widget Two");
		await Assert.That(widget.Key).IsNotEqualTo(2);
	}

	/// <summary>
	/// The seeded objects are the ones a live game has already read, so their new names and objids have
	/// to reach the engine's object cache and not only the store beneath it.
	/// </summary>
	[Test]
	public async ValueTask ReusedSeededObjectsAreFreshThroughTheEngineCache()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		foreach (var number in (int[])[0, 1, 2])
		{
			await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(number)));
		}

		const long createdSeconds = 1_300_000_000L;
		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0, Name = "Cached Void", Type = PennMUSHObjectType.Room,
					CreationTime = createdSeconds, ModificationTime = createdSeconds
				},
				new PennMUSHObject
				{
					DBRef = 1, Name = "CachedGod", Type = PennMUSHObjectType.Player,
					CreationTime = createdSeconds, ModificationTime = createdSeconds
				},
				new PennMUSHObject
				{
					DBRef = 2, Name = "Cached Master", Type = PennMUSHObjectType.Room,
					CreationTime = createdSeconds, ModificationTime = createdSeconds
				}
			]
		});

		await Assert.That(result.Errors).IsEmpty();

		string[] names = ["Cached Void", "CachedGod", "Cached Master"];
		foreach (var (number, name) in names.Index())
		{
			var cached = await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(number, createdSeconds * 1000)));
			await Assert.That(cached.IsNone).IsFalse()
				.Because($"#{number}'s new objid did not resolve through the engine");
			await Assert.That(cached.Expect<AnySharpObject>().Object().Name).IsEqualTo(name);
		}
	}

	/// <summary>
	/// The seeded #0 is a room, so only a source room can take it over; anything else at #0 is created
	/// like every other object and the seeded room stays as the import's Limbo.
	/// </summary>
	[Test]
	public async ValueTask ASourceObjectZeroThatIsNotARoomDoesNotTakeOverRoomZero()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Widget Zero", Type = PennMUSHObjectType.Thing },
				new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player }
			]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ThingsConverted).IsEqualTo(1);
		await Assert.That(result.RoomsConverted).IsEqualTo(0);

		var room0 = await world.Database.GetObjectNodeAsync(new DBRef(0));
		await Assert.That(room0.IsRoom).IsTrue();
		await Assert.That(room0.Expect<SharpRoom>().Object.Name).IsEqualTo("Room Zero");
		await Assert.That((await FindNamedAsync(world, "Widget Zero")).Key).IsNotEqualTo(0);
	}

	/// <summary>
	/// PennMUSH hardcodes God as #1, so a source #1 that is not a player is a damaged database; it is
	/// still imported as what it is rather than folded into God along with its attributes and locks.
	/// </summary>
	[Test]
	public async ValueTask ASourceObjectOneThatIsNotAPlayerDoesNotTakeOverGod()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 1, Name = "Widget One", Type = PennMUSHObjectType.Thing, Location = 0 }
			]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ThingsConverted).IsEqualTo(1);
		await Assert.That(result.PlayersConverted).IsEqualTo(0);

		var god = await world.Database.GetObjectNodeAsync(new DBRef(1));
		await Assert.That(god.IsPlayer).IsTrue();
		await Assert.That(god.Expect<SharpPlayer>().Object.Name).IsEqualTo("God");
		await Assert.That((await FindNamedAsync(world, "Widget One")).Key).IsNotEqualTo(1);
	}

	/// <summary>
	/// The totals count source objects. A source without #0, #1 or #2 converts none of them, even
	/// though the importer still maps onto the seeded ones.
	/// </summary>
	[Test]
	public async ValueTask SeededObjectsCountOnlyWhenTheSourceHasThem()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects = [new PennMUSHObject { DBRef = 10, Name = "Lone Widget", Type = PennMUSHObjectType.Thing }]
		});

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.TotalObjects).IsEqualTo(1);
		await Assert.That(result.ThingsConverted).IsEqualTo(1);
	}

	/// <summary>
	/// PennMUSH's NOTHING is -1, and #0 is a real room. A reference the source never set is NOTHING;
	/// defaulting it to 0 parents and zones every such object to Room Zero.
	/// </summary>
	[Test]
	public async ValueTask AnUnsetReferenceIsNothingRatherThanRoomZero()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects = [new PennMUSHObject { DBRef = 11, Name = "Unreferenced Widget", Type = PennMUSHObjectType.Thing }]
		});

		await Assert.That(result.Errors).IsEmpty();

		var widget = await FindNamedAsync(world, "Unreferenced Widget");
		await Assert.That((await widget.Parent.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
		await Assert.That((await widget.Zone.WithCancellation(CancellationToken.None)).IsNone).IsTrue();
	}

	[Test]
	public async ValueTask ConverterSetsParentRelationships()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Limbo", Type = PennMUSHObjectType.Room },
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash",
					Location = 0
				},
				new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 3, Name = "Child Room", Type = PennMUSHObjectType.Room, Parent = 2 }
			]
		});

		await Assert.That(result.Errors).IsEmpty();

		var child = await FindNamedAsync(world, "Child Room");
		var parent = await child.Parent.WithCancellation(CancellationToken.None);
		await Assert.That(parent.Expect<AnySharpObject>().Object().Key).IsEqualTo(2);
	}

	[Test]
	public async ValueTask ConverterSetsZoneRelationships()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Limbo", Type = PennMUSHObjectType.Room },
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash",
					Location = 0
				},
				new PennMUSHObject { DBRef = 2, Name = "Zone Master", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 3, Name = "Zoned Room", Type = PennMUSHObjectType.Room, Zone = 2 }
			]
		});

		await Assert.That(result.Errors).IsEmpty();

		var zoned = await FindNamedAsync(world, "Zoned Room");
		var zone = await zoned.Zone.WithCancellation(CancellationToken.None);
		await Assert.That(zone.Expect<AnySharpObject>().Object().Key).IsEqualTo(2);
	}

	private static async Task<int> CountNamedAsync(IsolatedImportWorld world, string name)
		=> await world.Database.GetAllObjectsAsync().CountAsync(obj => obj.Name == name);

	private static async Task<SharpObject> FindNamedAsync(IsolatedImportWorld world, string name)
		=> await world.Database.GetAllObjectsAsync().SingleAsync(obj => obj.Name == name);
}
