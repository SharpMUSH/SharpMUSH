using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// An import runs against a live game (<c>DatabaseConversionController</c>), so what it writes has to
/// reach the engine's object cache and not only the store beneath it: the engine reads objects through
/// the Mediator, and a write that bypasses it leaves the cached copy describing the object as it was.
/// </summary>
public class PennMUSHImportCacheCoherenceTests
{
	private static PennMUSHDatabase Fixture() => new()
	{
		Version = "Cache Fixture",
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Location = 0 },
			new PennMUSHObject
			{
				DBRef = 10, Name = "Cache Hall", Type = PennMUSHObjectType.Room,
				Attributes = [new PennMUSHAttribute { Name = "DESCRIBE", Value = "A hall.", Flags = [] }]
			},
			new PennMUSHObject { DBRef = 11, Name = "Cache Parent", Type = PennMUSHObjectType.Thing, Location = 10 },
			new PennMUSHObject
			{
				DBRef = 12, Name = "Cache Child", Type = PennMUSHObjectType.Thing, Location = 10, Parent = 11, Zone = 11,
				Attributes = [new PennMUSHAttribute { Name = "DESCRIBE", Value = "A child.", Flags = [] }],
				Locks = new Dictionary<string, string> { ["Basic"] = "#TRUE" }
			},
			new PennMUSHObject
			{
				DBRef = 13, Name = "Cache Door", Type = PennMUSHObjectType.Exit, Location = 10, Link = 0,
				Attributes = [new PennMUSHAttribute { Name = "SUCCESS", Value = "Through.", Flags = [] }]
			}
		]
	};

	private static readonly string[] Names = ["Cache Hall", "Cache Parent", "Cache Child", "Cache Door"];

	/// <summary>
	/// A game is running while the import does: every object the import creates lands in Limbo, and a
	/// <c>look</c> there reads each of them through the engine. The converter reports progress between
	/// its phases, so reading everything at each report stands in for that game, deterministically.
	/// </summary>
	[Test]
	public async Task EveryImportedObjectReadsTheSameThroughTheEngineAsInTheStore()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var progress = new InlineProgress(report =>
		{
			if (report.CurrentPhase != "Creating objects")
			{
				ReadThroughTheEngineAsync(world).GetAwaiter().GetResult();
			}
		});
		var result = await world.Converter.ConvertDatabaseAsync(Fixture(), progress);
		await Assert.That(result.Errors).IsEmpty();

		foreach (var name in Names)
		{
			var key = await KeyOfAsync(world, name);
			var stored = (await world.Database.GetObjectNodeAsync(new DBRef(key))).Expect<AnySharpObject>();
			var engine = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();

			await Assert.That(await Describe(engine)).IsEqualTo(await Describe(stored))
				.Because($"the engine's cached {name} does not match what the import stored");
		}
	}

	/// <summary>Reads each imported object, and everything <see cref="Describe"/> looks at, through the engine.</summary>
	private static async Task ReadThroughTheEngineAsync(IsolatedImportWorld world)
	{
		foreach (var name in Names)
		{
			await Describe((await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(await KeyOfAsync(world, name))))).Expect<AnySharpObject>());
		}
	}

	private static async Task<int> KeyOfAsync(IsolatedImportWorld world, string name)
		=> (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;

	/// <summary>
	/// Invokes the callback on the reporting thread, between the converter's phases. <see cref="Progress{T}"/>
	/// posts instead, which would let the next phase start before the reads.
	/// </summary>
	private sealed class InlineProgress(Action<ConversionProgress> onReport) : IProgress<ConversionProgress>
	{
		public void Report(ConversionProgress value) => onReport(value);
	}

	/// <summary>Everything the import writes about an object, rendered for comparison.</summary>
	private static async Task<string> Describe(AnySharpObject obj)
	{
		var o = obj.Object();
		var parent = await o.Parent.WithCancellation(CancellationToken.None);
		var zone = await o.Zone.WithCancellation(CancellationToken.None);
		var locks = string.Join(",", o.Locks.OrderBy(l => l.Key).Select(l => $"{l.Key}={l.Value.LockString}"));
		var destination = "-";
		if (obj.IsExit)
		{
			var home = await obj.Expect<SharpExit>().Home.WithCancellation(CancellationToken.None);
			destination = home.IsNone ? "none" : home.Object()!.DBRef.ToString();
		}

		return $"{o.DBRef} {o.Name} parent={Ref(parent)} zone={Ref(zone)} locks=[{locks}] destination={destination}";
	}

	private static string Ref(AnyOptionalSharpObject obj) => obj.IsNone ? "none" : obj.Expect<AnySharpObject>().Object().DBRef.ToString();
}
