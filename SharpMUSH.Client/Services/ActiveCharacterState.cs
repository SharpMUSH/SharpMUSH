using SharpMUSH.Library.API;
using static SharpMUSH.Client.Services.AccountAuthService;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The account's roster and the character this tab acts as, with the rule that keeps the two agreeing.
/// </summary>
/// <remarks>
/// <para>Split out of <see cref="AccountAuthService"/>, which still exposes both through
/// <see cref="IAccountAuthState"/>. Nothing here talks to the server or to storage: the acting
/// character is whatever the server's roster marks (<see cref="CharacterSummary.IsActing"/>), because
/// it is bound to the session token, which the client cannot read.</para>
///
/// <para>Blazor WASM gives each browser tab its own DI container, so this is already tab-scoped — two
/// tabs may act as different characters on one account.</para>
/// </remarks>
public sealed class ActiveCharacterState(ILogger logger)
{
	public IReadOnlyList<CharacterSummary> Characters { get; private set; } = [];

	/// <summary>The character this tab acts as. Null when the account holds none, or the session is bound to none.</summary>
	public CharacterSummary? ActiveCharacter { get; private set; }

	/// <summary>Raised whenever <see cref="ActiveCharacter"/> changes to a different character.</summary>
	public event Action? Changed;

	/// <summary>
	/// Sets the active character and raises <see cref="Changed"/> if it actually changed. Idempotent:
	/// re-setting the same character raises nothing, so callers may set defensively without causing
	/// render storms. The identity is the dbref AND the creation time — a recycled dbref is a different
	/// character.
	/// </summary>
	public void SetActive(CharacterSummary? character)
	{
		if (ActiveCharacter?.DbrefNumber == character?.DbrefNumber
				&& ActiveCharacter?.CreationTime == character?.CreationTime)
			return;

		ActiveCharacter = character;
		RaiseChanged();
	}

	/// <summary>
	/// Assigns the roster and takes the acting character from the server's marker on it.
	/// </summary>
	/// <remarks>
	/// The marker is the whole answer: an unbound token (or one naming a character the account no
	/// longer owns) comes back with no marker, and the server acts as nobody for it. Picking a
	/// character here anyway would show an identity the server will not honour. Every path that changes
	/// the roster — sign-in, a re-read, an unlink — routes through here, so an active character that
	/// left the roster cannot outlive it.
	/// </remarks>
	public void SetRoster(IReadOnlyList<CharacterSummary> characters)
	{
		Characters = characters;

		var marked = characters.FirstOrDefault(c => c.IsActing);
		if (marked is null)
		{
			SetActive(null);
			return;
		}

		// SetActive no-ops when the identity matches, which would leave a drifted name or flag set on
		// screen after a rename, so assign directly when only the details moved.
		if (marked != ActiveCharacter)
		{
			ActiveCharacter = marked;
			RaiseChanged();
		}
	}

	/// <summary>
	/// Raised when a character's theme or accent changes. Apart from <see cref="Changed"/>, which means a different
	/// identity and makes pages reload what they show for it; a new accent changes nothing they show.
	/// </summary>
	public event Action? AppearanceChanged;

	/// <summary>Records the theme, accent and colour vision the server stored for one character of the roster.</summary>
	public void SetAppearance(int dbrefNumber, CharacterAppearance appearance)
	{
		CharacterSummary Updated(CharacterSummary c) => c with { ThemeId = appearance.ThemeId, Accent = appearance.Accent, Vision = appearance.Vision };

		Characters = Characters.Select(c => c.DbrefNumber == dbrefNumber ? Updated(c) : c).ToList();
		if (ActiveCharacter?.DbrefNumber == dbrefNumber)
		{
			ActiveCharacter = Updated(ActiveCharacter);
		}

		Raise(AppearanceChanged, "AppearanceChanged");
	}

	/// <summary>Adds one character the server has just created; it is not acting until a switch says so.</summary>
	public void Add(CharacterSummary character) => SetRoster([.. Characters, character]);

	/// <summary>The roster without <paramref name="dbrefNumber"/>.</summary>
	public IReadOnlyList<CharacterSummary> Without(int dbrefNumber) =>
		Characters.Where(c => c.DbrefNumber != dbrefNumber).ToList();

	/// <summary>
	/// A subscriber's render exception must never propagate back into the caller mid-switch, so it is
	/// logged and swallowed.
	/// </summary>
	private void RaiseChanged() => Raise(Changed, "ActiveCharacterChanged");

	private void Raise(Action? handlers, string name)
	{
		try
		{
			handlers?.Invoke();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "An {Event} subscriber threw; swallowed", name);
		}
	}
}
