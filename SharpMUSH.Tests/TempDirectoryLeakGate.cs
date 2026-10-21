namespace SharpMUSH.Tests;

/// <summary>
/// After a whole-suite run, the temporary directory holds nothing the run did not find there. Fixtures that
/// wrote a config file, staged a plugin or opened a world beside <see cref="Path.GetTempPath"/> and never
/// removed it left about forty entries in <c>/tmp</c> per run.
///
/// <para>Only a runner that has the directory to itself can tell the suite's leftovers from another
/// process's files, so the gate runs when <c>SHARPMUSH_TEMP_LEAK_GATE</c> is set (CI's unit job) and
/// stands aside when the run is filtered.</para>
/// </summary>
public static class TempDirectoryLeakGate
{
	private static HashSet<string>? _before;

	private static bool Enabled => Environment.GetEnvironmentVariable("SHARPMUSH_TEMP_LEAK_GATE") is "true";

	[Before(TestSession)]
	public static void RecordTheTemporaryDirectory()
	{
		if (Enabled)
			_before = Entries();
	}

	[After(TestSession)]
	public static void TheRunLeftNothingBehind(TestSessionContext context)
	{
		if (_before is null || !string.IsNullOrEmpty(context.TestFilter))
			return;

		var left = Entries().Except(_before).Order(StringComparer.Ordinal).ToList();
		if (left.Count > 0)
			throw new InvalidOperationException(
				$"The run left {left.Count} entries in {Path.GetTempPath()}: [{string.Join(", ", left)}].");
	}

	private static HashSet<string> Entries() =>
		Directory.EnumerateFileSystemEntries(Path.GetTempPath()).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
}
