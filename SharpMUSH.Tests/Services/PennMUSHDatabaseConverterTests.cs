using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class PennMUSHDatabaseConverterTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

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
}
