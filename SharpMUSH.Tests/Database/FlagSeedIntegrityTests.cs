using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.ArangoDB.Migrations;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins the object-flag seed against PennMUSH for the two flags that share the letter <c>m</c>, and
/// against itself for the invariant that a flag name is seeded exactly once.
///
/// <para>PennMUSH has two flags on <c>m</c>, told apart by object type: MISTRUST
/// (<c>src/flags.c:778</c> — <c>TYPE_THING | TYPE_EXIT | TYPE_ROOM</c>) and MYOPIC
/// (<c>game/txt/hlp/pennflag.hlp:333</c> — players). <c>pennflag.hlp:37</c> lists the shared letter as
/// "m - Mistrust/Myopic", and that line is the likely source of the error this fixes: all three
/// SharpMUSH providers seeded MYOPIC as an <em>alias</em> of MISTRUST, so the two flags could not be
/// set independently and MISTRUST carried the wrong type list into the bargain.</para>
///
/// <para>A shared symbol is normal here and needs no special handling —
/// <c>SharpObjectFlag.Symbol</c> documents itself as not unique, and the seed already ships
/// ABODE/ANSI on 'A', CHOWN_OK/COLOR on 'C' and NO_LEAVE/NO_TEL on 'N', each pair separated by its
/// type restrictions.</para>
/// </summary>
public class FlagSeedIntegrityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task MistrustIsSeededWithPennMushTypesAndNoMyopicAlias()
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery("MISTRUST"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("MISTRUST");
		await Assert.That(flag.Symbol).IsEqualTo("m");
		await Assert.That(flag.Aliases ?? [])
			.DoesNotContain("MYOPIC")
			.Because("MYOPIC is its own flag in PennMUSH, not another name for MISTRUST");
		await Assert.That(flag.TypeRestrictions.Order(StringComparer.Ordinal).ToArray())
			.IsEquivalentTo(new[] { "EXIT", "ROOM", "THING" })
			.Because("src/flags.c:778 declares MISTRUST as TYPE_THING | TYPE_EXIT | TYPE_ROOM");
	}

	[Test]
	public async Task MyopicIsSeededAsItsOwnPlayerFlag()
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery("MYOPIC"));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name)
			.IsEqualTo("MYOPIC")
			.Because("asking for MYOPIC must not resolve to MISTRUST");
		await Assert.That(flag.Symbol).IsEqualTo("m");
		await Assert.That(flag.TypeRestrictions)
			.IsEquivalentTo(new[] { "PLAYER" })
			.Because("pennflag.hlp:333 documents MYOPIC as a player flag");
	}

	/// <summary>
	/// The ArangoDB seed wrote MISTRUST twice — once correctly and once carrying the MYOPIC alias — so
	/// which of the two documents any lookup found was down to iteration order. Name is the identity of a
	/// flag on every provider (SurrealDB and Memgraph key their records on it), so a duplicate is a seed
	/// bug wherever it appears.
	/// </summary>
	[Test]
	public async Task NoFlagNameIsSeededTwice()
	{
		var duplicates = await Mediator.CreateStream(new GetAllObjectFlagsQuery())
			.ToListAsync();

		var offenders = duplicates
			.GroupBy(flag => flag.Name, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} x{group.Count()}")
			.ToArray();

		await Assert.That(offenders)
			.IsEmpty()
			.Because("a flag name is its identity; two rows sharing one makes every lookup order-dependent");
	}

	/// <summary>
	/// The forward repair, run against a database that actually holds the broken seed.
	///
	/// <para>The seeds above only prove a <em>fresh</em> database comes out right;
	/// <see cref="Migration_RepairMistrustMyopic"/> exists for the ones already deployed, and on a fresh
	/// database it matches nothing, so running the suite would never exercise it. This builds the broken
	/// shape by hand in a throwaway ArangoDB database — two MISTRUST documents, the second carrying the
	/// MYOPIC alias, and an object edge pointing at that second one — runs the migration, and checks
	/// both that the duplicate is gone and that the object did not silently lose its flag.</para>
	///
	/// <para>Arango only: SurrealDB re-runs its flag seed as an unconditional UPSERT on every boot and so
	/// corrects itself, and Memgraph's repair is a <c>WHERE 'MYOPIC' IN f.aliases</c> statement inside
	/// its own migration path. The non-Arango legs skip.</para>
	/// </summary>
	[Test]
	public async Task RepairMigration_SplitsAnAlreadyBrokenDatabase()
	{
		if (WebAppFactoryArg.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var scratch = new ArangoHandle($"flagrepair{Guid.NewGuid():N}"[..24]);
		await context.Database.CreateAsync(scratch);

		try
		{
			await context.Collection.CreateAsync(scratch, new ArangoCollection
			{
				Name = SharpMUSH.Database.DatabaseConstants.ObjectFlags,
				Type = ArangoCollectionType.Document
			});
			await context.Collection.CreateAsync(scratch, new ArangoCollection
			{
				Name = SharpMUSH.Database.DatabaseConstants.HasFlags,
				Type = ArangoCollectionType.Edge
			});

			var wrongTypes = new[] { "PLAYER", "EXIT", "THING" };
			await context.Document.CreateAsync(scratch, SharpMUSH.Database.DatabaseConstants.ObjectFlags,
				new { Name = "MISTRUST", Symbol = "m", System = true, TypeRestrictions = wrongTypes });
			var aliased = await context.Document.CreateAsync(scratch, SharpMUSH.Database.DatabaseConstants.ObjectFlags,
				new
				{
					Name = "MISTRUST",
					Aliases = (string[])["MYOPIC"],
					Symbol = "m",
					System = true,
					TypeRestrictions = wrongTypes
				});

			const string ObjectId = "node_objects/12345";
			await context.Document.CreateAsync(scratch, SharpMUSH.Database.DatabaseConstants.HasFlags,
				new Dictionary<string, object> { ["_from"] = ObjectId, ["_to"] = aliased.Id });

			await new Migration_RepairMistrustMyopic().Up(new ArangoMigrator(context), scratch);

			var mistrusts = await context.Query.ExecuteAsync<FlagRow>(scratch,
				$"FOR f IN {SharpMUSH.Database.DatabaseConstants.ObjectFlags:@} FILTER f.Name == \"MISTRUST\" RETURN f");

			await Assert.That(mistrusts.Count)
				.IsEqualTo(1)
				.Because("the migration collapses the duplicate rows into one");
			await Assert.That(mistrusts[0].Aliases ?? []).DoesNotContain("MYOPIC");
			await Assert.That(mistrusts[0].TypeRestrictions.Order(StringComparer.Ordinal).ToArray())
				.IsEquivalentTo(new[] { "EXIT", "ROOM", "THING" });

			var myopics = await context.Query.ExecuteAsync<FlagRow>(scratch,
				$"FOR f IN {SharpMUSH.Database.DatabaseConstants.ObjectFlags:@} FILTER f.Name == \"MYOPIC\" RETURN f");

			await Assert.That(myopics.Count).IsEqualTo(1);
			await Assert.That(myopics[0].TypeRestrictions).IsEquivalentTo(new[] { "PLAYER" });

			var edgeTargets = await context.Query.ExecuteAsync<string>(scratch,
				$"FOR e IN {SharpMUSH.Database.DatabaseConstants.HasFlags:@} RETURN e._to");

			await Assert.That(edgeTargets)
				.IsEquivalentTo(new[] { mistrusts[0].Id })
				.Because("an object that was MISTRUST must still be MISTRUST after the duplicate is removed");
		}
		finally
		{
			await context.Database.DropAsync(scratch);
		}
	}

	private sealed record FlagRow(
		[property: System.Text.Json.Serialization.JsonPropertyName("_id")]
		string Id,
		string Name,
		string[]? Aliases,
		string[] TypeRestrictions);
}
