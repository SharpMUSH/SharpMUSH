using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

internal static class ReplayStoreTestExtensions
{
	/// <summary>Every frame after <paramref name="lastSeq"/>, collected; an incomplete history reads as empty.</summary>
	public static async Task<IReadOnlyList<byte[]>> AfterAsync(this ITerminalReplayStore store, string session, long lastSeq,
		CancellationToken ct = default)
	{
		if (await store.OpenAsync(session, lastSeq, ct) is not ReplayFrames frames) return [];
		await using (frames)
			return await frames.ReadAsync(ct).ToListAsync(ct);
	}
}
