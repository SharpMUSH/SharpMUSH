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

	/// <summary>
	/// <see cref="DBRef.SameObjectAs"/> exists because the two sides of an identity check are routinely
	/// spelled differently — an auth claim carries the objid, a reference resolved from a graph edge comes
	/// back bare — and <see cref="DBRef.Equals"/> calls those unequal. Comparing the two as strings is what
	/// hid every non-public scene from its own owner.
	/// </summary>
	[Test]
	[Arguments("#1", "#1", true)]
	[Arguments("#1:1785989066109", "#1", true)]
	[Arguments("#1", "#1:1785989066109", true)]
	[Arguments("#1:1785989066109", "#1:1785989066109", true)]
	[Arguments("#1", "#2", false)]
	[Arguments("#1:1785989066109", "#2", false)]
	// Both stamped and disagreeing: a recycled dbref, which must never match.
	[Arguments("#1:1785989066109", "#1:1700000000000", false)]
	public async ValueTask SameObjectAs_ComparesTheStampOnlyWhenBothSidesHaveOne(
		string left, string right, bool expected)
	{
		DBRef.TryParse(left, out var a);
		DBRef.TryParse(right, out var b);

		await Assert.That(a!.Value.SameObjectAs(b!.Value)).IsEqualTo(expected);
		// Symmetric, unlike Matches — an equality test must not depend on operand order.
		await Assert.That(b.Value.SameObjectAs(a.Value)).IsEqualTo(expected);
	}
}
