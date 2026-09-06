using System.Runtime.CompilerServices;
using DiffEngine;
namespace SharpMUSH.MarkupString.Tests.Snapshots;

/// <summary>
/// Verify's process-wide configuration for this assembly.
/// </summary>
internal static class VerifySetup
{
	/// <summary>
	/// Keeps a snapshot mismatch a test failure and nothing else. Without this, Verify launches
	/// whichever diff tool it finds installed, which is noise in a terminal run and a hang in CI.
	/// </summary>
	[ModuleInitializer]
	public static void Initialize() => DiffRunner.Disabled = true;
}
