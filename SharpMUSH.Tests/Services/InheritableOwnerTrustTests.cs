using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PennMUSH <c>dbdefs.h:219</c>:
/// <code>#define Inheritable(x) (IsPlayer(x) || Inherit(x) || Inherit(Owner(x)) || Wizard(x))</code>
/// with <c>Inherit(x)</c> being <c>has_flag_by_name(x, "TRUST", NOTYPE)</c> (<c>dbdefs.h:143</c>),
/// which resolves through <c>match_flag</c> → <c>ptab_find</c> and therefore compares
/// case-insensitively (<c>strcasecmp</c> / <c>string_prefix</c> via <c>DOWNCASE</c>).
///
/// SharpMUSH's four branches map one-to-one onto that macro, but the third — the OWNER's TRUST
/// flag — compared with an ordinal <c>x.Name == "Trust"</c> while the seed spells the flag
/// <c>"TRUST"</c> (<c>FlagSeed.cs:22</c>), so it could never be true and the branch was dead.
/// </summary>
public class InheritableOwnerTrustTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<AnySharpObject> Known(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private async Task<DBRef> ThingOwnedBy(DBRef owner, string namePrefix)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, namePrefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chown {thing}={owner}"));
		return thing;
	}

	[Test]
	public async Task Inheritable_IsTrueWhenTheOwnerHasTrust()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "InheritTrustOwner");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {owner}=TRUST"));

		var thing = await ThingOwnedBy(owner, "InheritTrustThing");

		await Assert.That(await (await Known(thing)).Inheritable()).IsTrue()
			.Because("Inherit(Owner(x)) is the third branch of PennMUSH's Inheritable, and the owner is TRUST");
	}

	[Test]
	public async Task Inheritable_IsFalseWhenNeitherTheObjectNorItsOwnerIsTrusted()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "InheritPlainOwner");

		var thing = await ThingOwnedBy(owner, "InheritPlainThing");

		await Assert.That(await (await Known(thing)).Inheritable()).IsFalse()
			.Because("no branch of Inheritable applies to an ordinary thing owned by an ordinary player");
	}

	[Test]
	public async Task Inheritable_IsTrueWhenTheObjectItselfHasTrust()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "InheritSelfOwner");

		var thing = await ThingOwnedBy(owner, "InheritSelfThing");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}=TRUST"));

		await Assert.That(await (await Known(thing)).Inheritable()).IsTrue()
			.Because("Inherit(x) is the second branch, and it already went through the case-insensitive HasFlag");
	}

	[Test]
	public async Task Inheritable_IsTrueForAPlayer()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "InheritPlayer");

		await Assert.That(await (await Known(player)).Inheritable()).IsTrue()
			.Because("IsPlayer(x) is the first branch and short-circuits the rest");
	}
}
