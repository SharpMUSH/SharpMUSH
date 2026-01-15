using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GuestLoginTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask ConnectGuest_BasicLogin_Succeeds()
	{
		// Create a guest character
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate Guest1="));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Guest1=Guest"));
		
		// Give the database a moment to persist
		await Task.Delay(100);

		// Connect using a fresh handle (not yet bound to a player)
		var guestHandle = 1000L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		// Should return a DBRef (not an error)
		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();
		
		// Should receive "Connected!" message
		await NotifyService
			.Received()
			.Notify(Arg.Is<long>(h => h == guestHandle), 
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));
	}

	[Test]
	public async ValueTask ConnectGuest_CaseInsensitive_Succeeds()
	{
		// Create a guest character
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate Guest2="));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Guest2=Guest"));
		
		// Give the database a moment to persist
		await Task.Delay(100);

		// Connect with different case variations
		var guestHandle = 1001L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect GUEST"));

		// Should return a DBRef (not an error)
		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();

		await NotifyService
			.Received()
			.Notify(Arg.Is<long>(h => h == guestHandle), 
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));
	}

	[Test]
	public async ValueTask ConnectGuest_NoGuestCharacters_FailsWithError()
	{
		// Don't create any guest characters
		var guestHandle = 1002L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		// Should return error CallState
		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsTrue();
		await Assert.That(resultMessage.Contains("NO GUEST CHARACTERS")).IsTrue();
		
		// Should receive error message about no guest characters
		await NotifyService
			.Received()
			.Notify(Arg.Is<long>(h => h == guestHandle), 
				Arg.Is<OneOf<MString, string>>(s => 
					TestHelpers.MessageContains(s, "guest") || 
					TestHelpers.MessageContains(s, "available") ||
					TestHelpers.MessageContains(s, "find")));
	}

	[Test]
	public async ValueTask ConnectGuest_MultipleGuests_SelectsAppropriateOne()
	{
		// Create multiple guest characters
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate Guest3="));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Guest3=Guest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate Guest4="));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Guest4=Guest"));
		
		// Give the database a moment to persist
		await Task.Delay(100);

		// Connect as guest - should connect to one of the available guests
		var guestHandle = 1003L;
		var result = await Parser.CommandParse(guestHandle, ConnectionService, MModule.single("connect guest"));

		// Should return a DBRef (not an error)
		var resultMessage = result.Message?.ToString() ?? "";
		await Assert.That(resultMessage.Contains("#-1")).IsFalse();

		await NotifyService
			.Received()
			.Notify(Arg.Is<long>(h => h == guestHandle), 
				Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));
	}

	[Test]
	[Skip("Requires guest configuration testing infrastructure")]
	public async ValueTask ConnectGuest_GuestsDisabled_FailsWithError()
	{
		// This test would require modifying the configuration to disable guests
		// Skipping for now as it requires configuration testing infrastructure
		await Task.CompletedTask;
	}

	[Test]
	[Skip("Requires advanced connection management")]
	public async ValueTask ConnectGuest_MaxGuestsReached_FailsWithError()
	{
		// This test would require:
		// 1. Setting max_guests configuration
		// 2. Creating exactly that many guest connections
		// 3. Attempting to connect one more
		// Skipping for now as it requires more complex setup
		await Task.CompletedTask;
	}
}
