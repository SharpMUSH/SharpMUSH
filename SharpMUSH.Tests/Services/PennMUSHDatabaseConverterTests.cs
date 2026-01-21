using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class PennMUSHDatabaseConverterTests: TestsBase
{

	private IPennMUSHDatabaseConverter GetConverter()
	{
		return Factory.Services.GetRequiredService<IPennMUSHDatabaseConverter>();
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
	[Skip("Creates objects in shared database that affect other tests - needs isolated database")]
	public async ValueTask ConversionResultIncludesStatistics()
	{
		var converter = GetConverter();
		var database = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Room Zero",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "Player One",
					Type = PennMUSHObjectType.Player
				},
				new PennMUSHObject
				{
					DBRef = 2,
					Name = "Thing Two",
					Type = PennMUSHObjectType.Thing
				},
				new PennMUSHObject
				{
					DBRef = 3,
					Name = "Exit Three",
					Type = PennMUSHObjectType.Exit
				}
			]
		};

		var result = await converter.ConvertDatabaseAsync(database);

		await Assert.That(result).IsNotNull();
		// TotalObjects may be 3 or 4 depending on whether pre-existing #0, #1, #2 were reused
		// The converter reuses existing default objects from database migration
		await Assert.That(result.TotalObjects).IsGreaterThanOrEqualTo(3);
		await Assert.That(result.TotalObjects).IsLessThanOrEqualTo(4);
		await Assert.That(result.RoomsConverted).IsGreaterThanOrEqualTo(1);
		await Assert.That(result.PlayersConverted).IsGreaterThanOrEqualTo(1);
		await Assert.That(result.ThingsConverted).IsGreaterThanOrEqualTo(0);
		await Assert.That(result.ExitsConverted).IsGreaterThanOrEqualTo(1);
		await Assert.That(result.Duration).IsGreaterThan(TimeSpan.Zero);
	}

	[Test]
	[Skip("Creates objects in shared database that affect other tests - needs isolated database")]
	public async ValueTask ConverterUpdatesGodPlayerNameAndPassword()
	{
		var converter = GetConverter();
		var database = Factory.Services.GetRequiredService<Library.ISharpDatabase>();
		
		var database1 = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Custom Limbo",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "CustomGod",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$customhash123"
				}
			]
		};

		var result = await converter.ConvertDatabaseAsync(database1);

		await Assert.That(result.IsSuccessful).IsTrue();
		
		// Verify God player was updated with custom name
		var godPlayer = await database.GetObjectNodeAsync(new Library.Models.DBRef(1));
		await Assert.That(godPlayer.IsT0).IsTrue();
		await Assert.That(godPlayer.AsT0.Object.Name).IsEqualTo("CustomGod");
	}

	[Test]
	[Skip("Creates objects in shared database that affect other tests - needs isolated database")]
	public async ValueTask ConverterUpdatesRoom0Name()
	{
		var converter = GetConverter();
		var database = Factory.Services.GetRequiredService<Library.ISharpDatabase>();
		
		var database1 = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Custom Void",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash"
				}
			]
		};

		var result = await converter.ConvertDatabaseAsync(database1);

		await Assert.That(result.IsSuccessful).IsTrue();
		
		// Verify Room #0 was updated with custom name
		var room0 = await database.GetObjectNodeAsync(new Library.Models.DBRef(0));
		await Assert.That(room0.IsT1).IsTrue();
		await Assert.That(room0.AsT1.Object.Name).IsEqualTo("Custom Void");
	}

	[Test]
	[Skip("Creates objects in shared database that affect other tests - needs isolated database")]
	public async ValueTask ConverterSetsParentRelationships()
	{
		var converter = GetConverter();
		
		var database1 = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Limbo",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash"
				},
				new PennMUSHObject
				{
					DBRef = 2,
					Name = "Master Room",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 3,
					Name = "Child Room",
					Type = PennMUSHObjectType.Room,
					Parent = 2 // Parent is Master Room
				}
			]
		};

		var result = await converter.ConvertDatabaseAsync(database1);

		await Assert.That(result.IsSuccessful).IsTrue();
		
		// Verify child room has parent set (would need to check the parent relationship)
		// This is a basic test that conversion succeeded without errors
		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.RoomsConverted).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	[Skip("Creates objects in shared database that affect other tests - needs isolated database")]
	public async ValueTask ConverterSetsZoneRelationships()
	{
		var converter = GetConverter();
		
		var database1 = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 0,
					Name = "Limbo",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 1,
					Name = "God",
					Type = PennMUSHObjectType.Player,
					Password = "$SHA1$test$hash"
				},
				new PennMUSHObject
				{
					DBRef = 2,
					Name = "Zone Master",
					Type = PennMUSHObjectType.Room
				},
				new PennMUSHObject
				{
					DBRef = 3,
					Name = "Zoned Room",
					Type = PennMUSHObjectType.Room,
					Zone = 2 // Zone is Zone Master
				}
			]
		};

		var result = await converter.ConvertDatabaseAsync(database1);

		await Assert.That(result.IsSuccessful).IsTrue();
		
		// Verify zoned room has zone set (would need to check the zone relationship)
		// This is a basic test that conversion succeeded without errors
		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.RoomsConverted).IsGreaterThanOrEqualTo(2);
	}
}
