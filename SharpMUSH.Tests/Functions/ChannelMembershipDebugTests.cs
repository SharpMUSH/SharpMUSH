using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

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
		TestDiagnostics.WriteLine("=== Starting Deep Debug Test (Using AddUserToChannelCommand) ===");

		TestDiagnostics.WriteLine("\n--- Step 1: Getting test player ---");
		var playerNode = await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef));
		var player = playerNode.AsPlayer;
		TestDiagnostics.WriteLine($"Using player DBRef: {TestPlayerDbRef}");
		TestDiagnostics.WriteLine($"Player ID: {player.Id}");
		TestDiagnostics.WriteLine($"Player Object ID: {player.Id}");

		// NOTE: CreateChannelCommand DOES automatically add the owner, so we'll track this
		TestDiagnostics.WriteLine("\n--- Step 2: Creating channel ---");
		await Mediator.Send(new CreateChannelCommand(
		MarkupText.Plain(DebugChannelName),
		[DebugChannelPrivilege],
		player
		));
		TestDiagnostics.WriteLine($"Created channel: {DebugChannelName}");
		TestDiagnostics.WriteLine("NOTE: CreateChannelCommand automatically adds owner as member");

		TestDiagnostics.WriteLine("\n--- Step 3: Inspecting initial channel membership ---");
		var channel = await Mediator.Send(new GetChannelQuery(DebugChannelName));
		await Assert.That(channel).IsNotNull();
		TestDiagnostics.WriteLine($"Channel ID: {channel!.Id}");

		var initialMembers = await channel.Members.Value.ToListAsync();
		TestDiagnostics.WriteLine($"Initial member count: {initialMembers.Count}");
		foreach (var member in initialMembers)
		{
			TestDiagnostics.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
			TestDiagnostics.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
			TestDiagnostics.WriteLine($"  Member Id(): {member.Member.Id()}");
		}
		TestDiagnostics.WriteLine($"Expected: 1 member (the owner)");
		await Assert.That(initialMembers.Count).IsEqualTo(1);
		await Assert.That(initialMembers[0].Member.Id()).IsEqualTo(player.Id);

		TestDiagnostics.WriteLine("\n--- Step 4: Checking player's channel list via GetMemberChannelsAsync ---");
		var playerChannels = await Database.GetMemberChannelsAsync(player).ToListAsync();
		TestDiagnostics.WriteLine($"Player is member of {playerChannels.Count} channel(s)");
		var isInList = playerChannels.Any(c => c.Id == channel.Id);
		TestDiagnostics.WriteLine($"Is player in channel list? {isInList}");
		await Assert.That(isInList).IsTrue();

		TestDiagnostics.WriteLine("\n--- Step 5: Removing player from channel via RemoveUserFromChannelCommand ---");
		await Mediator.Send(new RemoveUserFromChannelCommand(channel, player));
		TestDiagnostics.WriteLine("Remove command completed");

		TestDiagnostics.WriteLine("\n--- Step 6: Re-fetching channel after removal ---");
		var channelAfterRemove = await Mediator.Send(new GetChannelQuery(DebugChannelName));
		await Assert.That(channelAfterRemove).IsNotNull();
		TestDiagnostics.WriteLine($"Re-fetched channel ID: {channelAfterRemove!.Id}");
		TestDiagnostics.WriteLine($"Same channel object? {ReferenceEquals(channel, channelAfterRemove)}");

		var membersAfterRemove = await channelAfterRemove.Members.Value.ToListAsync();
		TestDiagnostics.WriteLine($"Member count after remove: {membersAfterRemove.Count}");
		foreach (var member in membersAfterRemove)
		{
			TestDiagnostics.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
			TestDiagnostics.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
		}

		TestDiagnostics.WriteLine("\n--- Step 7: Checking player's channel list after removal ---");
		var playerChannelsAfterRemove = await Database.GetMemberChannelsAsync(player).ToListAsync();
		TestDiagnostics.WriteLine($"Player is member of {playerChannelsAfterRemove.Count} channel(s) after removal");
		var isInListAfterRemove = playerChannelsAfterRemove.Any(c => c.Id == channel.Id);
		TestDiagnostics.WriteLine($"Is player in channel list after removal? {isInListAfterRemove}");

		TestDiagnostics.WriteLine("\n--- Asserting: Member should be removed ---");
		await Assert.That(membersAfterRemove.Count).IsEqualTo(0);
		await Assert.That(isInListAfterRemove).IsFalse();

		TestDiagnostics.WriteLine("\n--- Step 8: Adding player back via AddUserToChannelCommand ---");
		await Mediator.Send(new AddUserToChannelCommand(channelAfterRemove, player));
		TestDiagnostics.WriteLine("Add command completed");

		TestDiagnostics.WriteLine("\n--- Step 9: Re-fetching channel after re-adding ---");
		var channelAfterAdd = await Mediator.Send(new GetChannelQuery(DebugChannelName));
		await Assert.That(channelAfterAdd).IsNotNull();

		var membersAfterAdd = await channelAfterAdd!.Members.Value.ToListAsync();
		TestDiagnostics.WriteLine($"Member count after add: {membersAfterAdd.Count}");
		foreach (var member in membersAfterAdd)
		{
			TestDiagnostics.WriteLine($"  Member Object ID: {member.Member.Object().Id}");
			TestDiagnostics.WriteLine($"  Member DBRef: {member.Member.Object().DBRef}");
		}

		TestDiagnostics.WriteLine("\n--- Step 10: Checking player's channel list after re-adding ---");
		var playerChannelsAfterAdd = await Database.GetMemberChannelsAsync(player).ToListAsync();
		TestDiagnostics.WriteLine($"Player is member of {playerChannelsAfterAdd.Count} channel(s) after re-adding");
		var isInListAfterAdd = playerChannelsAfterAdd.Any(c => c.Id == channel.Id);
		TestDiagnostics.WriteLine($"Is player in channel list after re-adding? {isInListAfterAdd}");

		TestDiagnostics.WriteLine("\n--- Asserting: Member should be added back ---");
		await Assert.That(membersAfterAdd.Count).IsEqualTo(1);
		await Assert.That(membersAfterAdd[0].Member.Id()).IsEqualTo(player.Id);
		await Assert.That(isInListAfterAdd).IsTrue();

		TestDiagnostics.WriteLine("\n--- Cleanup: Deleting channel ---");
		await Mediator.Send(new DeleteChannelCommand(channelAfterAdd));
		TestDiagnostics.WriteLine("=== Deep Debug Test Complete ===");
	}
}
