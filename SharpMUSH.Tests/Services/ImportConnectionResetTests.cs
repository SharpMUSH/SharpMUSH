using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Services;

public class ImportConnectionResetTests
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "import-reset-" + Guid.NewGuid().ToString("N"));
	[After(Test)] public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_root);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OnlyAnImportCommitDropsOutgoingTransportBindingsBeforeConsumersStart(bool imported)
	{
		var journal = new ImportPromotionJournal(Path.Combine(_root, "live"));
		if (imported)
		{
			var session = journal.Create();
			await CreateClosedWorldAsync(journal.LiveWorldPath);
			await CreateClosedWorldAsync(session.WorldPath);
			Directory.CreateDirectory(session.KeyRingPath);
			journal.MarkReady(session.Id);
			journal.RequestPromotion(session.Id);
			journal.ApplyPendingBeforeHostConstruction();
		}
		var states = Substitute.For<IConnectionStateStore>();
		states.GetAllConnectionsAsync(Arg.Any<CancellationToken>()).Returns(new[]
		{
			(91L, new ConnectionStateData { Handle = 91, PlayerObjid = "#1:old-world", State = "Connected",
				IpAddress = "127.0.0.1", Hostname = "localhost", ConnectionType = "telnet", ConnectedAt = DateTimeOffset.UtcNow })
		});
		var bus = Substitute.For<IMessageBus>();
		await new ImportConnectionResetService(journal, states, bus, NullLogger<ImportConnectionResetService>.Instance).StartingAsync(CancellationToken.None);
		if (imported)
		{
			await bus.Received(1).Publish(Arg.Is<DisconnectConnectionMessage>(message => message.Handle == 91), Arg.Any<CancellationToken>());
			await states.Received(1).RemoveConnectionAsync(91, Arg.Any<CancellationToken>());
			await Assert.That(journal.RequiresConnectionReset).IsFalse();
		}
		else await states.DidNotReceive().GetAllConnectionsAsync(Arg.Any<CancellationToken>());
	}
	private static async Task CreateClosedWorldAsync(string path)
	{
		await using var world = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await world.Migrate();
	}

}
