namespace SharpMUSH.Client.Components.Play;

/// <summary>Why the Play page shows a gate instead of the terminal (Play.razor says why each is its own state).</summary>
public enum PlayGateState
{
	/// <summary>The account's roster is still being read.</summary>
	Resolving,

	/// <summary>The roster request failed: the page has no answer, so it asserts nothing about the account.</summary>
	RosterFailed,

	/// <summary>The account holds no character yet.</summary>
	NeedsCharacter,
}
