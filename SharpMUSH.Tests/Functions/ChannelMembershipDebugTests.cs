using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Deep debugging tests for channel membership operations.
/// Uses explicit AddUserToChannelCommand to track membership changes.
/// </summary>
public class ChannelMembershipDebugTests
{
private const string DebugChannelName = "DebugChannel";
private const string DebugChannelPrivilege = "Open";
private const int TestPlayerDbRef = 1;

[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public required ServerWebAppFactory WebAppFactoryArg { get; init; }

private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

[Test]
[NotInParallel]
public async Task DeepDebug_ChannelMembership_WithExplicitAddCommand()
{
Console.WriteLine("=== Starting Deep Debug Test (Using AddUserToChannelCommand) ===");

// Step 1: Get the test player
Console.WriteLine("\n--- Step 1: Getting test player ---");
var playerNode = await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef));
var player = playerNode.AsPlayer;
Console.WriteLine($"Using player DBRef: {TestPlayerDbRef}");
Console.WriteLine($"Player ID: {player.Id}");
Console.WriteLine($"Player Object ID: {player.Id}");

// Step 2: Create a channel WITHOUT auto-adding owner (we'll add explicitly)
// NOTE: CreateChannelCommand DOES automatically add the owner, so we'll track this
Console.WriteLine("\n--- Step 2: Creating channel ---");
await Mediator.Send(new CreateChannelCommand(
MModule.single(DebugChannelName),
[DebugChannelPrivilege],
player
));
Console.WriteLine($"Created channel: {DebugChannelName}");
Console.WriteLine("NOTE: CreateChannelCommand automatically adds owner as member");

// Step 3: Get the channel and inspect initial membership
Console.WriteLine("\n--- Step 3: Inspecting initial channel membership ---");
var channel = await Mediator.Send(new GetChannelQuery(DebugChannelName));
await Assert.That(channel).IsNotNull();
Console.WriteLine($"Channel ID: {channel!.Id}");

var initialMembers = await channel.Members.Value.ToListAsync();
Console.WriteLine($"Initial member count: {initialMembers.Count}");
foreach (var member in initialMembers)
{
Console.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
Console.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
Console.WriteLine($"  Member Id(): {member.Member.Id()}");
}
Console.WriteLine($"Expected: 1 member (the owner)");
await Assert.That(initialMembers.Count).IsEqualTo(1);
await Assert.That(initialMembers[0].Member.Id()).IsEqualTo(player.Id);

// Step 4: Check via GetMemberChannelsAsync
Console.WriteLine("\n--- Step 4: Checking player's channel list via GetMemberChannelsAsync ---");
var playerChannels = await Database.GetMemberChannelsAsync(player).ToListAsync();
Console.WriteLine($"Player is member of {playerChannels.Count} channel(s)");
var isInList = playerChannels.Any(c => c.Id == channel.Id);
Console.WriteLine($"Is player in channel list? {isInList}");
await Assert.That(isInList).IsTrue();

// Step 5: Remove the player from the channel
Console.WriteLine("\n--- Step 5: Removing player from channel via RemoveUserFromChannelCommand ---");
await Mediator.Send(new RemoveUserFromChannelCommand(channel, player));
Console.WriteLine("Remove command completed");

// Step 6: Re-fetch the channel and check membership
Console.WriteLine("\n--- Step 6: Re-fetching channel after removal ---");
var channelAfterRemove = await Mediator.Send(new GetChannelQuery(DebugChannelName));
await Assert.That(channelAfterRemove).IsNotNull();
Console.WriteLine($"Re-fetched channel ID: {channelAfterRemove!.Id}");
Console.WriteLine($"Same channel object? {ReferenceEquals(channel, channelAfterRemove)}");

var membersAfterRemove = await channelAfterRemove.Members.Value.ToListAsync();
Console.WriteLine($"Member count after remove: {membersAfterRemove.Count}");
foreach (var member in membersAfterRemove)
{
Console.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
Console.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
}

// Step 7: Check player's channel list after removal via GetMemberChannelsAsync
Console.WriteLine("\n--- Step 7: Checking player's channel list after removal ---");
var playerChannelsAfterRemove = await Database.GetMemberChannelsAsync(player).ToListAsync();
Console.WriteLine($"Player is member of {playerChannelsAfterRemove.Count} channel(s) after removal");
var isInListAfterRemove = playerChannelsAfterRemove.Any(c => c.Id == channel.Id);
Console.WriteLine($"Is player in channel list after removal? {isInListAfterRemove}");

// ASSERTION: Member should be removed
Console.WriteLine("\n--- Asserting: Member should be removed ---");
await Assert.That(membersAfterRemove.Count).IsEqualTo(0);
await Assert.That(isInListAfterRemove).IsFalse();

// Step 8: Add the player back explicitly using AddUserToChannelCommand
Console.WriteLine("\n--- Step 8: Adding player back via AddUserToChannelCommand ---");
await Mediator.Send(new AddUserToChannelCommand(channelAfterRemove, player));
Console.WriteLine("Add command completed");

// Step 9: Re-fetch and verify player is back
Console.WriteLine("\n--- Step 9: Re-fetching channel after re-adding ---");
var channelAfterAdd = await Mediator.Send(new GetChannelQuery(DebugChannelName));
await Assert.That(channelAfterAdd).IsNotNull();

var membersAfterAdd = await channelAfterAdd!.Members.Value.ToListAsync();
Console.WriteLine($"Member count after add: {membersAfterAdd.Count}");
foreach (var member in membersAfterAdd)
{
Console.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
Console.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
}

// Step 10: Check player's channel list after adding back
Console.WriteLine("\n--- Step 10: Checking player's channel list after re-adding ---");
var playerChannelsAfterAdd = await Database.GetMemberChannelsAsync(player).ToListAsync();
Console.WriteLine($"Player is member of {playerChannelsAfterAdd.Count} channel(s) after re-adding");
var isInListAfterAdd = playerChannelsAfterAdd.Any(c => c.Id == channel.Id);
Console.WriteLine($"Is player in channel list after re-adding? {isInListAfterAdd}");

// ASSERTION: Member should be added back
Console.WriteLine("\n--- Asserting: Member should be added back ---");
await Assert.That(membersAfterAdd.Count).IsEqualTo(1);
await Assert.That(membersAfterAdd[0].Member.Id()).IsEqualTo(player.Id);
await Assert.That(isInListAfterAdd).IsTrue();

// Cleanup
Console.WriteLine("\n--- Cleanup: Deleting channel ---");
await Mediator.Send(new DeleteChannelCommand(channelAfterAdd));
Console.WriteLine("=== Deep Debug Test Complete ===");
}
}
