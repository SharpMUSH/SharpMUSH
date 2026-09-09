using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Database;

public class SurrealRoleCancellationTests
{
	[Test]
	public async Task RoleLookupReceivesCallerCancellation()
	{
		var client = Substitute.For<ISurrealDbClient>();
		var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
			Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
		var response = new TaskCompletionSource<SurrealDb.Net.Models.Response.SurrealDbResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken forwarded = default;
		CancellationTokenRegistration registration = default;
		client.RawQuery(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>?>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			forwarded = call.ArgAt<CancellationToken>(2);
			registration = forwarded.Register(() => response.TrySetCanceled(forwarded));
			return response.Task;
		});
		using var cancellation = new CancellationTokenSource();
		var write = database.GetRoleAsync("wizard", cancellation.Token);
		cancellation.Cancel();
		var cancelled = false;
		try { await write; }
		catch (OperationCanceledException) { cancelled = true; }
		finally { registration.Dispose(); }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(forwarded.IsCancellationRequested).IsTrue();
	}

}
