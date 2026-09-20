using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@UNRECYCLE</c> is <c>@UNDESTROY</c> under another name: PennMUSH maps both onto
/// <c>cmd_undestroy</c> (<c>src/command.c:319</c> and <c>:324</c>). It was a wizard-gated
/// placeholder here that took no arguments and only announced itself as unimplemented.
/// <para>
/// Permission is the subject of half of these, so they run through a mortal handle — God controls
/// everything and would pass the gate vacuously. Not parallel with the destruction classes, whose
/// <c>@purge</c> frees any GOING object in the shared world.
/// </para>
/// </summary>
[NotInParallel]
public class UnrecycleAliasTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private ValueTask<CallState> AsGod(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private ValueTask<CallState> As(TestIsolationHelpers.TestPlayer who, string command) =>
		Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain(command));

	private Task<TestIsolationHelpers.TestPlayer> MortalAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService,
			prefix);

	private async Task<DBRef> CreateAsync(string prefix, TestIsolationHelpers.TestPlayer? owner = null)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var result = owner is null ? await AsGod($"@create {name}") : await As(owner, $"@create {name}");
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private async Task<bool> IsGoingAsync(DBRef target)
		=> await (await Mediator.Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>().HasFlag("GOING");

	private bool WasTold(DBRef who, string message)
		=> WebAppFactoryArg.Notifications.For(who).Any(m => m.Contains(message, StringComparison.Ordinal));

	/// <summary>The alias does the work: a GOING object is spared, exactly as <c>@undestroy</c> spares it.</summary>
	[Test]
	[Arguments("@undestroy")]
	[Arguments("@unrecycle")]
	public async Task EitherName_SparesAGoingObject(string command)
	{
		var thing = await CreateAsync("Unrecycle_Going");

		await AsGod($"@destroy {thing}");
		await Assert.That(await IsGoingAsync(thing)).IsTrue();

		await AsGod($"{command} {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsFalse();
	}

	/// <summary>An object that was never destroyed is not recoverable, under either name.</summary>
	[Test]
	[Arguments("@undestroy")]
	[Arguments("@unrecycle")]
	public async Task EitherName_RefusesAnObjectNotMarkedForDestruction(string command)
	{
		var mortal = await MortalAsync("Unrecycle_NotGoing");
		var thing = await CreateAsync("Unrecycle_NotGoing", mortal);

		var result = await As(mortal, $"{command} {thing}");

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NotGoing);
		await Assert.That(WasTold(mortal.DbRef, ErrorMessages.Notifications.NotMarkedForDestruction)).IsTrue();
	}

	/// <summary>
	/// Control, not wizardry, is the gate — a mortal recovers their own object. The placeholder
	/// required WIZARD, so this is what the alias buys a builder.
	/// </summary>
	[Test]
	[Arguments("@undestroy")]
	[Arguments("@unrecycle")]
	public async Task EitherName_LetsAMortalRecoverTheirOwnObject(string command)
	{
		var mortal = await MortalAsync("Unrecycle_Own");
		var thing = await CreateAsync("Unrecycle_Own", mortal);

		await As(mortal, $"@destroy {thing}");
		await Assert.That(await IsGoingAsync(thing)).IsTrue();

		await As(mortal, $"{command} {thing}");

		await Assert.That(await IsGoingAsync(thing)).IsFalse();
		await Assert.That(WasTold(mortal.DbRef, "Spared from destruction")).IsTrue();
	}

	/// <summary>A mortal cannot recover somebody else's object, and the object stays GOING.</summary>
	[Test]
	[Arguments("@undestroy")]
	[Arguments("@unrecycle")]
	public async Task EitherName_RefusesATargetTheExecutorDoesNotControl(string command)
	{
		var owner = await MortalAsync("Unrecycle_Owner");
		var stranger = await MortalAsync("Unrecycle_Stranger");
		var thing = await CreateAsync("Unrecycle_NotYours", owner);

		await As(owner, $"@destroy {thing}");
		await Assert.That(await IsGoingAsync(thing)).IsTrue();

		var result = await As(stranger, $"{command} {thing}");

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(WasTold(stranger.DbRef, ErrorMessages.Notifications.PermissionDenied)).IsTrue();
		await Assert.That(await IsGoingAsync(thing)).IsTrue();
	}
}
