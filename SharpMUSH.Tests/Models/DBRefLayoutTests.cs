using System.Runtime.CompilerServices;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Models;

/// <summary>
/// <see cref="DBRef"/> keeps its creation stamp as a flag and a <see langword="long"/> rather than a
/// <see langword="long"/>?, so it is 16 bytes. These pin that and the values the packed form must still
/// tell apart.
/// </summary>
public class DBRefLayoutTests
{
	[Test]
	public async ValueTask IsSixteenBytes()
		=> await Assert.That(Unsafe.SizeOf<DBRef>()).IsEqualTo(16);

	[Test]
	public async ValueTask DefaultHasNoCreationTime()
	{
		var dbref = default(DBRef);
		await Assert.That(dbref.CreationMilliseconds).IsNull();
		await Assert.That(dbref.IsObjid).IsFalse();
	}

	[Test]
	public async ValueTask ACreationTimeOfZeroIsStillACreationTime()
	{
		var stamped = new DBRef(5, 0);
		await Assert.That(stamped.CreationMilliseconds).IsEqualTo(0);
		await Assert.That(stamped).IsNotEqualTo(new DBRef(5));
		await Assert.That(stamped.GetHashCode()).IsEqualTo(new DBRef(5, 0).GetHashCode());
	}

	[Test]
	public async ValueTask InitSetsAndClearsTheCreationTime()
	{
		var stamped = new DBRef(7) with { CreationMilliseconds = 1234 };
		await Assert.That(stamped).IsEqualTo(new DBRef(7, 1234));
		await Assert.That(stamped.ToString()).IsEqualTo("#7:1234");

		var bare = stamped with { CreationMilliseconds = null };
		await Assert.That(bare).IsEqualTo(new DBRef(7));
		await Assert.That(bare.ToString()).IsEqualTo("#7");
	}
}
