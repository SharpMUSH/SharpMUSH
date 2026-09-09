using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Shared helpers that improve test isolation by creating fresh, uniquely-named objects
/// for each test so that no test mutates the shared player #1 or any other shared state.
/// </summary>
public static class TestIsolationHelpers
{
	/// <summary>
	/// Monotonically increasing handle counter.  Starts above any well-known handles
	/// (handle 1 = God, handle 2 = BBS tester) to avoid collisions.
	/// </summary>
	private static long _nextHandle = 100;
	/// <summary>
	/// Generates a unique name by combining <paramref name="prefix"/> with the current
	/// UTC Unix-millisecond timestamp and a random four-digit number.
	/// </summary>
	public static string GenerateUniqueName(string prefix) =>
		$"{prefix}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";

	/// <summary>
	/// Creates a fresh, isolated player through the database layer, so tests never mutate
	/// the shared player #1 object.  The player name is made unique via
	/// <see cref="GenerateUniqueName"/> to prevent cross-test name collisions.
	/// </summary>
	/// <param name="services">The test service provider (e.g. <c>WebAppFactoryArg.Services</c>).</param>
	/// <param name="mediator">The mediator used to send the <see cref="CreatePlayerCommand"/>.</param>
	/// <param name="namePrefix">
	/// A short, human-readable prefix included in the player name
	/// (e.g. <c>"ZT_ZMRCmd"</c> or <c>"PDT_SelfOwnership"</c>).
	/// </param>
	/// <returns>The <see cref="DBRef"/> of the newly created player.</returns>
	public static async Task<DBRef> CreateTestPlayerAsync(
		IServiceProvider services,
		IMediator mediator,
		string namePrefix)
	{
		var options = services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var defaultHome = new DBRef((int)options.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)options.CurrentValue.Limit.StartingQuota;

		return await mediator.Send(new CreatePlayerCommand(
			GenerateUniqueName(namePrefix),
			"TestPassword123",
			defaultHome,
			defaultHome,
			startingQuota));
	}

	/// <summary>
	/// Result returned by <see cref="CreateTestPlayerWithHandleAsync"/>: the player's
	/// <see cref="DBRef"/> together with the connection handle that was registered and
	/// bound so that <c>CommandParse(handle, …)</c> executes as that player.
	/// </summary>
	public record TestPlayer(DBRef DbRef, long Handle);

	/// <summary>
	/// Creates a fresh, isolated player <b>and</b> registers + binds a unique connection
	/// handle for it, so commands can be executed as that player via
	/// <c>Parser.CommandParse(testPlayer.Handle, ConnectionService, …)</c>.
	/// </summary>
	/// <param name="services">The test service provider (e.g. <c>WebAppFactoryArg.Services</c>).</param>
	/// <param name="mediator">The mediator used to send the <see cref="CreatePlayerCommand"/>.</param>
	/// <param name="connectionService">The connection service to register the handle on.</param>
	/// <param name="namePrefix">
	/// A short, human-readable prefix included in the player name
	/// (e.g. <c>"MvtTelSelf"</c> or <c>"MvtHome"</c>).
	/// </param>
	/// <returns>A <see cref="TestPlayer"/> with the DBRef and handle.</returns>
	public static async Task<TestPlayer> CreateTestPlayerWithHandleAsync(
		IServiceProvider services,
		IMediator mediator,
		IConnectionService connectionService,
		string namePrefix)
	{
		var playerDbRef = await CreateTestPlayerAsync(services, mediator, namePrefix);
		var handle = Interlocked.Increment(ref _nextHandle);

		await connectionService.Register(
			handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8);
		await connectionService.Bind(handle, playerDbRef);

		return new TestPlayer(playerDbRef, handle);
	}

	/// <summary>
	/// Creates a fresh, isolated thing object by running <c>@create</c> through the MUSH
	/// command parser.  The name is made unique via <see cref="GenerateUniqueName"/> to
	/// prevent cross-test name collisions.
	/// </summary>
	/// <param name="parser">The command parser (e.g. <c>Parser</c> from the test class).</param>
	/// <param name="connectionService">
	/// The connection service (e.g. <c>ConnectionService</c> from the test class).
	/// </param>
	/// <param name="namePrefix">
	/// A short, human-readable prefix included in the object name
	/// (e.g. <c>"AttrTest"</c> or <c>"EditTest"</c>).
	/// </param>
	/// <returns>The <see cref="DBRef"/> of the newly created thing.</returns>
	public static async Task<DBRef> CreateTestThingAsync(
		IMUSHCodeParser parser,
		IConnectionService connectionService,
		string namePrefix)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var uniqueName = GenerateUniqueName(namePrefix);
		var result = await CreateObjectCommandAsync(parser, connectionService, uniqueName);
		var message = result.Message
			?? throw new InvalidOperationException($"@create {uniqueName} returned a null message. The command may have failed.");
		var plainText = message.ToPlainText()
			?? throw new InvalidOperationException($"@create {uniqueName} message could not be converted to plain text.");
		var created = DBRef.Parse(plainText);

		// Things are created NO_COMMAND, as PennMUSH's thing_flags ships them, so nothing on them is
		// searched for $-commands until the flag comes off. A test thing exists to be driven, so the
		// helper does what a game has to do for any object that carries a $-command.
		await ClearNoCommandAsync(parser, connectionService, created);

		return created;
	}

	/// <summary>Creates named fixture data under a finite setup deadline, independent of behavior being tested.</summary>
	public static async ValueTask<CallState> CreateObjectCommandAsync(IMUSHCodeParser parser,
		IConnectionService connectionService, string name, long handle = 1)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		var result = await parser.CommandParse(handle, connectionService, MarkupText.Plain($"@create {name}"));
		if (!DBRef.TryParse(result.Message?.ToPlainText() ?? "", out _))
			throw new InvalidOperationException($"Fixture @create {name} failed: {result.Message?.ToPlainText()}");
		return result;
	}

	/// <summary>
	/// Takes NO_COMMAND off an object so its <c>$</c>-commands are matched again — the
	/// <c>@set &lt;object&gt;=!NO_COMMAND</c> that every game now has to run on anything carrying one,
	/// since players, rooms and things are all created with the flag.
	/// </summary>
	public static async Task ClearNoCommandAsync(
		IMUSHCodeParser parser,
		IConnectionService connectionService,
		DBRef target) =>
		await parser.CommandParse(1, connectionService,
			MarkupText.Plain($"@set #{target.Number}=!NO_COMMAND"));
}
