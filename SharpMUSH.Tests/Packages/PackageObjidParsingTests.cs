using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// An objid is an identity key: the package installer stores it in the registry and later resolves
/// it back to a live object. Coercing malformed text into a valid reference therefore does not fail
/// loudly, it silently names the wrong object, so the package services must use the one canonical
/// dbref grammar (<see cref="HelperFunctions.ParseDbRef"/>) rather than a private parser.
/// </summary>
public class PackageObjidParsingTests
{
	[Test]
	[Arguments("#1", 1)]
	[Arguments("#42", 42)]
	public async Task BareDbrefParses(string text, int number)
	{
		await Assert.That(DBRef.TryParse(text, out var dbref)).IsTrue();
		await Assert.That(dbref).IsNotNull();

		var parsed = dbref.GetValueOrDefault();
		await Assert.That(parsed.Number).IsEqualTo(number);
		await Assert.That(parsed.CreationMilliseconds).IsNull();
	}

	[Test]
	public async Task FullObjidParses()
	{
		await Assert.That(DBRef.TryParse("#7:1700000000", out var dbref)).IsTrue();
		await Assert.That(dbref).IsNotNull();

		var parsed = dbref.GetValueOrDefault();
		await Assert.That(parsed.Number).IsEqualTo(7);
		await Assert.That(parsed.CreationMilliseconds).IsEqualTo(1700000000L);
	}

	/// <summary>
	/// The forms the package services' private parser used to accept. Each one silently resolved to
	/// <c>#1</c>: a trailing junk field was dropped on the floor, and <c>int.TryParse</c>'s default
	/// styles let a sign or leading whitespace through. The canonical grammar is anchored and rejects
	/// all of them.
	/// </summary>
	[Test]
	[Arguments("#1:2:3")]
	[Arguments("#+1")]
	[Arguments("# 1")]
	[Arguments("#1 ")]
	[Arguments("#-1")]
	public async Task MalformedObjidIsRejectedRatherThanTruncated(string text)
	{
		await Assert.That(DBRef.TryParse(text, out var dbref)).IsFalse();
		await Assert.That(dbref).IsNull();
	}

	[Test]
	[Arguments("")]
	[Arguments("#")]
	[Arguments("1")]
	[Arguments("abc")]
	public async Task NonObjidIsRejected(string text)
		=> await Assert.That(DBRef.TryParse(text, out _)).IsFalse();

	/// <summary>Round-trips: whatever <see cref="DBRef.ToString"/> writes, the parser reads back.</summary>
	[Test]
	[Arguments(1, null)]
	[Arguments(42, 1700000000L)]
	public async Task RoundTripsWhatDbRefWrites(int number, long? milliseconds)
	{
		var original = new DBRef(number, milliseconds);
		await Assert.That(DBRef.Parse(original.ToString())).IsEqualTo(original);
	}
}
