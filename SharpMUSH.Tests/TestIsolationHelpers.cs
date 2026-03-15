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
		var uniqueName = GenerateUniqueName(namePrefix);
		var result = await parser.CommandParse(1, connectionService, MModule.single($"@create {uniqueName}"));
		return DBRef.Parse(result.Message!.ToPlainText()!);
	}
}
