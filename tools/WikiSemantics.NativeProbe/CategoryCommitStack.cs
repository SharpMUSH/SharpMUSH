namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>Projects actual causal frame lines without source-path or async-tail size dependence.</summary>
internal static class CategoryCommitStack
{
	internal static string Capture(string raw, bool grouped)
	{
		string[] required = grouped
			? ["at SharpMUSH.Database.Lightning.Store.LightningStore.Commit(",
				 "at SharpMUSH.Database.Lightning.Store.LightningStore.WriteBatch(",
				 "at SharpMUSH.Database.Lightning.Store.LightningWriter.RunBatch("]
			: ["at SharpMUSH.Database.Lightning.Store.LightningStore.Commit("];
		var found = new string[required.Length];
		var next = 0;
		foreach (var sourceLine in raw.AsSpan().EnumerateLines())
		{
			var line = sourceLine.Trim();
			if (!line.StartsWith(required[next], StringComparison.Ordinal)) continue;
			var location = line.IndexOf(" in ", StringComparison.Ordinal);
			if (location >= 0) line = line[..location];
			if (line.Length > 512) throw new InvalidDataException("Oversized native commit witness frame");
			found[next++] = line.ToString();
			if (next == required.Length) break;
		}
		if (next != required.Length)
			throw new InvalidDataException("Missing ordered native commit witness; raw chars=" + raw.Length);
		var result = string.Join('\n', found);
		if (result.Length > 2048) throw new InvalidDataException("Oversized canonical native commit witness");
		return result;
	}
}
