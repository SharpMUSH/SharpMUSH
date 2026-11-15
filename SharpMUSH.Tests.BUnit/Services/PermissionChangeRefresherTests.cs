using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// A tab reads its permissions again when the server says a role change reached its account, or when its hub
/// comes back from a gap in which that notice could have been lost; a burst of notices costs at most two reads.
/// </summary>
public class PermissionChangeRefresherTests
{
	private sealed class SingleHubFactory : IGameHubConnectionFactory
	{
		public FakeGameHubConnection Hub { get; } = new();
		public IGameHubConnection Create() => Hub;
		public IGameHubConnection? CreateScene() => null;
	}

	private static async Task<(SingleHubFactory Factory, ConnectionStateService Connection)> ConnectedAsync()
	{
		var factory = new SingleHubFactory();
		var connection = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await connection.ConnectAsync();
		return (factory, connection);
	}

	[Test]
	public async Task APermissionsNoticeReloadsTheTabsAuthority()
	{
		var (factory, connection) = await ConnectedAsync();
		var reloads = 0;
		using var refresher = new PermissionChangeRefresher(connection, () =>
		{
			reloads++;
			return Task.CompletedTask;
		}, NullLogger<PermissionChangeRefresher>.Instance);

		factory.Hub.Raise("ReceivePermissionsChanged");
		await refresher.Idle;

		await Assert.That(reloads).IsEqualTo(1);
	}

	[Test]
	public async Task AReconnectReloadsTheTabsAuthority()
	{
		var (factory, connection) = await ConnectedAsync();
		var reloads = 0;
		using var refresher = new PermissionChangeRefresher(connection, () =>
		{
			reloads++;
			return Task.CompletedTask;
		}, NullLogger<PermissionChangeRefresher>.Instance);

		await factory.Hub.RaiseReconnectedAsync();
		await refresher.Idle;

		await Assert.That(reloads).IsEqualTo(1);
	}

	[Test]
	public async Task NoticesDuringAReloadFoldIntoOneMore()
	{
		var (factory, connection) = await ConnectedAsync();
		var reloads = 0;
		var first = new TaskCompletionSource();
		using var refresher = new PermissionChangeRefresher(connection, () =>
		{
			reloads++;
			return reloads == 1 ? first.Task : Task.CompletedTask;
		}, NullLogger<PermissionChangeRefresher>.Instance);

		factory.Hub.Raise("ReceivePermissionsChanged");
		var running = refresher.Idle;
		factory.Hub.Raise("ReceivePermissionsChanged");
		factory.Hub.Raise("ReceivePermissionsChanged");
		factory.Hub.Raise("ReceivePermissionsChanged");
		first.SetResult();
		await running;

		await Assert.That(reloads).IsEqualTo(2);
	}

	[Test]
	public async Task AFailedReloadDoesNotStopTheNext()
	{
		var (factory, connection) = await ConnectedAsync();
		var reloads = 0;
		using var refresher = new PermissionChangeRefresher(connection, () =>
		{
			reloads++;
			return reloads == 1 ? Task.FromException(new HttpRequestException("offline")) : Task.CompletedTask;
		}, NullLogger<PermissionChangeRefresher>.Instance);

		factory.Hub.Raise("ReceivePermissionsChanged");
		await refresher.Idle;
		factory.Hub.Raise("ReceivePermissionsChanged");
		await refresher.Idle;

		await Assert.That(reloads).IsEqualTo(2);
	}

	[Test]
	public async Task ADisposedRefresherStopsListening()
	{
		var (factory, connection) = await ConnectedAsync();
		var reloads = 0;
		var refresher = new PermissionChangeRefresher(connection, () =>
		{
			reloads++;
			return Task.CompletedTask;
		}, NullLogger<PermissionChangeRefresher>.Instance);
		refresher.Dispose();

		factory.Hub.Raise("ReceivePermissionsChanged");
		await refresher.Idle;

		await Assert.That(reloads).IsEqualTo(0);
	}
}
