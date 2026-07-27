using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Models;

/// <summary>
/// <see cref="DBRef.TryParse"/> is the inbound boundary for object references arriving as
/// untrusted text — SignalR method arguments, NATS payloads, auth claims. Those deserialize to
/// <see langword="null"/> despite non-nullable declarations, because nullable reference types are
/// not enforced at runtime, so this must answer with a bool rather than an exception.
/// </summary>
public class DBRefParseTests
{
	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("42")]
	[Arguments("not-a-dbref")]
	[Arguments("#")]
	[Arguments("#4 2")]
	[Arguments("#42:")]
	public async ValueTask TryParse_Unparseable_ReturnsFalseWithoutThrowing(string? value)
	{
		await Assert.That(DBRef.TryParse(value, out var dbref)).IsFalse();
		await Assert.That(dbref).IsNull();
	}

	[Test]
	public async ValueTask TryParse_BareDbref_HasNoCreationTime()
	{
		await Assert.That(DBRef.TryParse("#42", out var dbref)).IsTrue();
		var parsed = dbref!.Value;
		await Assert.That(parsed.Number).IsEqualTo(42);
		await Assert.That(parsed.CreationMilliseconds).IsNull();
		await Assert.That(parsed.IsObjid).IsFalse();
	}

	[Test]
	public async ValueTask TryParse_Objid_KeepsTheCreationTime()
	{
		await Assert.That(DBRef.TryParse("#42:1700000000", out var dbref)).IsTrue();
		var parsed = dbref!.Value;
		await Assert.That(parsed.Number).IsEqualTo(42);
		await Assert.That(parsed.CreationMilliseconds).IsEqualTo(1700000000L);
		await Assert.That(parsed.IsObjid).IsTrue();
	}

	/// <summary>
	/// The round-trip that every cross-boundary reference relies on: whatever a producer writes
	/// with <see cref="DBRef.ToString"/>, a consumer parses back to an equal value.
	/// </summary>
	[Test]
	[Arguments(42, 1700000000L)]
	[Arguments(42, null)]
	[Arguments(1, 0L)]
	public async ValueTask ToString_RoundTripsThroughTryParse(int number, long? creationMilliseconds)
	{
		var original = new DBRef(number, creationMilliseconds);

		await Assert.That(DBRef.TryParse(original.ToString(), out var parsed)).IsTrue();
		await Assert.That(parsed!.Value).IsEqualTo(original);
	}
}
