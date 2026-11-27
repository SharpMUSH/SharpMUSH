namespace SharpMUSH.Client.Services;

/// <summary>
/// How the last Play page in this tab was laid out once it had read the browser's stored choices and the screen. Play
/// reads them back from the browser only after its first render, so a Play mounted afresh (a switch between characters
/// on /client) would first draw the defaults and then animate into the choices, the aside sliding shut among them.
/// Starting from these instead, it draws the layout it will keep.
/// </summary>
public sealed class PlayLayoutMemory
{
	public PlayLayout? Last { get; set; }
}

/// <summary>The choices and screen facts Play reads after its first render.</summary>
public sealed record PlayLayout(
	int Cols,
	bool BannerOpen,
	bool AsideCollapsed,
	bool WrapAtCols,
	int InputLines,
	bool CompactScreen,
	bool ShortScreen,
	bool TouchChrome);
