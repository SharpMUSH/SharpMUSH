using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class PennMUSHLockMetadataTests
{
	[Test]
	public async Task LabeledLockBlockPreservesExpressionCreatorAndFlags()
	{
		var content = "+V0\nsavedtime \"now\"\n!0\nname \"Room Zero\"\ntype 1\nlockcount 1\n type \"Basic\"\n creator #10\n flags 489\n derefs 0\n key \"(=#10|=#11)\"\n***END OF DUMP***\n";
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
		var parser = new PennMUSHDatabaseParser(NullLogger<PennMUSHDatabaseParser>.Instance);
		var database = await parser.ParseAsync(stream);
		var imported = database.Objects[0].Locks["Basic"];
		await Assert.That(imported.Expression).IsEqualTo("(=#10|=#11)");
		await Assert.That(imported.Creator).IsEqualTo(10);
		var converted = imported.ToSharpLockData(new DBRef(42));
		await Assert.That(converted.Creator?.Number).IsEqualTo(42);
		await Assert.That(converted.Flags).IsEqualTo(LockService.LockFlags.Visual | LockService.LockFlags.Locked | LockService.LockFlags.NoSuccessAction | LockService.LockFlags.NoFailureAction | LockService.LockFlags.Owner | LockService.LockFlags.Ox);
	}

	[Test]
	public async Task MissingImportedCreatorRemainsUnknown()
	{
		var converted = new PennMUSHLock("=#10").ToSharpLockData(null);
		await Assert.That(converted.Creator).IsNull();
	}
}
