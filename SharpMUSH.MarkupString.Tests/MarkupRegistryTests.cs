using System.Text.Json;
namespace SharpMUSH.MarkupString.Tests;

/// <summary>
/// The registry's composition rules: what a later registration displaces, and the set-once
/// behaviour behind <see cref="MarkupRegistry.Default"/>.
/// </summary>
/// <remarks>
/// Nothing here assigns <see cref="MarkupRegistry.Default"/> — this assembly deliberately leaves it
/// unconfigured (see <c>RendererTests.Render_WithoutDefaultRegistry_Throws</c>), so the set-once
/// path is exercised through <see cref="MarkupRegistry.AssignOnce"/> against a local field instead.
/// </remarks>
public class MarkupRegistryTests
{
	private sealed record Alpha : IMarkup;

	private sealed record Beta : IMarkup;

	private sealed class Codec(string kind, Type markupType) : IMarkupCodec
	{
		public string Kind => kind;
		public Type MarkupType => markupType;
		public void Write(Utf8JsonWriter writer, IMarkup markup) { }
		public IMarkup Read(JsonElement element) => throw new NotSupportedException("The composition tests never read.");
	}

	[Test]
	public async Task WithCodec_SameKindDifferentType_DropsTheOldTypeMapping()
	{
		var old = new Codec("k", typeof(Alpha));
		var replacement = new Codec("k", typeof(Beta));

		var registry = MarkupRegistry.Empty.With(old).With(replacement);

		await Assert.That(registry.FindCodec("k")).IsSameReferenceAs(replacement);
		await Assert.That(registry.FindCodec(typeof(Beta))).IsSameReferenceAs(replacement);
		// Alpha's codec is no longer reachable by kind, so it must not still be reachable by type:
		// text carrying an Alpha would otherwise serialise under "k" and read back as a Beta.
		await Assert.That(registry.FindCodec(typeof(Alpha))).IsNull();
	}

	[Test]
	public async Task WithCodec_SameTypeDifferentKind_DropsTheOldKindMapping()
	{
		var old = new Codec("old", typeof(Alpha));
		var replacement = new Codec("new", typeof(Alpha));

		var registry = MarkupRegistry.Empty.With(old).With(replacement);

		await Assert.That(registry.FindCodec(typeof(Alpha))).IsSameReferenceAs(replacement);
		await Assert.That(registry.FindCodec("new")).IsSameReferenceAs(replacement);
		await Assert.That(registry.FindCodec("old")).IsNull();
	}

	[Test]
	public async Task WithCodec_UnrelatedCodecs_BothStayReachable()
	{
		var alpha = new Codec("a", typeof(Alpha));
		var beta = new Codec("b", typeof(Beta));

		var registry = MarkupRegistry.Empty.With(alpha).With(beta);

		await Assert.That(registry.FindCodec("a")).IsSameReferenceAs(alpha);
		await Assert.That(registry.FindCodec("b")).IsSameReferenceAs(beta);
		await Assert.That(registry.FindCodec(typeof(Alpha))).IsSameReferenceAs(alpha);
		await Assert.That(registry.FindCodec(typeof(Beta))).IsSameReferenceAs(beta);
	}

	[Test]
	public async Task AssignOnce_FirstAssignment_Sticks()
	{
		MarkupRegistry? field = null;
		var registry = MarkupRegistry.Empty;

		MarkupRegistry.AssignOnce(ref field, registry);

		await Assert.That(field).IsSameReferenceAs(registry);
	}

	[Test]
	public async Task AssignOnce_TheSameInstanceAgain_IsANoOp()
	{
		// This is the shape every host's startup uses: `if (!IsConfigured) Default = …`, which can run
		// twice in one process (a WebApplicationFactory, a second host in the same test run).
		MarkupRegistry? field = null;
		var registry = MarkupRegistry.Empty;

		MarkupRegistry.AssignOnce(ref field, registry);
		MarkupRegistry.AssignOnce(ref field, registry);

		await Assert.That(field).IsSameReferenceAs(registry);
	}

	[Test]
	public async Task AssignOnce_ADifferentInstance_Throws()
	{
		MarkupRegistry? field = null;
		MarkupRegistry.AssignOnce(ref field, MarkupRegistry.Empty);

		var second = MarkupRegistry.Empty.With(new Codec("a", typeof(Alpha)));

		await Assert.That(() => MarkupRegistry.AssignOnce(ref field, second)).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task AssignOnce_Null_Throws()
	{
		MarkupRegistry? field = null;

		await Assert.That(() => MarkupRegistry.AssignOnce(ref field, null!)).Throws<ArgumentNullException>();
	}
}
