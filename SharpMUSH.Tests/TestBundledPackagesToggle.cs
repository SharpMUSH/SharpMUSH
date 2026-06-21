using System.Runtime.CompilerServices;

namespace SharpMUSH.Tests;

/// <summary>
/// Generic unit tests run against the bare engine: the bundled default packages (scene,
/// common-functions, http-/profile-handler) are NOT installed into the test server. This keeps a
/// package's server-global side effects — notably the scene package's <c>@hook/override @EMIT</c>
/// capture — from changing core command behaviour (e.g. <c>@emit</c>'s sender) underneath unrelated
/// tests. Package- and plugin-dependent tests live in <c>SharpMUSH.Tests.Integration</c>, which leaves
/// the bootstrap enabled (its default) and also loads the scene plugin.
///
/// Set process-wide before any test server boots: <see cref="ModuleInitializerAttribute"/> runs once
/// at assembly load, ahead of the first <c>ServerWebAppFactory</c> initialisation.
/// </summary>
internal static class TestBundledPackagesToggle
{
	[ModuleInitializer]
	internal static void DisableBundledPackages() =>
		Environment.SetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_BUNDLED_PACKAGES", "false");
}
