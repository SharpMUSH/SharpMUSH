using OneOf;
using OneOf.Types;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

public class WikiService
{
	private Dictionary<string, WikiArticle> _articles = new Dictionary<string, WikiArticle>()
	{
		{
			"home",
			new WikiArticle("SharpMUSH",
																		@"
### Prepare for Adventure!
[Getting Started](/wiki/getting-started)
",
						"assets/Logo.svg")
		},
		{
			"getting-started",
			new WikiArticle("Getting Started",
				"An article about adventurers getting started!",
				null)
		}
	};


	public async ValueTask<OneOf<WikiArticle, None>> GetWikiArticle(string slug)
	{
		await ValueTask.CompletedTask;

		return _articles.TryGetValue(slug, out var article)
			? article
			: new None();
	}
}
