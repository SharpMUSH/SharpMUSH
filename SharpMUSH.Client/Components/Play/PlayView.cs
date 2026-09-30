namespace SharpMUSH.Client.Components.Play;

/// <summary>What the Play page's scene card shows (README §5.3).</summary>
public enum PlayView
{
	/// <summary>The scene's poses, with portraits (§5.4). Offered only in a scene.</summary>
	Story,

	/// <summary>The raw game stream, channels and pages included (§5.5).</summary>
	Terminal,
}
