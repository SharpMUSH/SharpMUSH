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

	private (bool Any, DateTimeOffset At)? _roster;

	public async ValueTask<bool> CanLogInAsync(CancellationToken ct = default)
	{
		var net = options.CurrentValue.Net;
		if (!net.Logins || !net.Guests) return false;

		if (_roster is { } known && DateTimeOffset.UtcNow - known.At < RosterFreshFor)
			return known.Any;

		var any = await GuestCharacters.AllAsync(mediator).AnyAsync(ct);
		_roster = (any, DateTimeOffset.UtcNow);
		return any;
	}

	public void Invalidate() => _roster = null;
}
