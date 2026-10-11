using System.Security.Claims;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Tests.Integration.CodeAnalysis;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

internal sealed class FixedAnalysisSessions(IMushCodeAnalyzer analyzer) : IAnalysisSessionFactory
{
	public IAnalysisSession CreateSession(ClaimsPrincipal principal) => new Session(analyzer, principal.FindFirst("character_key")?.Value ?? "fixture");
	private sealed class Session(IMushCodeAnalyzer analyzer, string principalKey) : IAnalysisSession
	{
		public string PrincipalKey => principalKey;
		public ValueTask<IMushCodeAnalyzer> CreateAnalyzerAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(analyzer); }
	}
}

internal sealed class CatalogAnalysisSessions(IAnalysisCatalog catalog) : IAnalysisSessionFactory
{
	public IAnalysisSession CreateSession(ClaimsPrincipal principal) => new Session(catalog, int.Parse(principal.FindFirst("character_key")!.Value));
	private sealed class Session(IAnalysisCatalog catalog, int key) : IAnalysisSession
	{
		public string PrincipalKey => key.ToString();
		public async ValueTask<IMushCodeAnalyzer> CreateAnalyzerAsync(CancellationToken cancellationToken = default)
			=> AnalysisCatalogAnalyzer.Create(await catalog.GetSnapshotAsync(LiveAnalysisCatalogTests.Actor(key), cancellationToken), SharpMUSHOptions.Default());
	}
}
