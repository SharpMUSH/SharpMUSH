using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A substituted <see cref="ISharpDatabase"/> holding exactly one session, counting the writes a
/// <see cref="Library.Services.DatabaseAccountSessionStore"/> issues against it and distinguishing the
/// two kinds: the insert-capable upsert and the conditional expiry touch.
/// </summary>
internal sealed class SessionSpy
{
	public ISharpDatabase Database { get; } = Substitute.For<ISharpDatabase>();
	public SharpSession? Stored { get; private set; }
	public int Upserts { get; private set; }
	public int Touches { get; private set; }
	public int Deletes { get; private set; }

	/// <summary>
	/// Runs once, immediately after a read returns, before the caller can act on it. This is the seam that
	/// pins the read-then-write race: whatever it does commits strictly between the two halves.
	/// </summary>
	public Func<ValueTask>? AfterNextRead { get; set; }

	public SessionSpy()
	{
		Database.GetSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(_ => new ValueTask<SharpSession?>(ReadAsync()));
		Database.When(x => x.UpsertSessionAsync(Arg.Any<SharpSession>(), Arg.Any<CancellationToken>()))
			.Do(call =>
			{
				Upserts++;
				var s = call.Arg<SharpSession>();
				// Copy: the store mutates the instance it read back, so keeping the reference would
				// make the "stored" expiry follow un-persisted slides.
				Stored = new SharpSession
				{
					Token = s.Token, AccountId = s.AccountId, OriginIp = s.OriginIp,
					ExpiryUnixMs = s.ExpiryUnixMs, TtlMs = s.TtlMs,
					CharacterKey = s.CharacterKey, CharacterCreationTime = s.CharacterCreationTime
				};
			});
		Database.TouchSessionExpiryAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				Touches++;
				// The whole point of the method: absent document, no write, no insert.
				if (Stored is null) return new ValueTask<bool>(false);
				Stored.ExpiryUnixMs = call.Arg<long>();
				return new ValueTask<bool>(true);
			});
		Database.When(x => x.DeleteSessionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
			.Do(_ =>
			{
				Deletes++;
				Stored = null;
			});
		Database.When(x => x.DeleteSessionsForAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
			.Do(call =>
			{
				if (Stored?.AccountId != call.Arg<string>()) return;
				Deletes++;
				Stored = null;
			});
	}

	private async Task<SharpSession?> ReadAsync()
	{
		var read = Stored;
		var hook = AfterNextRead;
		AfterNextRead = null;
		if (hook is not null) await hook();
		return read;
	}

	/// <summary>Backdates the persisted expiry, standing in for time passing since the last write.</summary>
	public void AgeBy(TimeSpan elapsed) => Stored!.ExpiryUnixMs -= (long)elapsed.TotalMilliseconds;
}
