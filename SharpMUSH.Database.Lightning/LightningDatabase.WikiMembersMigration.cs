using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// World format 2: a wiki requirement row may carry <see cref="WikiRequirementRecord.Members"/>, which make an area
/// private. Nothing on disk changes; the format moves so a format-1 server, which would read past the members and
/// show a private page to everyone, refuses a world that has them.
/// </summary>
public partial class LightningDatabase
{
	internal const string WikiMembersMigrationId = "0013_wiki_private_areas";

	private static void MigrateWikiMembers(ITx tx)
	{
		var marker = Keys.Str("mig:" + WikiMembersMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = WikiMembersMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
