using Microsoft.Extensions.DependencyInjection;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

public class GuestDebugTests
{
[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
public required WebAppFactory WebAppFactoryArg { get; init; }

private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

[Test]
public async ValueTask DebugGuestCreation()
{
// Create a guest character
var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@pcreate DebugGuest=testpass"));
Console.WriteLine($"Create result: '{createResult.Message}'");

await Task.Delay(500);

// Check if player exists
var player = await Mediator.CreateStream(new GetPlayerQuery("DebugGuest")).FirstOrDefaultAsync();
Console.WriteLine($"Player found: {player != null}");
if (player != null)
{
Console.WriteLine($"Player name: {player.Object.Name}");
Console.WriteLine($"Player DBRef: #{player.Object.Key}");

// Check current powers
var currentPowers = await player.Object.Powers.Value.ToListAsync();
Console.WriteLine($"Current powers count: {currentPowers.Count}");
foreach (var p in currentPowers)
{
Console.WriteLine($"  Power: {p.Name}");
}
}

// Try to set Guest power using powers() function
Console.WriteLine("Attempting to set Guest power...");
var powerResult = await Parser.CommandParse(1, ConnectionService, MModule.single("think [powers(DebugGuest, Guest)]"));
Console.WriteLine($"Power result: '{powerResult.Message}'");

await Task.Delay(500);

// Check powers again
player = await Mediator.CreateStream(new GetPlayerQuery("DebugGuest")).FirstOrDefaultAsync();
if (player != null)
{
var currentPowers = await player.Object.Powers.Value.ToListAsync();
Console.WriteLine($"Powers after setting: {currentPowers.Count}");
foreach (var p in currentPowers)
{
Console.WriteLine($"  Power: {p.Name}");
}

// Check if Guest power exists in database
var guestPower = await Mediator.Send(new GetPowerQuery("GUEST"));
Console.WriteLine($"Guest power exists in database: {guestPower != null}");
if (guestPower != null)
{
Console.WriteLine($"  Name: {guestPower.Name}");
Console.WriteLine($"  Alias: {guestPower.Alias}");
}
}

// Try to connect
var connectResult = await Parser.CommandParse(2000L, ConnectionService, MModule.single("connect guest"));
Console.WriteLine($"Connect result: '{connectResult.Message}'");

// Cleanup
await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy DebugGuest"));
}
}
