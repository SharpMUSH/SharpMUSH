using SharpMUSH.Tools.ContractAudit;

namespace SharpMUSH.Tests.Documentation;

public class ContractAuditProvenanceTests
{
	[Test]
	public async Task CensusWritesAndInputOrderDoNotChangeIdentityButSourceEditsDo()
	{
		var directory = Directory.CreateTempSubdirectory("contract-provenance-");
		try
		{
			Directory.CreateDirectory(Path.Join(directory.FullName, "docs/audits"));
			var source = Path.Join(directory.FullName, "source.cs");
			var census = Path.Join(directory.FullName, "docs/audits/softcode-contracts.json");
			await File.WriteAllTextAsync(source, "before");
			await File.WriteAllTextAsync(census, "old generated output");
			var first = SourceProvenance.Capture(directory.FullName, ["source.cs", "docs/audits/softcode-contracts.json"]);
			await File.WriteAllTextAsync(census, "new generated output");
			var regenerated = SourceProvenance.Capture(directory.FullName, ["docs/audits/softcode-contracts.json", "source.cs"]);
			await Assert.That(regenerated).IsEqualTo(first);
			await Assert.That(first.Files).IsEqualTo(1);
			await File.WriteAllTextAsync(source, "after");
			var changed = SourceProvenance.Capture(directory.FullName, ["source.cs", "docs/audits/softcode-contracts.json"]);
			await Assert.That(changed.ContentSha256).IsNotEqualTo(first.ContentSha256);
		}
		finally
		{
			directory.Delete(true);
		}
	}
}
