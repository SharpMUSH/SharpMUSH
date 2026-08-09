using Core.Arango;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins the seed → read round trip for flag unset permissions.
///
/// <para><b>These flags were never actually unprotected.</b> The ArangoDB seed spelled the property
/// <c>UnSetPermissions</c> on thirteen flags while <c>SharpObjectFlagQueryResult</c> reads
/// <c>UnsetPermissions</c>, which looks like it should have left them with no unset restriction at all.
/// It did not: <c>Core.Arango</c>'s <c>ArangoJsonSerializer</c> is constructed from
/// <c>JsonSerializerDefaults.Web</c>, which sets <c>PropertyNameCaseInsensitive</c>, so the misspelled
/// key landed on the right member anyway. ROYALTY, SUSPECT and the rest were enforced the whole time —
/// measured, by running the behavioural tests below against a database migrated by the unfixed seed
/// and watching all of them pass.</para>
///
/// <para>So what was fixed is a coincidence, not a hole. The coincidence is not worth keeping, for
/// three reasons: AQL attribute access is case-sensitive, so the first query that filters on this
/// field loses the restriction silently; changing the serializer's naming policy would do the same;
/// and a document carrying both spellings resolves last-key-wins rather than to the correct one.</para>
///
/// <para>That is also why the assertions split in two. Everything behavioural here passes with or
/// without the fix and exists to pin the enforcement contract going forward. Only
/// <see cref="NoSeededFlag_CarriesAMisspelledPermissionProperty"/>, which reads the stored attribute
/// names, can tell the two seeds apart.</para>
/// </summary>
public class FlagUnsetPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IManipulateSharpObjectService ManipulateService
		=> WebAppFactoryArg.Services.GetRequiredService<IManipulateSharpObjectService>();

	/// <summary>
	/// Every flag the seeds restrict unsetting on, with the permission level each requires. Staff-only
	/// flags and the monitoring/punishment flags a badly behaved character would most want to clear.
	/// </summary>
	public static IEnumerable<(string Flag, string Permission)> RestrictedUnsetFlags() =>
	[
		("NOSPOOF", "odark"),
		("GAGGED", "wizard"),
		("JURY_OK", "royalty"),
		("MISTRUST", "trusted"),
		("ROYALTY", "trusted"),
		("SUSPECT", "wizard"),
		("CHAN_USEFIRSTMATCH", "trusted"),
		("NO_LOG", "wizard"),
		("PARANOID", "odark"),
		("MONIKER", "royalty"),
		("GOING", "wizard"),
		("GOING_TWICE", "wizard"),
		("WIZARD", "wizard"),
		("FIXED", "wizard"),
		("TRUST", "trusted"),
		("JUDGE", "royalty"),
		("UNREGISTERED", "royalty"),
		("APPROVED", "royalty")
	];

	/// <summary>
	/// The storage-level assertion, and the only one that fails on the misspelled seed.
	///
	/// <para>The behavioural tests below pass either way, because <c>ArangoJsonSerializer</c> is built on
	/// <c>JsonSerializerDefaults.Web</c> and so deserializes case-insensitively — <c>UnSetPermissions</c>
	/// happens to land on <c>UnsetPermissions</c>. That is the whole reason the misspelling went
	/// unnoticed, and it is not a property worth relying on: AQL attribute access is case-sensitive,
	/// swapping the naming policy would silently drop the restriction on twelve flags, and a document
	/// carrying both spellings resolves last-key-wins rather than to the correct one.</para>
	///
	/// <para>Arango only — Memgraph and SurrealDB seed from positional named tuples the compiler checks,
	/// so this class of typo cannot reach their documents. Those legs skip.</para>
	/// </summary>
	[Test]
	public async Task NoSeededFlag_CarriesAMisspelledPermissionProperty()
	{
		if (WebAppFactoryArg.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = WebAppFactoryArg.Services.GetRequiredService<ArangoHandle>();
		var offenders = await context.Query.ExecuteAsync<string>(handle,
			"""
			FOR flag IN @@c
				FILTER LENGTH(ATTRIBUTES(flag)[* FILTER LOWER(CURRENT) == "unsetpermissions"
					AND CURRENT != "UnsetPermissions"]) > 0
				RETURN flag.Name
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", SharpMUSH.Database.DatabaseConstants.ObjectFlags }
			});

		await Assert.That(offenders)
			.IsEmpty()
			.Because("the seed must write the property name the model reads, not one that only matches case-insensitively");
	}

	[Test]
	[MethodDataSource(nameof(RestrictedUnsetFlags))]
	public async Task SeededFlag_ReadsBackItsUnsetPermissions(string flagName, string permission)
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery(flagName));

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.UnsetPermissions)
			.IsNotEmpty()
			.Because($"{flagName} must not be unsettable by anyone who controls the object");
		await Assert.That(flag.UnsetPermissions).Contains(permission);
	}

	/// <summary>
	/// The whole point of the property, stated as behaviour: a plain player carrying SUSPECT cannot
	/// take it off themselves. They control themselves, so the ownership gate lets them through and the
	/// flag's unset permission is the only thing standing in the way. SUSPECT is the flag to test with
	/// because it has no <c>CheckFlagSpecificPermissions</c> rule of its own — the generic gate is the
	/// entire defence.
	/// </summary>
	[Test]
	public async Task PlainPlayer_CannotUnsetSuspectFromThemselves()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Suspect");
		var created = await Parser.CommandParse(1, ConnectionService, MModule.single($"@pcreate {name}=pw_{name}"));
		var playerDbRef = DBRef.Parse(created.Message!.ToPlainText()!);
		var player = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Known;

		var suspect = await Mediator.Send(new GetObjectFlagQuery("SUSPECT"));
		await Mediator.Send(new SetObjectFlagCommand(player, suspect!));

		var reread = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Known;
		var result = await ManipulateService.SetOrUnsetFlag(reread, reread, "!SUSPECT", false);

		await Assert.That(result.Message!.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		var after = (await Mediator.Send(new GetObjectNodeQuery(playerDbRef))).Known;
		var flags = await after.Object().Flags.Value.ToArrayAsync();
		await Assert.That(flags.Any(flag => flag.Name == "SUSPECT"))
			.IsTrue()
			.Because("a refused unset must leave the flag in place");
	}
}
