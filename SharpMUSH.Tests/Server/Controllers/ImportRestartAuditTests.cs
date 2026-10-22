using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services.Imports;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Server.Controllers;

public class ImportRestartAuditTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DurableIntentStillAnswersAcceptedAndRestartsWhenAuditThrows(bool rollback)
	{
		var root = Path.Combine(Path.GetTempPath(), "import-audit-" + Guid.NewGuid().ToString("N"));
		try
		{
			var journal = new ImportPromotionJournal(Path.Combine(root, "live"));
			var session = journal.Create();
			await CreateClosedWorldAsync(session.WorldPath);
			Directory.CreateDirectory(session.KeyRingPath);
			journal.MarkReady(session.Id);
			if (rollback)
			{
				await CreateClosedWorldAsync(journal.LiveWorldPath);
				journal.RequestPromotion(session.Id);
				journal.ApplyPendingBeforeHostConstruction();
			}
			var audit = Substitute.For<IAuditLog>();
			audit.RecordPortalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AuditTarget?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
				.Returns(ValueTask.FromException(new InvalidOperationException("Audit provider unavailable")));
			var lifetime = Substitute.For<IHostApplicationLifetime>();
			var feature = new CompletionFeature();
			var http = new DefaultHttpContext();
			http.Features.Set<IHttpResponseFeature>(feature);
			var controller = new DatabaseConversionController(null!, journal, null!, Substitute.For<IStorageCapacityService>(),
				audit, lifetime, NullLogger<DatabaseConversionController>.Instance)
			{ ControllerContext = new ControllerContext { HttpContext = http } };
			var confirmation = new DatabaseConversionController.RestartConfirmation(true);
			var result = rollback ? await controller.Rollback(session.Id, confirmation, CancellationToken.None)
				: await controller.Promote(session.Id, confirmation, CancellationToken.None);
			await Assert.That(result is AcceptedResult).IsTrue();
			await Assert.That(journal.Get(session.Id).State).IsEqualTo(rollback ? ImportSessionState.RollbackRequested : ImportSessionState.PromotionRequested);
			lifetime.DidNotReceive().StopApplication();
			await feature.CompleteAsync();
			lifetime.Received(1).StopApplication();
		}
		finally { await FixtureDirectoryCleanup.DeleteAsync(root); }
	}

	private sealed class CompletionFeature : HttpResponseFeature
	{
		private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];
		public override void OnCompleted(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));
		public async Task CompleteAsync()
		{
			foreach (var (callback, state) in _callbacks) await callback(state);
		}
	}
	private static async Task CreateClosedWorldAsync(string path)
	{
		await using var world = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await world.Migrate();
	}

}
