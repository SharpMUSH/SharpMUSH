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
public async ValueTask ConnectGuest_NoGuestCharacters_FailsWithError()
{
// Don't create any guest characters - test the error case
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
[Skip("Requires @pcreate and @power commands to work in test environment")]
[DependsOn(nameof(ConnectGuest_NoGuestCharacters_FailsWithError))]
public async ValueTask ConnectGuest_BasicLogin_Succeeds()
{
// This test requires @pcreate and @power to successfully create and configure guest characters
// Currently these commands don't persist properly in the test database
await ValueTask.CompletedTask;
}

[Test]
[Skip("Requires @pcreate and @power commands to work in test environment")]
[DependsOn(nameof(ConnectGuest_BasicLogin_Succeeds))]
public async ValueTask ConnectGuest_CaseInsensitive_Succeeds()
{
// This test requires @pcreate and @power to successfully create and configure guest characters
// Currently these commands don't persist properly in the test database
await ValueTask.CompletedTask;
}

[Test]
[Skip("Requires @pcreate and @power commands to work in test environment")]
[DependsOn(nameof(ConnectGuest_CaseInsensitive_Succeeds))]
public async ValueTask ConnectGuest_MultipleGuests_SelectsAppropriateOne()
{
// This test requires @pcreate and @power to successfully create and configure guest characters
// Currently these commands don't persist properly in the test database
await ValueTask.CompletedTask;
}

[Test]
[Skip("Requires guest configuration testing infrastructure")]
[DependsOn(nameof(ConnectGuest_MultipleGuests_SelectsAppropriateOne))]
public async ValueTask ConnectGuest_GuestsDisabled_FailsWithError()
{
// This test would require modifying the configuration to disable guests
// Skipping for now as it requires configuration testing infrastructure
await ValueTask.CompletedTask;
}

[Test]
[Skip("Requires advanced connection management")]
[DependsOn(nameof(ConnectGuest_GuestsDisabled_FailsWithError))]
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
