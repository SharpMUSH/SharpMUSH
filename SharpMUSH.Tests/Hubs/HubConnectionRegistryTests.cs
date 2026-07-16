using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="HubConnectionRegistry"/>.
/// </summary>
public class HubConnectionRegistryTests
{
	[Test]
	public async Task ConnectionsForAccount_WithMultipleConnectionsForSameAccount_ReturnsAllOfThem()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => { });
		registry.Add("conn-a2", "account-A", "2.2.2.2", () => { });
		registry.Add("conn-b1", "account-B", "3.3.3.3", () => { });

		var forA = registry.ConnectionsForAccount("account-A");

		await Assert.That(forA).IsEquivalentTo(new[] { "conn-a1", "conn-a2" });
	}

	[Test]
	public async Task ConnectionsForAccount_DoesNotIncludeOtherAccounts()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => { });
		registry.Add("conn-b1", "account-B", "3.3.3.3", () => { });

		var forB = registry.ConnectionsForAccount("account-B");

		await Assert.That(forB).IsEquivalentTo(new[] { "conn-b1" });
	}

	[Test]
	public async Task Remove_DropsOnlyTheRemovedConnection()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => { });
		registry.Add("conn-a2", "account-A", "2.2.2.2", () => { });

		registry.Remove("conn-a1");

		var forA = registry.ConnectionsForAccount("account-A");
		await Assert.That(forA).IsEquivalentTo(new[] { "conn-a2" });
	}

	[Test]
	public async Task ConnectionsForIp_FiltersByOriginIp()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "9.9.9.9", () => { });
		registry.Add("conn-a2", "account-A", "8.8.8.8", () => { });
		registry.Add("conn-b1", "account-B", "9.9.9.9", () => { });

		var forIp = registry.ConnectionsForIp("9.9.9.9");

		await Assert.That(forIp).IsEquivalentTo(new[] { "conn-a1", "conn-b1" });
	}

	[Test]
	public async Task ConnectionsForIp_AfterRemove_NoLongerIncludesRemovedConnection()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "9.9.9.9", () => { });
		registry.Add("conn-b1", "account-B", "9.9.9.9", () => { });

		registry.Remove("conn-b1");

		var forIp = registry.ConnectionsForIp("9.9.9.9");
		await Assert.That(forIp).IsEquivalentTo(new[] { "conn-a1" });
	}

	[Test]
	public async Task ConnectionsForAccount_UnknownAccount_ReturnsEmpty()
	{
		var registry = new HubConnectionRegistry();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => { });

		var forUnknown = registry.ConnectionsForAccount("no-such-account");

		await Assert.That(forUnknown).IsEmpty();
	}

	[Test]
	public async Task AbortConnectionsForAccount_InvokesStoredAbortDelegatesForThatAccountOnly()
	{
		var registry = new HubConnectionRegistry();
		var aborted = new List<string>();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => aborted.Add("conn-a1"));
		registry.Add("conn-a2", "account-A", "2.2.2.2", () => aborted.Add("conn-a2"));
		registry.Add("conn-b1", "account-B", "3.3.3.3", () => aborted.Add("conn-b1"));

		registry.AbortConnectionsForAccount("account-A");

		await Assert.That(aborted).IsEquivalentTo(new[] { "conn-a1", "conn-a2" });
	}

	[Test]
	public async Task AbortConnectionsForIp_InvokesStoredAbortDelegatesForThatIpOnly()
	{
		var registry = new HubConnectionRegistry();
		var aborted = new List<string>();
		registry.Add("conn-a1", "account-A", "9.9.9.9", () => aborted.Add("conn-a1"));
		registry.Add("conn-a2", "account-A", "8.8.8.8", () => aborted.Add("conn-a2"));
		registry.Add("conn-b1", "account-B", "9.9.9.9", () => aborted.Add("conn-b1"));

		registry.AbortConnectionsForIp("9.9.9.9");

		await Assert.That(aborted).IsEquivalentTo(new[] { "conn-a1", "conn-b1" });
	}

	[Test]
	public async Task AbortConnectionsForAccount_OneDelegateThrows_StillInvokesTheOthers()
	{
		// A race (e.g. a connection tearing itself down concurrently) can make Abort() throw for
		// one entry; that must not stop ban enforcement from aborting the rest of the account's
		// connections.
		var registry = new HubConnectionRegistry();
		var aborted = new List<string>();
		registry.Add("conn-a1", "account-A", "1.1.1.1", () => throw new InvalidOperationException("already gone"));
		registry.Add("conn-a2", "account-A", "2.2.2.2", () => aborted.Add("conn-a2"));
		registry.Add("conn-a3", "account-A", "3.3.3.3", () => aborted.Add("conn-a3"));

		registry.AbortConnectionsForAccount("account-A");

		await Assert.That(aborted).IsEquivalentTo(new[] { "conn-a2", "conn-a3" });
	}

	[Test]
	public async Task AbortConnectionsForIp_OneDelegateThrows_StillInvokesTheOthers()
	{
		var registry = new HubConnectionRegistry();
		var aborted = new List<string>();
		registry.Add("conn-a1", "account-A", "9.9.9.9", () => throw new InvalidOperationException("already gone"));
		registry.Add("conn-b1", "account-B", "9.9.9.9", () => aborted.Add("conn-b1"));

		registry.AbortConnectionsForIp("9.9.9.9");

		await Assert.That(aborted).IsEquivalentTo(new[] { "conn-b1" });
	}
}
