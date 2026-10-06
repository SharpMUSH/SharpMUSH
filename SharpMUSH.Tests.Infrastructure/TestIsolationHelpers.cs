using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Identity;
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

	/// <summary>Allocates a connection handle shared by all helpers and connection-level tests.</summary>
	public static long GenerateUniqueHandle() => Interlocked.Increment(ref _nextHandle);

	/// <summary>The password every test player is created with.</summary>
	public const string TestPassword = "TestPassword123";

	/// <summary>
	/// <see cref="TestPassword"/>, hashed once for the whole run. A player created with a plaintext password
	/// is hashed on creation (PBKDF2, tens of milliseconds of CPU, paid by every test that makes a player);
	/// a stored hash with an empty salt skips that and leaves the same record. A hash does not depend on
	/// whose password it is, so one verifies for every player.
	/// </summary>
	public static string TestPasswordHash => TestPasswordHashValue.Value;

	private static readonly Lazy<string> TestPasswordHashValue = new(() =>
		new PasswordHasher<string>().HashPassword(string.Empty, TestPassword));

	/// <summary>
	/// A <see cref="CreatePlayerCommand"/> for a test player whose password is <see cref="TestPassword"/>,
	/// stored already hashed (<see cref="TestPasswordHash"/>).
	/// </summary>
	public static CreatePlayerCommand CreateTestPlayerCommand(string name, DBRef location, DBRef home, int quota) =>
		new(name, TestPasswordHash, location, home, quota, Salt: string.Empty);

	private static long _nextName;
	private static readonly string RunId = System.Buffers.Text.Base64Url.EncodeToString(
		System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
	/// <summary>
	/// Generates a unique name from the readable prefix, a process-run identifier, and an
	/// atomic sequence. Parallel calls never reuse a suffix within the run.
	/// </summary>
	public static string GenerateUniqueName(string prefix) =>
		$"{prefix}_{RunId}_{Interlocked.Increment(ref _nextName):x}";

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
	public static Task<DBRef> CreateTestPlayerAsync(
		IServiceProvider services,
		IMediator mediator,
		string namePrefix)
		=> CreateNamedTestPlayerAsync(services, mediator, GenerateUniqueName(namePrefix));

	/// <summary>
	/// As <see cref="CreateTestPlayerAsync"/>, but takes the finished name rather than a prefix, so a
	/// caller that needs to know the player's name does not have to read it back out of the database.
	/// </summary>
	private static async Task<DBRef> CreateNamedTestPlayerAsync(
		IServiceProvider services,
		IMediator mediator,
		string name,
		DBRef? initialHome = null)
	{
		var options = services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var defaultHome = initialHome ?? new DBRef((int)options.CurrentValue.Database.DefaultHome);
		var startingQuota = (int)options.CurrentValue.Limit.StartingQuota;

		return await mediator.Send(CreateTestPlayerCommand(name, defaultHome, defaultHome, startingQuota));
	}

	/// <summary>
	/// Result returned by <see cref="CreateTestPlayerWithHandleAsync"/>: the player's
	/// <see cref="DBRef"/> together with the connection handle that was registered and
	/// bound so that <c>CommandParse(handle, …)</c> executes as that player, and the unique
	/// name it was created under — which a test asserting on name-prefixed output needs.
	/// </summary>
	public record TestPlayer(DBRef DbRef, long Handle, string Name);

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
	/// <returns>A <see cref="TestPlayer"/> with the DBRef, handle and name.</returns>
	public static Task<TestPlayer> CreateTestPlayerWithHandleAsync(
		IServiceProvider services,
		IMediator mediator,
		IConnectionService connectionService,
		string namePrefix)
		=> CreatePlayerWithHandleAsync(services, mediator, connectionService, namePrefix, null);

	/// <summary>Creates the player at its supplied home before registering or binding a connection.</summary>
	public static Task<TestPlayer> CreateTestPlayerWithHandleAsync(
		IServiceProvider services, IMediator mediator, IConnectionService connectionService,
		string namePrefix, DBRef initialHome)
		=> CreatePlayerWithHandleAsync(services, mediator, connectionService, namePrefix, initialHome);

	private static async Task<TestPlayer> CreatePlayerWithHandleAsync(
		IServiceProvider services, IMediator mediator, IConnectionService connectionService,
		string namePrefix, DBRef? initialHome)
	{
		var name = GenerateUniqueName(namePrefix);
		var playerDbRef = await CreateNamedTestPlayerAsync(services, mediator, name, initialHome);
		var handle = await ConnectTestHandleAsync(connectionService, playerDbRef);

		return new TestPlayer(playerDbRef, handle, name);
	}

	/// <summary>
	/// Registers a fresh connection handle that nobody is logged in on yet — a client sitting on the
	/// connect screen. The connection service is shared by the whole test session, so the handle comes
	/// from <see cref="GenerateUniqueHandle"/>: a random or hard-coded handle can land on another test's
	/// live connection and take it over.
	/// </summary>
	/// <param name="connectionService">The connection service to register the handle on.</param>
	/// <param name="connectionType">The transport the connection claims to be (<c>telnet</c>, <c>websocket</c>…).</param>
	/// <param name="metadata">Connection metadata to register with, or null for the service's defaults.</param>
	/// <returns>The registered handle.</returns>
	public static async Task<long> RegisterTestHandleAsync(
		IConnectionService connectionService,
		string connectionType = "test",
		ConcurrentDictionary<string, string>? metadata = null)
	{
		var handle = GenerateUniqueHandle();
		await connectionService.Register(
			handle, "localhost", "localhost", connectionType,
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			metadata);
		return handle;
	}

	/// <summary>
	/// As <see cref="RegisterTestHandleAsync"/>, then binds the handle to <paramref name="player"/>, so
	/// <c>CommandParse(handle, …)</c> runs as that player and connection functions see it online.
	/// </summary>
	public static async Task<long> ConnectTestHandleAsync(
		IConnectionService connectionService,
		DBRef player,
		string connectionType = "test",
		ConcurrentDictionary<string, string>? metadata = null)
	{
		var handle = await RegisterTestHandleAsync(connectionService, connectionType, metadata);
		await connectionService.Bind(handle, player);
		return handle;
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
