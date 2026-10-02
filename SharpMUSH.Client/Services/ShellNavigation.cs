namespace SharpMUSH.Client.Services;

/// <summary>
/// The shell's navigation drawer, cascaded by MainLayout to its pages. Play hides the shell's touch
/// header and merges it into its own card header, whose avatar opens the drawer through this.
/// </summary>
public sealed class ShellNavigation(Action open)
{
	/// <summary>Opens the navigation drawer (touch chrome; a desktop has the rail instead).</summary>
	public void Open() => open();
}
