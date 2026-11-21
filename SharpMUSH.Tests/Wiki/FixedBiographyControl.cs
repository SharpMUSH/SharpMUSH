using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// A biography rule with no game behind it: <paramref name="writers"/> maps a biography's slug to the dbrefs
/// that may write it.
/// </summary>
public sealed class FixedBiographyControl(IReadOnlyDictionary<string, string[]> writers) : ICharacterBiographyControl
{
	public ValueTask<bool> MayWriteAsync(string? writerDbref, string title, string slug, CancellationToken ct = default)
		=> ValueTask.FromResult(writerDbref is not null
			&& writers.TryGetValue(slug, out var allowed) && allowed.Contains(writerDbref));
}
