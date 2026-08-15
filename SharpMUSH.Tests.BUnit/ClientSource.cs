namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Where the client's CSS and Razor sources land in the test output — see the
/// CopyToOutputDirectory items in SharpMUSH.Tests.BUnit.csproj.
/// </summary>
internal static class ClientSource
{
	public static string RazorRoot => Path.Join(AppContext.BaseDirectory, "client", "razor");
	public static string CssRoot => Path.Join(AppContext.BaseDirectory, "client", "css");
}
