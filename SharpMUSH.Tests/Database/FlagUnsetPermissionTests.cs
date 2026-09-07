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
		var created = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@pcreate {name}=pw_{name}"));
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
