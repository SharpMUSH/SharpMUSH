namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Where the client's CSS and Razor sources land once copied into the test output (see the
/// CopyToOutputDirectory items in SharpMUSH.Tests.BUnit.csproj). Centralised so every test that
/// scans them agrees on the layout instead of each one re-deriving it from
/// AppContext.BaseDirectory, which is how the same two paths ended up written out three times.
/// </summary>
internal static class ClientSource
{
	public static string RazorRoot => Path.Join(AppContext.BaseDirectory, "client", "razor");
	public static string CssRoot => Path.Join(AppContext.BaseDirectory, "client", "css");
}
