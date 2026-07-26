using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Parser;

public class BooleanExpressionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IBooleanExpressionParser BooleanParser => WebAppFactoryArg.Services.GetRequiredService<IBooleanExpressionParser>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Arguments("!#FALSE", true)]
	[Arguments("#TRUE", true)]
	[Arguments("(#TRUE)", true)]
	[Arguments("!#TRUE", false)]
	[Arguments("#FALSE", false)]
	[Arguments("(#FALSE)", false)]
	[Arguments("(#FALSE | #TRUE) & #TRUE", true)]
	[Arguments("(#FALSE | #TRUE) | #FALSE", true)]
	[Arguments("(#FALSE | #TRUE) & #FALSE", false)]
	[Arguments("#TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE", true)]
	[Arguments("#TRUE & #FALSE", false)]
	[Arguments("#FALSE & #TRUE", false)]
	[Arguments("#TRUE | #FALSE", true)]
	[Arguments("#FALSE | #TRUE", true)]
	[Arguments("#TRUE & #TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE | #TRUE", true)]
	[Test]
	public async Task SimpleExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player & #TRUE", true)]
	[Arguments("type^Player & #FALSE", false)]
	[Arguments("type^Player & !type^Player", false)]
	[Arguments("type^Thing", false)]
	[Arguments("type^Player", true)]
	[Test]
	public async Task TypeExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player", true)]
	[Arguments("type^Thing", true)]
	[Arguments("type^Room", true)]
	[Arguments("type^Exit", true)]
	[Arguments("type^Nonsense", false)]
	[Test]
	public async Task TypeValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("name^Player*", true)]
	[Arguments("name^test", true)]
	[Arguments("name^*", true)]
	[Test]
	public async Task NameValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("=me", true)]
	[Arguments("=#1", true)]
	[Arguments("=TestObject", true)]
	[Test]
	public async Task ExactObjectValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("name^God", true)]
	[Arguments("name^NonExistent", false)]
	[Test]
	public async Task NameExpressionMatching(string input, bool expected)
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, player)).IsTrue();
		await Assert.That(bep.Compile(input)(player, player)).IsEqualTo(expected);
	}

	[Arguments("=#1", true)]
	[Arguments("=#2", false)]
	[Arguments("=me", true)]
	[Test]
	public async Task ExactObjectMatching(string input, bool expected)
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, player)).IsTrue();
		await Assert.That(bep.Compile(input)(player, player)).IsEqualTo(expected);
	}

	[Arguments("dbreflist^testattr", true)]
	[Test]
	public async Task DbRefListValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("ip^127.0.0.1", true)]
	[Arguments("ip^192.168.*", true)]
	[Arguments("hostname^localhost", true)]
	[Test]
	public async Task HostLockValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("channel^Public", true)]
	[Test]
	public async Task ChannelValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Test]
	public async Task InvalidateCache_SpecificExpression_AllowsRecompile()
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		var input = "type^Player";

		await Assert.That(bep.Compile(input)(player, player)).IsTrue();
		bep.InvalidateCache(input);
		await Assert.That(bep.Compile(input)(player, player)).IsTrue();
	}

	[Test]
	public async Task InvalidateCache_AllExpressions_AllowsRecompile()
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Compile("#TRUE")(player, player)).IsTrue();
		await Assert.That(bep.Compile("type^Thing")(player, player)).IsFalse();

		bep.InvalidateCache();

		await Assert.That(bep.Compile("#TRUE")(player, player)).IsTrue();
		await Assert.That(bep.Compile("type^Thing")(player, player)).IsFalse();
	}

	[Test]
	public async Task CompileAndInvalidateCache_Concurrently_DoesNotThrow()
	{
		const int iterationCount = 1000;
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();
		var input = "type^Player";
		var errors = new ConcurrentQueue<Exception>();

		var compileTask = Task.Run(() =>
		{
			for (var i = 0; i < iterationCount; i++)
			{
				try
				{
					var compiled = bep.Compile(input);
					_ = compiled(player, player);
				}
				catch (Exception ex)
				{
					errors.Enqueue(ex);
				}
			}
		});

		var invalidateTask = Task.Run(() =>
		{
			for (var i = 0; i < iterationCount; i++)
			{
				try
				{
					bep.InvalidateCache(input);
				}
				catch (Exception ex)
				{
					errors.Enqueue(ex);
				}
			}
		});

		await Task.WhenAll(compileTask, invalidateTask);
		await Assert.That(errors).IsEmpty();
	}

	/// <summary>
	/// Malformed lock expressions must not validate. ANTLR's error recovery produces a partial
	/// tree that the validation visitor was happy to walk, so LockService.Set — which gates on
	/// Validate — accepted locks whose text could never mean what was typed.
	/// </summary>
	[Arguments("#TRUE &")]
	[Arguments("& #TRUE")]
	[Arguments("#TRUE | | #FALSE")]
	[Arguments("(#TRUE")]
	[Arguments("#TRUE)")]
	[Test]
	public async Task MalformedExpressionsAreInvalid(string input)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsFalse();
	}

	/// <summary>
	/// PennMUSH binds <c>&amp;</c> tighter than <c>|</c> (src/boolexp.c: <c>E -&gt; T | E</c>,
	/// <c>T -&gt; F &amp; T</c>), so <c>a &amp; b | c</c> groups as <c>(a &amp; b) | c</c>.
	/// The first four cases are false under the equal-precedence reading <c>a &amp; (b | c)</c>,
	/// which is what SharpMUSH did before the grammar gained precedence layers — an
	/// over-restrictive lock. The last two are the inverse: true under the old reading only.
	/// </summary>
	[Arguments("#FALSE & #FALSE | #TRUE", true)]
	[Arguments("#FALSE & #TRUE | #TRUE", true)]
	[Arguments("#FALSE & #FALSE | #FALSE | #TRUE", true)]
	[Arguments("!#TRUE & #FALSE | !#FALSE", true)]
	[Arguments("#TRUE & #FALSE | #FALSE", false)]
	[Arguments("#TRUE & #TRUE & #FALSE | #FALSE", false)]
	[Test]
	public async Task AndBindsTighterThanOr(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	/// <summary>
	/// Explicit parentheses must still override precedence, and chains of a single
	/// operator must stay associative.
	/// </summary>
	[Arguments("#FALSE & (#FALSE | #TRUE)", false)]
	[Arguments("(#FALSE & #FALSE) | #TRUE", true)]
	[Arguments("#TRUE & (#TRUE | #FALSE)", true)]
	[Arguments("#TRUE & #TRUE & #TRUE & #TRUE", true)]
	[Arguments("#TRUE & #TRUE & #FALSE & #TRUE", false)]
	[Arguments("#FALSE | #FALSE | #FALSE | #TRUE", true)]
	[Arguments("#FALSE | #FALSE | #FALSE", false)]
	[Test]
	public async Task PrecedenceGroupingAndAssociativity(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	/// <summary>
	/// Normalization must round-trip: the canonical text a lock is stored as has to
	/// re-compile to the same truth value, or @lock would silently rewrite semantics.
	/// </summary>
	[Arguments("#FALSE & #FALSE | #TRUE")]
	[Arguments("#TRUE & #FALSE | #FALSE")]
	[Arguments("#FALSE & (#FALSE | #TRUE)")]
	[Arguments("#TRUE | #FALSE & #FALSE")]
	[Test]
	public async Task NormalizeRoundTripsPrecedence(string input)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		var normalized = bep.Normalize(input);
		var before = bep.Compile(input)(dbn, dbn);
		var after = bep.Compile(normalized)(dbn, dbn);

		await Assert.That(after).IsEqualTo(before);
	}
}
