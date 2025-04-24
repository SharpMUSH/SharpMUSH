using OneOf;
using SharpMUSH.Portal.Models;

namespace SharpMUSH.Portal;

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


	public async ValueTask<OneOf<WikiArticle, OneOf.Types.None>> GetWikiArticle(string slug)
	{
		await ValueTask.CompletedTask;

		return _articles.TryGetValue(slug, out var article)
			? article
			: new OneOf.Types.None();
	}
}
