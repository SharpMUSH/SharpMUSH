using System.Collections.Concurrent;
using System.Text;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="IConnectionService.ConnectionData"/>'s derived properties read straight out of
/// <c>Metadata</c>, and <c>ConnectionService.Register</c> seeds its defaults with
/// <c>metaData ?? new …</c> — a caller that supplies any dictionary supplies all of it. A caller
/// that supplies a partial one therefore produces a live connection whose <c>Idle</c> and
/// <c>Connected</c> threw <see cref="KeyNotFoundException"/> on read, which is a property nothing
/// reading a nullable <see cref="TimeSpan"/> expects to be able to do.
/// </summary>
public class ConnectionDataMetadataTests
{
	private static IConnectionService.ConnectionData WithMetadata(params (string Key, string Value)[] entries)
		=> new(
			Handle: 1,
			Ref: new DBRef(1),
			State: IConnectionService.ConnectionState.LoggedIn,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			Encoding: () => Encoding.UTF8,
			Metadata: new ConcurrentDictionary<string, string>(
				entries.ToDictionary(entry => entry.Key, entry => entry.Value)));

	[Test]
	public async Task IdleAndConnectedAreUnknownRatherThanFatalWhenTheirTimestampsAreMissing()
	{
		var connection = WithMetadata(("PUEBLO", "1"));

		await Assert.That(connection.Idle).IsNull();
		await Assert.That(connection.Connected).IsNull();
	}

	[Test]
	public async Task IdleAndConnectedAreStillMeasuredWhenTheirTimestampsArePresent()
	{
		var when = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds().ToString();
		var connection = WithMetadata(("LastConnectionSignal", when), ("ConnectionStartTime", when));

		await Assert.That(connection.Idle!.Value.TotalMinutes).IsGreaterThanOrEqualTo(4.5);
		await Assert.That(connection.Connected!.Value.TotalMinutes).IsGreaterThanOrEqualTo(4.5);
	}

	/// <summary>An unparseable timestamp is no more readable than an absent one.</summary>
	[Test]
	public async Task AnUnparseableTimestampIsUnknownRatherThanFatal()
	{
		var connection = WithMetadata(("LastConnectionSignal", "not-a-number"));

		await Assert.That(connection.Idle).IsNull();
	}
}
