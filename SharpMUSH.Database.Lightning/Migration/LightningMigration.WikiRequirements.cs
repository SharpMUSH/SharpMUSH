using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// Replaces the wiki's protected flag with requirements, once. Every protected page gets a page requirement
/// of <c>wiki.admin</c> for editing and deleting it, which is what the flag meant, and the row is written
/// again without the flag. The <c>system</c> namespace gets the same requirement for creating, editing and
/// deleting, unless it already has one. The retired protected-page index is emptied.
/// </summary>
public partial class LightningDatabase
{
	internal const string WikiRequirementsMigrationId = "0013_wiki_requirements";

	private const string RetiredWikiProtectedTable = "wiki.protected";

	private async ValueTask MoveWikiProtectionIntoRequirementsAsync(CancellationToken cancellationToken)
	{
		var marker = Keys.Str("mig:" + WikiRequirementsMigrationId);
		if (Store.Read(tx => tx.TryGet(Tables.Meta, marker, out _))) return;

		var hasPages = Store.Read(tx => tx.Range(Tables.WikiPage, []).Any());
		var retiredProtected = hasPages ? Store.OpenTable(RetiredWikiProtectedTable, duplicates: false) : null;

		await Store.WriteAsync(tx =>
		{
			if (tx.TryGet(Tables.Meta, marker, out _)) return;

			var now = DateTimeOffset.UtcNow;
			var admin = new[] { PortalPermission.WikiAdmin };

			foreach (var (key, value) in tx.Range(Tables.WikiPage, []).ToList())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!Codec.Deserialize<WikiPageProtectedRecord>(value).IsProtected) continue;

				var pageKey = Keys.ReadDbref(key);
				tx.Put(Tables.WikiRequirement, WikiRequirementKey(WikiRuleScope.Page, WikiPageId(pageKey)), Codec.Serialize(
					new WikiRequirementRecord
					{
						Scope = "page",
						Key = WikiPageId(pageKey),
						Required = new() { ["edit"] = admin, ["delete"] = admin },
						UpdatedBy = "#1",
						UpdatedAt = now.ToUnixTimeMilliseconds(),
					}));
				tx.Put(Tables.WikiPage, key, Codec.Serialize(Codec.Deserialize<WikiPageRecord>(value)));
			}

			var system = WikiRequirementKey(WikiRuleScope.Namespace, "system");
			if (!tx.TryGet(Tables.WikiRequirement, system, out _))
			{
				tx.Put(Tables.WikiRequirement, system, Codec.Serialize(new WikiRequirementRecord
				{
					Scope = "namespace",
					Key = "system",
					Required = new() { ["create"] = admin, ["edit"] = admin, ["delete"] = admin },
					UpdatedBy = "#1",
					UpdatedAt = now.ToUnixTimeMilliseconds(),
				}));
			}

			if (retiredProtected is not null) tx.DeletePrefix(retiredProtected, []);

			tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
			{
				Id = WikiRequirementsMigrationId, AppliedUnixMs = now.ToUnixTimeMilliseconds()
			}));
		}, cancellationToken);
	}
}
