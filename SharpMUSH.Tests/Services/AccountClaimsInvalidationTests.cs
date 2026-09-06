using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// N-03 regression cover: the cached role/permission claims for an account are derived from the
/// characters it owns, so linking or unlinking one makes them stale. Nothing invalidated them, and
/// the 30s FusionCache TTL was the only thing that eventually did — a brand-new player who created
/// their first character and logged straight in got <c>role=Guest</c> with zero scopes, and an
/// account that unlinked its last character kept its Player scopes for up to 30s afterwards.
/// <para>
/// The invalidation lives on <see cref="AccountService"/>, where the link is actually written, so
/// no caller can forget it. <see cref="EveryCharacterLinkWriteGoesThroughAccountService"/> is what
/// keeps that true: it fails if a future call site reaches past the service to the database.
/// </para>
/// </summary>
public class AccountClaimsInvalidationTests
{
	private static (AccountService Service, ISharpDatabase Database, IAccountClaimsInvalidator Invalidator) Build()
	{
		var database = Substitute.For<ISharpDatabase>();
		var invalidator = Substitute.For<IAccountClaimsInvalidator>();
		var service = new AccountService(database, database, Substitute.For<IPasswordService>(),
			Substitute.For<IAccountSessionStore>(), banEnforcer: null, claimsInvalidator: invalidator);

		return (service, database, invalidator);
	}

	[Test]
	public async Task LinkCharacterAsync_InvalidatesCachedClaims()
	{
		var (service, database, invalidator) = Build();

		await service.LinkCharacterAsync("node_accounts/1", new DBRef(7, 777L));

		await database.Received(1).LinkCharacterToAccountAsync("node_accounts/1", new DBRef(7, 777L),
			Arg.Any<CancellationToken>());
		await invalidator.Received(1).InvalidateAsync("node_accounts/1", Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The security-relevant direction: unlinking the last character drops the account to Guest, and
	/// a stale cache entry would keep granting Player scopes it is no longer entitled to.
	/// </summary>
	[Test]
	public async Task UnlinkCharacterAsync_InvalidatesCachedClaims()
	{
		var (service, database, invalidator) = Build();

		await service.UnlinkCharacterAsync("node_accounts/1", new DBRef(7, 777L));

		await database.Received(1).UnlinkCharacterFromAccountAsync("node_accounts/1", new DBRef(7, 777L),
			Arg.Any<CancellationToken>());
		await invalidator.Received(1).InvalidateAsync("node_accounts/1", Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A cache invalidation placed on the service only helps for as long as everything that links a
	/// character goes through the service. This fails the moment production code calls the database's
	/// link/unlink directly — the one way a future call site could silently skip the invalidation.
	/// </summary>
	[Test]
	public async Task EveryCharacterLinkWriteGoesThroughAccountService()
	{
		string[] databaseWrites = ["LinkCharacterToAccountAsync", "UnlinkCharacterFromAccountAsync"];

		// The declaration, the one implementation allowed to call it, and the providers that define it.
		string[] permitted =
		[
			Path.Combine("SharpMUSH.Library", "Stores", "IAccountStore.cs"),
			Path.Combine("SharpMUSH.Library", "Services", "AccountService.cs"),
		];

		var offenders = ProductionSourceFiles(RepositoryRoot())
			.Where(file => !permitted.Any(p => file.EndsWith(p, StringComparison.Ordinal)))
			.Select(file => (File: file, Text: File.ReadAllText(file)))
			.Where(f => databaseWrites.Any(w => f.Text.Contains(w, StringComparison.Ordinal)))
			.Select(f => Path.GetRelativePath(RepositoryRoot(), f.File))
			.Order(StringComparer.Ordinal)
			.ToArray();

		await Assert.That(offenders).IsEmpty()
			.Because("character link/unlink must go through IAccountService, which invalidates the "
				+ "account's cached role/permission claims; these call the database directly and skip it: "
				+ string.Join(", ", offenders));
	}

	/// <summary>Every <c>.cs</c> file in a shipped project — test and database-provider projects excluded.</summary>
	private static IEnumerable<string> ProductionSourceFiles(string root) =>
		Directory.EnumerateDirectories(root, "SharpMUSH.*")
			.Where(d => !Path.GetFileName(d).Contains("Tests", StringComparison.Ordinal))
			.Where(d => !Path.GetFileName(d).StartsWith("SharpMUSH.Database", StringComparison.Ordinal))
			.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

	/// <summary>Walks up from the test binaries to the directory holding <c>SharpMUSH.sln</c>.</summary>
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpMUSH.sln")))
			directory = directory.Parent;

		return directory?.FullName
			?? throw new InvalidOperationException("Could not locate the repository root (no SharpMUSH.sln above the test binaries).");
	}
}
