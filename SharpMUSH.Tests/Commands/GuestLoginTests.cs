using Microsoft.Extensions.DependencyInjection;
using Mediator;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GuestLoginTests : TestClassFactory
{

[Test]
public async ValueTask ConnectGuest_NoGuestCharacters_FailsWithError()
{
// Don't create any guest characters - test the error case
var guestHandle = 1002L;
var result = await CommandParser.CommandParse(guestHandle, Services.GetRequiredService<IConnectionService>(), MModule.single("connect guest"));

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
public async ValueTask ConnectGuest_BasicLogin_Succeeds()
{
// Get default home from configuration
var defaultHome = new DBRef((int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.DefaultHome);
var startingQuota = (int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.StartingQuota;

// Create a guest character using Mediator
var playerDbRef = await Services.GetRequiredService<IMediator>().Send(new CreatePlayerCommand(
"Guest1",
"testpass",
defaultHome,
defaultHome,
startingQuota
));

// Get the player object
var player = await Services.GetRequiredService<IMediator>().CreateStream(new GetPlayerQuery("Guest1")).FirstOrDefaultAsync();
await Assert.That(player).IsNotNull();

// Get Guest power
var guestPower = await Services.GetRequiredService<IMediator>().Send(new GetPowerQuery("Guest"));
await Assert.That(guestPower).IsNotNull();

// Set Guest power on the player
var anyPlayer = new AnySharpObject(player!);
var setPowerResult = await Services.GetRequiredService<IMediator>().Send(new SetObjectPowerCommand(anyPlayer, guestPower!));
await Assert.That(setPowerResult).IsTrue();

// Give the database a moment to persist
await Task.Delay(200);

// Connect using a fresh handle (not yet bound to a player)
var guestHandle = 1000L;
var result = await CommandParser.CommandParse(guestHandle, Services.GetRequiredService<IConnectionService>(), MModule.single("connect guest"));

// Should return a DBRef (not an error)
var resultMessage = result.Message?.ToString() ?? "";
await Assert.That(resultMessage.Contains("#-1")).IsFalse();

// Should receive "Connected!" message
await NotifyService
.Received()
.Notify(Arg.Is<long>(h => h == guestHandle), 
Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));

// Cleanup
await CommandParser.CommandParse(1, Services.GetRequiredService<IConnectionService>(), MModule.single($"@destroy Guest1"));
}

[Test]
public async ValueTask ConnectGuest_CaseInsensitive_Succeeds()
{
// Get default home from configuration
var defaultHome = new DBRef((int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.DefaultHome);
var startingQuota = (int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.StartingQuota;

// Create a guest character using Mediator
var playerDbRef = await Services.GetRequiredService<IMediator>().Send(new CreatePlayerCommand(
"Guest2",
"testpass",
defaultHome,
defaultHome,
startingQuota
));

// Get player and set Guest power
var player = await Services.GetRequiredService<IMediator>().CreateStream(new GetPlayerQuery("Guest2")).FirstOrDefaultAsync();
var guestPower = await Services.GetRequiredService<IMediator>().Send(new GetPowerQuery("Guest"));
await Services.GetRequiredService<IMediator>().Send(new SetObjectPowerCommand(new AnySharpObject(player!), guestPower!));

await Task.Delay(200);

// Connect with different case variations
var guestHandle = 1001L;
var result = await CommandParser.CommandParse(guestHandle, Services.GetRequiredService<IConnectionService>(), MModule.single("connect GUEST"));

// Should return a DBRef (not an error)
var resultMessage = result.Message?.ToString() ?? "";
await Assert.That(resultMessage.Contains("#-1")).IsFalse();

await NotifyService
.Received()
.Notify(Arg.Is<long>(h => h == guestHandle), 
Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));

// Cleanup
await CommandParser.CommandParse(1, Services.GetRequiredService<IConnectionService>(), MModule.single($"@destroy Guest2"));
}

[Test]
public async ValueTask ConnectGuest_MultipleGuests_SelectsAppropriateOne()
{
// Get default home from configuration
var defaultHome = new DBRef((int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Database.DefaultHome);
var startingQuota = (int)Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.StartingQuota;

// Create multiple guest characters using Mediator
await Services.GetRequiredService<IMediator>().Send(new CreatePlayerCommand("Guest3", "testpass", defaultHome, defaultHome, startingQuota));
await Services.GetRequiredService<IMediator>().Send(new CreatePlayerCommand("Guest4", "testpass", defaultHome, defaultHome, startingQuota));

// Get players and set Guest power on both
var player3 = await Services.GetRequiredService<IMediator>().CreateStream(new GetPlayerQuery("Guest3")).FirstOrDefaultAsync();
var player4 = await Services.GetRequiredService<IMediator>().CreateStream(new GetPlayerQuery("Guest4")).FirstOrDefaultAsync();
var guestPower = await Services.GetRequiredService<IMediator>().Send(new GetPowerQuery("Guest"));

await Services.GetRequiredService<IMediator>().Send(new SetObjectPowerCommand(new AnySharpObject(player3!), guestPower!));
await Services.GetRequiredService<IMediator>().Send(new SetObjectPowerCommand(new AnySharpObject(player4!), guestPower!));

await Task.Delay(200);

// Connect as guest - should connect to one of the available guests
var guestHandle = 1003L;
var result = await CommandParser.CommandParse(guestHandle, Services.GetRequiredService<IConnectionService>(), MModule.single("connect guest"));

// Should return a DBRef (not an error)
var resultMessage = result.Message?.ToString() ?? "";
await Assert.That(resultMessage.Contains("#-1")).IsFalse();

await NotifyService
.Received()
.Notify(Arg.Is<long>(h => h == guestHandle), 
Arg.Is<OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Connected")));

// Cleanup
await CommandParser.CommandParse(1, Services.GetRequiredService<IConnectionService>(), MModule.single($"@destroy Guest3"));
await CommandParser.CommandParse(1, Services.GetRequiredService<IConnectionService>(), MModule.single($"@destroy Guest4"));
}

[Test]
[Skip("Requires guest configuration testing infrastructure")]
public async ValueTask ConnectGuest_GuestsDisabled_FailsWithError()
{
// This test would require modifying the configuration to disable guests
// Skipping for now as it requires configuration testing infrastructure
await ValueTask.CompletedTask;
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
await ValueTask.CompletedTask;
}
}
