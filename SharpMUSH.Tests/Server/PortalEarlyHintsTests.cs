using SharpMUSH.Server;

namespace SharpMUSH.Tests.Server;

/// <summary>What the portal shell's <c>Link</c> header names (<see cref="PortalEarlyHints"/>), read from a shell shaped like the published one.</summary>
public class PortalEarlyHintsTests
{
	private const string Shell = """
		<!DOCTYPE html>
		<html>
		<head>
			<base href="/" />
			<link href="_framework/dotnet.abc.js" rel="preload" as="script" fetchpriority="high" crossorigin="anonymous" integrity="sha256-x" />
			<!-- <link rel="preload" as="style" href="css/commented.css" /> -->
			<link rel="preload" as="style" href="css/tokens.css" />
			<link rel="icon" href="api/server-info/favicon" />
			<link rel="manifest" href="manifest.webmanifest" />
			<link rel="preload" href="/fonts/mono.woff2" as="font" type="font/woff2" crossorigin="anonymous" />
			<link href="css/custom.css" rel="stylesheet" />
			<link href="https://fonts.example/x.css" rel="stylesheet" />
			<link rel="preload" as="style" href="css/tokens.css" />
		</head>
		<body>
			<script src="js/layout.def.js"></script>
			<script src="_framework/blazor.webassembly.js" autostart="false"></script>
			<script src="js/unfilled#[.{fingerprint}].js"></script>
		</body>
		</html>
		""";

	[Test]
	public async Task NamesThePreloadsStylesheetsAndBootScript_InOrder()
	{
		await Assert.That(PortalEarlyHints.LinkHeader(Shell)).IsEqualTo(string.Join(", ",
			"</css/tokens.css>; rel=preload; as=style",
			"</fonts/mono.woff2>; rel=preload; as=font; type=\"font/woff2\"; crossorigin",
			"</css/custom.css>; rel=preload; as=style",
			"</_framework/blazor.webassembly.js>; rel=preload; as=script"));
	}

	/// <summary>A hint without the integrity its tag carries would not be reused, and the file would be fetched twice.</summary>
	[Test]
	public async Task LeavesOutAPreloadCarryingIntegrity()
	{
		await Assert.That(PortalEarlyHints.LinkHeader(Shell)).DoesNotContain("dotnet.abc.js");
	}

	[Test]
	public async Task AShellNamingNothing_HasNoHeader()
	{
		await Assert.That(PortalEarlyHints.LinkHeader("<html><body>portal</body></html>")).IsNull();
	}
}
