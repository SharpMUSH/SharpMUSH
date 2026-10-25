using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>A large game spends minutes in one phase; the import says how far into it it is.</summary>
public class PennMUSHImportProgressTests
{
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Progress Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			.. Enumerable.Range(3, 400).Select(dbref => new PennMUSHObject
			{
				DBRef = dbref, Name = $"Progress Thing {dbref}", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 0, Link = 0
			})
		]
	};

	[Test]
	public async Task ProgressMovesWithinEachPhase()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var reports = new List<ConversionProgress>();

		var result = await world.Converter.ConvertDatabaseAsync(Fixture(), new InlineProgress(reports.Add));

		await Assert.That(result.Errors).IsEmpty();
		var creating = reports.Where(report => report.CurrentPhase == "Creating objects" && report.PercentageComplete > 0).ToList();
		await Assert.That(creating.Count).IsGreaterThan(50);
		await Assert.That(creating.Select(report => report.PercentageComplete)).IsInOrder();
		await Assert.That(creating.Max(report => report.PercentageComplete)).IsLessThanOrEqualTo(25);
		await Assert.That(creating[^1].ProcessedObjects).IsGreaterThan(creating[0].ProcessedObjects);
		foreach (var phase in (string[])["Linking objects", "Creating attributes", "Creating locks"])
		{
			await Assert.That(reports.Count(report => report.CurrentPhase == phase)).IsGreaterThan(50).Because(phase);
		}
	}

	private sealed class InlineProgress(Action<ConversionProgress> onReport) : IProgress<ConversionProgress>
	{
		public void Report(ConversionProgress value) => onReport(value);
	}
}
