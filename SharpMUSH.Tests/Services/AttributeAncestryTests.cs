using DotNext.Threading;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

internal static class TestAttributeFactory
{
	/// <summary>
	/// Builds a minimal <see cref="SharpAttribute"/> with the given <see cref="SharpAttribute.LongName"/>
	/// and no flags. Suitable for tests that only care about identity/name, not value or lazy relations.
	/// </summary>
	public static SharpAttribute Named(string longName) => new(
		Id: $"attribute/{longName}",
		Key: longName,
		Name: longName,
		Flags: [],
		CommandListIndex: null,
		LongName: longName,
		Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
		Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
		SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(null)));
}

public class AttributeAncestryTests
{
	private static SharpAttribute Attr(string longName) => TestAttributeFactory.Named(longName);

	private static Func<string[], ValueTask<SharpAttribute?>> NeverFetches()
		=> _ => throw new InvalidOperationException("fetch should not have been called");

	private static Func<string[], ValueTask<SharpAttribute?>> FetchesNothing()
		=> _ => ValueTask.FromResult<SharpAttribute?>(null);

	[Test]
	public async Task TopLevelAttribute_HasOnlyItself()
	{
		var leaf = Attr("FOO");
		var path = await AttributeAncestry.PathAsync(leaf, new Dictionary<string, SharpAttribute>(), NeverFetches());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO" });
	}

	[Test]
	public async Task AncestorsPresentInKnown_AreNotFetched()
	{
		var branch = Attr("FOO");
		var leaf = Attr("FOO`BAR");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase)
		{
			["FOO"] = branch, ["FOO`BAR"] = leaf
		};

		var path = await AttributeAncestry.PathAsync(leaf, known, NeverFetches());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR" });
	}

	[Test]
	public async Task AbsentAncestor_IsFetched()
	{
		var leaf = Attr("FOO`BAR");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase) { ["FOO`BAR"] = leaf };
		var fetched = new List<string>();

		var path = await AttributeAncestry.PathAsync(leaf, known, parts =>
		{
			fetched.Add(string.Join('`', parts));
			return ValueTask.FromResult<SharpAttribute?>(Attr(string.Join('`', parts)));
		});

		await Assert.That(fetched).IsEquivalentTo(new[] { "FOO" });
		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "FOO", "FOO`BAR" });
	}

	[Test]
	public async Task DeepPath_IsRootToLeafInOrder()
	{
		var leaf = Attr("A`B`C`D");
		var path = await AttributeAncestry.PathAsync(leaf, new Dictionary<string, SharpAttribute>(),
			parts => ValueTask.FromResult<SharpAttribute?>(Attr(string.Join('`', parts))));

		await Assert.That(path.Select(a => a.LongName).ToArray())
			.IsEquivalentTo(new[] { "A", "A`B", "A`B`C", "A`B`C`D" });
	}

	[Test]
	public async Task OrphanedLeaf_OmitsMissingAncestorRatherThanFailing()
	{
		// A leaf whose branch node genuinely does not exist means "no ancestor",
		// NOT a denial. Penn treats a missing branch as absent, not as forbidding.
		var leaf = Attr("GONE`LEAF");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase) { ["GONE`LEAF"] = leaf };

		var path = await AttributeAncestry.PathAsync(leaf, known, FetchesNothing());

		await Assert.That(path.Select(a => a.LongName)).IsEquivalentTo(new[] { "GONE`LEAF" });
	}

	[Test]
	public async Task LookupIsCaseInsensitive()
	{
		var leaf = Attr("Foo`Bar");
		var known = new Dictionary<string, SharpAttribute>(StringComparer.OrdinalIgnoreCase)
		{
			["FOO"] = Attr("FOO"), ["Foo`Bar"] = leaf
		};

		var path = await AttributeAncestry.PathAsync(leaf, known, NeverFetches());

		await Assert.That(path).Count().IsEqualTo(2);
	}
}
