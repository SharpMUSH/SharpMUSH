using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The <c>depth</c> argument of <see cref="IAttributeService.GetVisibleAttributesAsync"/> and
/// <see cref="IAttributeService.LazilyGetVisibleAttributesAsync"/>: how many levels of the
/// attribute tree a listing descends, and in what order. The lazy walk is bounded by a
/// <see cref="TimeoutAttribute"/> because it once counted its depth in the wrong direction and
/// never finished.
/// </summary>
public class AttributeVisibleDepthTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private async ValueTask<AnySharpObject> CreateTreeAsync(string label)
	{
		var result = await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create {label}"));
		var dbref = DBRef.Parse(result.Message!.ToPlainText()!);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

		await AttributeService.SetAttributeAsync(obj, obj, "TREE", MarkupText.Plain("root"));
		await AttributeService.SetAttributeAsync(obj, obj, "TREE`BRANCH", MarkupText.Plain("branch"));
		await AttributeService.SetAttributeAsync(obj, obj, "TREE`BRANCH`LEAF", MarkupText.Plain("leaf"));

		return obj;
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask GetVisibleAttributesAsync_DescendsToDepth_LevelBeforeSubtree(CancellationToken ct)
	{
		var obj = await CreateTreeAsync("VisibleDepth");

		var full = await AttributeService.GetVisibleAttributesAsync(obj, obj, depth: 3);
		await Assert.That(full.IsAttribute).IsTrue();

		var names = full.Expect<SharpAttribute[]>().Select(a => a.LongName).ToList();
		await Assert.That(names).Contains("TREE");
		await Assert.That(names).Contains("TREE`BRANCH");
		await Assert.That(names).Contains("TREE`BRANCH`LEAF");
		await Assert.That(names.IndexOf("TREE`BRANCH")).IsGreaterThan(names.IndexOf("TREE"));
		await Assert.That(names.IndexOf("TREE`BRANCH`LEAF")).IsGreaterThan(names.IndexOf("TREE`BRANCH"));

		var shallow = await AttributeService.GetVisibleAttributesAsync(obj, obj, depth: 2);
		var shallowNames = shallow.Expect<SharpAttribute[]>().Select(a => a.LongName).ToList();
		await Assert.That(shallowNames).Contains("TREE`BRANCH");
		await Assert.That(shallowNames).DoesNotContain("TREE`BRANCH`LEAF");
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask LazilyGetVisibleAttributesAsync_DeeperThanOne_TerminatesAtDepth(CancellationToken ct)
	{
		var obj = await CreateTreeAsync("LazyVisibleDepth");

		var full = await AttributeService.LazilyGetVisibleAttributesAsync(obj, obj, depth: 3);
		await Assert.That(full.IsAttribute).IsTrue();

		var names = await full.Expect<IAsyncEnumerable<LazySharpAttribute>>().Select(a => a.LongName).ToListAsync(ct);
		await Assert.That(names).Contains("TREE");
		await Assert.That(names).Contains("TREE`BRANCH");
		await Assert.That(names).Contains("TREE`BRANCH`LEAF");

		var shallow = await AttributeService.LazilyGetVisibleAttributesAsync(obj, obj, depth: 2);
		var shallowNames = await shallow.Expect<IAsyncEnumerable<LazySharpAttribute>>().Select(a => a.LongName).ToListAsync(ct);
		await Assert.That(shallowNames).Contains("TREE`BRANCH");
		await Assert.That(shallowNames).DoesNotContain("TREE`BRANCH`LEAF");
	}
}
