using System.Text;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The chatdb the same PennMUSH 1.8.8 game wrote beside <see cref="PennMUSHDbrefPreservationTests"/>'s dump:
/// <c>Public</c> (One's; Player; buffer 10, mogrified by #6 Widget; Alice, Bob, Carol and One, two of them
/// titled), <c>Secret</c> (Alice's; join and speak locked to Alice or Bob; Alice, Bob and One) and
/// <c>Staff</c> (One's; Player and Wizard; One).
/// </summary>
public class PennMUSHChannelImportTests
{
	private static readonly string ChatFixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0-holes.chatdb");

	[Test]
	public async Task TheChatdbIsRead()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var chat = await world.Parser.ParseChatFileAsync(ChatFixturePath);

		await Assert.That(chat.Flags).IsEqualTo(PennMUSHChatDatabase.SpiffyFlag);
		await Assert.That(chat.SavedTime).IsEqualTo("Thu Sep 24 17:04:06 2026");
		await Assert.That(string.Join(",", chat.Channels.Select(c => c.Name))).IsEqualTo("Public,Secret,Staff");

		var secret = chat.Channels[1];
		await Assert.That(secret.Creator).IsEqualTo(3);
		await Assert.That(secret.Mogrifier).IsEqualTo(-1);
		await Assert.That(secret.Locks["join"]).IsEqualTo("#3|#4");
		await Assert.That(secret.Locks["modify"]).IsEqualTo("=#1");
		await Assert.That(string.Join(" ", secret.Users.Select(u => $"#{u.DBRef}:{u.Title}"))).IsEqualTo("#3:Founder #4: #1:");
		await Assert.That(chat.Channels[2].Flags).IsEqualTo(33);
	}

	/// <summary>
	/// Through the path the upload endpoint takes: the dump and the chatdb together. Every channel keeps its
	/// owner, privileges, description, locks, buffer, mogrifier and members, under the dbrefs the dump kept.
	/// </summary>
	[Test]
	public async Task EveryChannelArrivesAsPennMUSHHadIt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var progress = new RecordingProgress();
		var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath, null,
			ChatFixturePath, progress);

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ChannelsConverted).IsEqualTo(3);
		await Assert.That(result.ChannelMembersConverted).IsEqualTo(8);
		// Only the creation cost, which SharpMUSH has nowhere to keep, is reported; nothing else was changed.
		await Assert.That(result.Warnings.Where(w => w.StartsWith("Channel"))).IsEquivalentTo(
			["Channel creation costs were not kept, as SharpMUSH does not refund them: Public (1000), Staff (1000)"]);
		await Assert.That(result.Warnings).DoesNotContain(w => w.StartsWith("The chatdb was saved at"));

		var phases = progress.Reports.Select(p => p.CurrentPhase).ToList();
		await Assert.That(phases.IndexOf("Channels imported")).IsGreaterThan(phases.IndexOf("Locks created"));
		await Assert.That(progress.Reports[^1].CurrentPhase).IsEqualTo("Complete");
		await Assert.That(progress.Reports.Count(p => p.PercentageComplete >= 100)).IsEqualTo(1);

		var widget = await PennMUSHDbrefPreservationTests.NodeAsync(world, 6);

		var @public = await ChannelAsync(world, "Public");
		await Assert.That(await DescribeAsync(@public)).IsEqualTo("Public|Everyone talks here|#1|Player|10");
		await Assert.That(Locks(@public)).IsEqualTo("||=#1||");
		await Assert.That(@public.Mogrifier).IsEqualTo(widget.Object().DBRef.ToString());
		await Assert.That(await MembersAsync(@public)).IsEqualTo("#1: #3:The Oracle #4:Bobster #5:");

		var secret = await ChannelAsync(world, "Secret");
		await Assert.That(await DescribeAsync(secret)).IsEqualTo("Secret|Hidden chatter|#3|Player|0");
		await Assert.That(Locks(secret)).IsEqualTo("#3|#4|#3|#4|=#1||");
		await Assert.That(secret.Mogrifier).IsEqualTo(string.Empty);
		await Assert.That(await MembersAsync(secret)).IsEqualTo("#1: #3:Founder #4:");

		var staff = await ChannelAsync(world, "Staff");
		await Assert.That(await DescribeAsync(staff)).IsEqualTo("Staff||#1|Player Wizard|0");
		await Assert.That(await MembersAsync(staff)).IsEqualTo("#1:");

		// The locks are live: Secret's join lock lets Alice and Bob in and keeps Carol out.
		await Assert.That(await world.Permissions.ChannelCanJoin(await PennMUSHDbrefPreservationTests.NodeAsync(world, 4), secret)).IsTrue();
		await Assert.That(await world.Permissions.ChannelCanJoin(await PennMUSHDbrefPreservationTests.NodeAsync(world, 5), secret)).IsFalse();
		await Assert.That(await world.Permissions.ChannelCanJoin(await PennMUSHDbrefPreservationTests.NodeAsync(world, 5), @public)).IsTrue();
		// Its speak lock too.
		await Assert.That(await world.Permissions.ChannelCanSpeak(await PennMUSHDbrefPreservationTests.NodeAsync(world, 4), secret)).IsTrue();
		await Assert.That(await world.Permissions.ChannelCanSpeak(await PennMUSHDbrefPreservationTests.NodeAsync(world, 5), secret)).IsFalse();

		// A member finds the channel from its side too.
		var carolsChannels = await world.Mediator
			.CreateStream(new GetOnChannelQuery(await PennMUSHDbrefPreservationTests.NodeAsync(world, 5)))
			.Select(c => c.Name.ToPlainText()).ToListAsync();
		await Assert.That(carolsChannels).IsEquivalentTo(["Public"]);
	}

	/// <summary>
	/// What load_labeled_channel and load_labeled_chanusers change on load, and what SharpMUSH cannot keep, is
	/// done and reported. The owner here is #7, a hole in the dump; #6 is the Widget thing.
	/// </summary>
	[Test]
	public async Task WhatDoesNotComeAcrossAsItWasIsReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath);
		const string chatdb = """
			+V1
			savedtime "Fri Sep 25 01:00:00 2026"
			channels 2
			 name "Broken"
			  description ""
			  flags 4097
			  creator #7
			  cost 0
			  buffer 0
			  mogrifier #7
			  lock "join"
			  key "*UNLOCKED*"
			  lock "speak"
			  key "#3"
			  users 3
			   dbref #3
			    flags 15
			    title "Hi"
			   dbref #6
			    flags 0
			    title ""
			   dbref #7
			    flags 0
			    title ""
			 name "Things"
			  description "For objects"
			  flags 2
			  creator #3
			  cost 0
			  buffer 0
			  mogrifier #-1
			  users 2
			   dbref #6
			    flags 1
			    title "Gadget"
			   dbref #4
			    flags 0
			    title ""
			***END OF DUMP***

			""";
		database.Chat = await world.Parser.ParseChatAsync(new MemoryStream(Encoding.UTF8.GetBytes(chatdb)));

		var result = await world.Converter.ConvertDatabaseAsync(database);

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ChannelsConverted).IsEqualTo(2);
		await Assert.That(result.ChannelMembersConverted).IsEqualTo(2);
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("The chatdb was saved at Fri Sep 25 01:00:00 2026"));
		await Assert.That(result.Warnings).Contains("Channel Broken: owner #7 is not an imported player; given to #1");
		await Assert.That(result.Warnings).Contains("Channel Broken: unknown channel flag bits 0x1000 were dropped");
		await Assert.That(result.Warnings).Contains("Channel Broken: mogrifier #7 is not an imported object and was dropped");
		await Assert.That(result.Warnings).Contains("Channel Broken: dropped member(s) that are not an imported player (#6 #7)");
		await Assert.That(result.Warnings).Contains("Channel Things: dropped member(s) that are not an imported thing (#4)");
		await Assert.That(result.Warnings).DoesNotContain(w => w.StartsWith("Channel creation costs"));

		var broken = await ChannelAsync(world, "Broken");
		await Assert.That(await DescribeAsync(broken)).IsEqualTo("Broken||#1|Player|0");
		await Assert.That(Locks(broken)).IsEqualTo("|#3|||");
		await Assert.That(broken.Mogrifier).IsEqualTo(string.Empty);
		// The owner it was given to was never on it, so it is not made a member.
		await Assert.That(await MembersAsync(broken)).IsEqualTo("#3:Hi");
		var alice = (await broken.Members.Value.ToListAsync()).Single().Status;
		// Quiet, Hide and Combine are kept; Gag is cleared, as PennMUSH clears it on any load but a reboot.
		await Assert.That(alice is { Mute: true, Hide: true, Combine: true, Gagged: false }).IsTrue();

		var things = await ChannelAsync(world, "Things");
		await Assert.That(await DescribeAsync(things)).IsEqualTo("Things|For objects|#3|Object|0");
		await Assert.That(await MembersAsync(things)).IsEqualTo("#6:Gadget");
	}

	/// <summary>
	/// A source #2 that is a thing cannot become the Master Room and is imported as #11. A channel lock that
	/// names it follows it there, so it still gates on the Box and not on the Master Room; a number that is
	/// an attribute value, not a reference, is left alone.
	/// </summary>
	[Test]
	public async Task AChannelLockFollowsAnObjectImportedUnderANewNumber()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = new PennMUSHDatabase
		{
			Version = "Test Version",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
				new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
				new PennMUSHObject { DBRef = 2, Name = "Box", Type = PennMUSHObjectType.Thing },
				new PennMUSHObject { DBRef = 10, Name = "Ten", Type = PennMUSHObjectType.Thing }
			]
		};
		const string chatdb = """
			+V1
			savedtime "x"
			channels 1
			 name "Boxed"
			  description ""
			  flags 3
			  creator #1
			  cost 0
			  buffer 0
			  mogrifier #2
			  lock "join"
			  key "=#2|+#2|#10|NUM:#2|@#2/Basic"
			  lock "speak"
			  key "#1"
			  users 2
			   dbref #1
			    flags 0
			    title ""
			   dbref #2
			    flags 0
			    title ""
			***END OF DUMP***

			""";
		database.Chat = await world.Parser.ParseChatAsync(new MemoryStream(Encoding.UTF8.GetBytes(chatdb)));

		var result = await world.Converter.ConvertDatabaseAsync(database);

		await Assert.That(result.Errors).IsEmpty();
		var boxed = await ChannelAsync(world, "Boxed");
		await Assert.That(boxed.JoinLock).IsEqualTo("=#11|+#11|#10|NUM:#2|@#11/Basic");
		await Assert.That(boxed.SpeakLock).IsEqualTo("#1");
		await Assert.That(result.Warnings).Contains(w =>
			w.StartsWith("Channel Boxed: join lock names objects imported under new numbers (#2 as #11, #2 as #11, #2 as #11)"));
		await Assert.That(result.Warnings).DoesNotContain(w => w.StartsWith("Channel Boxed: speak lock"));
		// The mogrifier and the member follow it the same way.
		var box = await PennMUSHDbrefPreservationTests.NodeAsync(world, 11);
		await Assert.That(box.Object().Name).IsEqualTo("Box");
		await Assert.That(boxed.Mogrifier).IsEqualTo(box.Object().DBRef.ToString());
		await Assert.That(await MembersAsync(boxed)).IsEqualTo("#1: #11:");
	}

	/// <summary>A dump imported without its chatdb imports no channels and says nothing about them.</summary>
	[Test]
	public async Task WithoutAChatdbThereAreNoChannels()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(
			await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath));

		await Assert.That(result.ChannelsConverted).IsEqualTo(0);
		await Assert.That(result.Warnings).DoesNotContain(w => w.Contains("chatdb"));
		await Assert.That(await world.Mediator.CreateStream(new GetChannelListQuery()).CountAsync()).IsEqualTo(0);
	}

	/// <summary>The chatdb is optional: one that cannot be read costs its channels, not the world, and says so.</summary>
	[Test]
	public async Task AnUnreadableChatdbStillImportsTheWorld()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var badChatdb = Path.Join(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.chatdb");
		// A channel cut off after its name.
		await File.WriteAllTextAsync(badChatdb, "+V1\nsavedtime \"x\"\nchannels 1\n name \"Public\"\n");

		try
		{
			var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath, null,
				badChatdb, new Progress<ConversionProgress>());

			await Assert.That(result.Errors).IsEmpty();
			await Assert.That(result.PlayersConverted).IsGreaterThan(0);
			await Assert.That(result.ChannelsConverted).IsEqualTo(0);
			await Assert.That(result.Warnings).Contains(w => w.StartsWith("The chatdb could not be read"));
			await Assert.That((await PennMUSHDbrefPreservationTests.NodeAsync(world, 3)).Object().Name).IsEqualTo("Alice");
		}
		finally
		{
			File.Delete(badChatdb);
		}
	}

	/// <summary>
	/// An empty chatdb (an admin picked a zero-byte file) and one that cannot be opened are reported the
	/// same way as a malformed one, and the world still imports.
	/// </summary>
	[Test]
	public async Task AnEmptyOrMissingChatdbIsReportedAndTheWorldStillImports()
	{
		var emptyChatdb = Path.Join(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.chatdb");
		await File.WriteAllTextAsync(emptyChatdb, string.Empty);
		var missingChatdb = Path.Join(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.chatdb");

		try
		{
			foreach (var chatdb in new[] { emptyChatdb, missingChatdb })
			{
				await using var world = await IsolatedImportWorld.CreateAsync();
				var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath, null,
					chatdb, new Progress<ConversionProgress>());

				await Assert.That(result.Errors).IsEmpty();
				await Assert.That(result.ChannelsConverted).IsEqualTo(0);
				await Assert.That(result.Warnings).Contains(w => w.StartsWith("The chatdb could not be read"));
				await Assert.That((await PennMUSHDbrefPreservationTests.NodeAsync(world, 3)).Object().Name).IsEqualTo("Alice");
			}
		}
		finally
		{
			File.Delete(emptyChatdb);
		}
	}

	private static async Task<SharpChannel> ChannelAsync(IsolatedImportWorld world, string name)
		=> await world.Mediator.Send(new GetChannelQuery(name))
			?? throw new InvalidOperationException($"No channel {name}");

	private static async Task<string> DescribeAsync(SharpChannel channel)
	{
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		return $"{channel.Name.ToPlainText()}|{channel.Description.ToPlainText()}|#{owner.Object.DBRef.Number}|" +
			$"{string.Join(" ", channel.Privs)}|{channel.Buffer}";
	}

	/// <summary>join, speak, modify, see and hide, in the order the chatdb writes them.</summary>
	private static string Locks(SharpChannel channel)
		=> string.Join("|", channel.JoinLock, channel.SpeakLock, channel.ModLock, channel.SeeLock, channel.HideLock);

	private static async Task<string> MembersAsync(SharpChannel channel)
		=> string.Join(" ", (await channel.Members.Value.ToListAsync())
			.OrderBy(m => m.Member.Object().DBRef.Number)
			.Select(m => $"#{m.Member.Object().DBRef.Number}:{m.Status.Title?.ToPlainText()}"));

	/// <summary>Records each report as it is made; <see cref="Progress{T}"/> posts them, so their order is lost.</summary>
	private sealed class RecordingProgress : IProgress<ConversionProgress>
	{
		public List<ConversionProgress> Reports { get; } = [];

		public void Report(ConversionProgress value) => Reports.Add(value);
	}
}
