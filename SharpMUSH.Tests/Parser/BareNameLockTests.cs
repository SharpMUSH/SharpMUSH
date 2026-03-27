using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Tests for bare name/dbref lock expressions (without = prefix).
/// In PennMUSH, a bare name in a lock is equivalent to =name (exact object match).
/// </summary>
public class BareNameLockTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IBooleanExpressionParser BooleanParser => WebAppFactoryArg.Services.GetRequiredService<IBooleanExpressionParser>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Arguments("me", true)]
	[Arguments("#1", true)]
	[Arguments("#2", true)]
	[Test]
	public async Task BareNameLockValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known;

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("me", true)]
	[Arguments("#1", true)]
	[Arguments("#2", false)]
	[Test]
	public async Task BareNameLockMatching(string input, bool expected)
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known;

		await Assert.That(bep.Compile(input)(player, player)).IsEqualTo(expected);
	}

	[Test]
	public async Task BareNameLockNormalization()
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known;
		var dbRef = player.Object().DBRef;

		var normalized = bep.Normalize($"#{dbRef.Number}");

		// Normalization should convert bare dbref to include creation time
		await Assert.That(normalized).Contains($"#{dbRef.Number}:{dbRef.CreationMilliseconds}");
	}

	[Test]
	public async Task BareNameInCompoundLock()
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known;

		// Test bare name combined with other lock expressions
		await Assert.That(bep.Compile("me & #TRUE")(player, player)).IsTrue();
		await Assert.That(bep.Compile("me | #FALSE")(player, player)).IsTrue();
		await Assert.That(bep.Compile("!me")(player, player)).IsFalse();
	}
}
