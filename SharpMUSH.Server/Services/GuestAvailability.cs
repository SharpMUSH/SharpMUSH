using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Whether <c>connect guest</c> can hand a visitor a character: logins and guest logins on, and at least one
/// guest character in the game. The portal offers Play to anonymous visitors on this answer; it used to read
/// <c>Net.Guests</c> alone, so a new game (guests on, no guest characters) sent every visitor to "Sorry, there
/// are no guest characters available."
/// </summary>
/// <remarks>
/// Every portal page load asks (<c>api/server-info</c>), and finding the guests scans the players, so the roster
/// half is kept for <see cref="RosterFreshFor"/>. The guest panel invalidates it when it creates or destroys a
/// guest; a Guest power granted in the game shows within that window. A guest roster that is all in use is not
/// counted against it: that is a moment, which the login itself explains.
/// </remarks>
public interface IGuestAvailability
{
	ValueTask<bool> CanLogInAsync(CancellationToken ct = default);

	/// <summary>Forgets the cached roster answer, after a guest character is created or destroyed.</summary>
	void Invalidate();
}

public sealed class GuestAvailability(IMediator mediator, IOptionsWrapper<SharpMUSHOptions> options) : IGuestAvailability
{
	public static readonly TimeSpan RosterFreshFor = TimeSpan.FromSeconds(30);

	private sealed record Roster(bool Any, DateTimeOffset At);

	private Roster? _roster;

	// Bumped by Invalidate, so a lookup that was already running when a guest was created or destroyed does
	// not put its older answer back.
	private int _generation;

	public async ValueTask<bool> CanLogInAsync(CancellationToken ct = default)
	{
		var net = options.CurrentValue.Net;
		if (!net.Logins || !net.Guests) return false;

		if (Volatile.Read(ref _roster) is { } known && DateTimeOffset.UtcNow - known.At < RosterFreshFor)
			return known.Any;

		var generation = Volatile.Read(ref _generation);
		var any = await GuestCharacters.AllAsync(mediator).AnyAsync(ct);
		var answer = new Roster(any, DateTimeOffset.UtcNow);
		// Publish only while no invalidation happened since the lookup began.
		if (Volatile.Read(ref _generation) == generation) Interlocked.Exchange(ref _roster, answer);
		if (Volatile.Read(ref _generation) != generation) Interlocked.CompareExchange(ref _roster, null, answer);
		return any;
	}

	public void Invalidate()
	{
		Interlocked.Increment(ref _generation);
		Interlocked.Exchange(ref _roster, null);
	}
}
