using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds the wiki subsystem: <c>node_wiki_pages</c> and <c>node_wiki_revisions</c> vertex collections
/// with appropriate indexes.
/// </summary>
public class Migration_AddWiki : IArangoMigration
{
	public long Id => 20250601_001;

	public string Name => "add_wiki";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		// ── node_wiki_pages ───────────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.WikiPages))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.WikiPages,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							Slug = new { type = DatabaseConstants.TypeString },
							Title = new { type = DatabaseConstants.TypeString },
							Namespace = new { type = DatabaseConstants.TypeString },
							Category = new { type = DatabaseConstants.TypeString },
							MarkdownSource = new { type = DatabaseConstants.TypeString },
							RenderedHtml = new { type = DatabaseConstants.TypeString },
							PlainText = new { type = DatabaseConstants.TypeString },
							AuthorDbref = new { type = DatabaseConstants.TypeString },
							LastEditorDbref = new { type = DatabaseConstants.TypeString },
							IsProtected = new { type = DatabaseConstants.TypeBoolean },
							RevisionNumber = new { type = DatabaseConstants.TypeNumber }
						},
						required = (string[])["Slug", "Title", "Namespace", "MarkdownSource"],
						additionalProperties = true
					}
				}
			});

			// Unique slug-per-(namespace, category) index (compound). Category is part of page
			// identity, so the same slug may exist in different categories within a namespace.
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiPages, new ArangoIndex
			{
				Fields = ["Namespace", "Category", "Slug"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			// UpdatedAt descending for recent-changes queries
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiPages, new ArangoIndex
			{
				Fields = ["UpdatedAt"],
				Type = ArangoIndexType.Persistent
			});

			// Namespace listing
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiPages, new ArangoIndex
			{
				Fields = ["Namespace", "Slug"],
				Type = ArangoIndexType.Persistent
			});
		}

		// ── node_wiki_revisions ───────────────────────────────────────────────
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.WikiRevisions))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.WikiRevisions,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PageId = new { type = DatabaseConstants.TypeString },
							RevisionNumber = new { type = DatabaseConstants.TypeNumber },
							MarkdownSource = new { type = DatabaseConstants.TypeString },
							EditorDbref = new { type = DatabaseConstants.TypeString }
						},
						required = (string[])["PageId", "RevisionNumber", "MarkdownSource"],
						additionalProperties = true
					}
				}
			});

			// Lookup revisions by page
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiRevisions, new ArangoIndex
			{
				Fields = ["PageId", "RevisionNumber"],
				Type = ArangoIndexType.Persistent
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
