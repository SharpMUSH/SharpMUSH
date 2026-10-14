using Microsoft.Extensions.Options;
using SharpMUSH.Plugins.Lsp;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class LspSessionResourceTests
{
	[Test]
	public async Task Sessions_IsolateSameUrisAndRejectStaleVersions()
	{
		using var resources = new LspResources(Options.Create(new LspOptions()));
		using var first = resources.OpenSession("one");
		using var second = resources.OpenSession("one");
		first.Open("file:///same.fun", "first", 2);
		second.Open("file:///same.fun", "second", 1);
		await Assert.That(first.Change("file:///same.fun", "stale", 1)).IsFalse();
		await Assert.That(first.Get("file:///same.fun")!.Text).IsEqualTo("first");
		await Assert.That(second.Get("file:///same.fun")!.Text).IsEqualTo("second");
		first.Close("file:///same.fun");
		await Assert.That(second.Get("file:///same.fun")).IsNotNull();
	}

	[Test]
	public async Task GlobalAndPrincipalCaps_ReleaseOnDisposeWithoutEvictingOthers()
	{
		using var resources = new LspResources(Options.Create(new LspOptions { MaxSessions = 2, MaxSessionsPerPrincipal = 1, MaxStoredBytes = 8, MaxBytesPerSession = 8, MaxDocumentBytes = 8, MaxDocumentsPerSession = 1 }));
		var first = resources.OpenSession("one");
		using var second = resources.OpenSession("two");
		first.Open("file:///same.fun", "12345678", 1);
		await Assert.That(() => resources.OpenSession("one")).Throws<InvalidOperationException>();
		await Assert.That(() => second.Open("file:///same.fun", "x", 1)).Throws<ArgumentException>();
		await Assert.That(() => first.Open("file:///extra.fun", "x", 1)).Throws<ArgumentException>();
		first.Dispose();
		second.Open("file:///same.fun", "12345678", 1);
		using var replacement = resources.OpenSession("one");
		await Assert.That(second.Get("file:///same.fun")!.Text).IsEqualTo("12345678");
	}

	[Test]
	public async Task FiveMiBAndUtf8Budget_ArePreserved()
	{
		using var resources = new LspResources(Options.Create(new LspOptions()));
		using var session = resources.OpenSession("one");
		session.Open("file:///full.fun", new string('x', 5 * 1024 * 1024), 1);
		await Assert.That(() => session.Open("file:///oversize.fun", new string('é', 3 * 1024 * 1024), 1)).Throws<ArgumentException>();
	}
}
