using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Tests.Database.Lightning;

public class CodecTests
{
	[Test]
	public async Task ObjectRecordRoundTripsIncludingLocks()
	{
		var record = new ObjectRecord
		{
			Name = "God", Type = "PLAYER", Aliases = ["#1"], CreationTime = 1, ModifiedTime = 2, Quota = 999999,
			Locks = new() { ["Basic"] = new LockRecord { LockString = "#TRUE", Flags = "" } }
		};
		var back = Codec.Deserialize<ObjectRecord>(Codec.Serialize(record));
		await Assert.That(back).IsEqualTo(record);
		await Assert.That(back.Locks["Basic"].LockString).IsEqualTo("#TRUE");
		await Assert.That(back.PasswordHash).IsNull();
	}
}
