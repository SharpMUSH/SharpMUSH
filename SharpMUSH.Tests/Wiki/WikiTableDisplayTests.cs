using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

public class WikiTableDisplayTests
{
	[Test]
	public async Task StoredTablesAreUpgradedBeforeBlazorRendersAndArticleChangesLeaveNoWrappers()
	{
		await using var ctx = new BunitContext();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.AddAuthorization();
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();
		ctx.Services.AddSingleton(Substitute.For<IAccountAuthState>());
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance));
		ctx.Services.AddSingleton(sp => new WikiService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Equipment", "| Name | Damage |\n| --- | --- |\n| Sword | 1d8 |", null,
			"<table><thead><tr><th>Name</th><th>Damage</th></tr></thead><tbody><tr><td>Sword</td><td>1d8</td></tr></tbody></table>");
		var cut = ctx.Render<WikiDisplay>(p => p.Add(c => c.Slug, "equipment").Add(c => c.Article, article).Add(c => c.Embedded, true));
		await Assert.That(cut.FindAll(".wiki-table-scroll > table").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("th").Count).IsEqualTo(2);

		article.Content = "| Name | Damage |\n| --- | --- |\n| Spear | 1d6 |";
		cut.Render(p => p.Add(c => c.Article, article));
		await Assert.That(cut.Find("td").TextContent).IsEqualTo("Spear");
		await Assert.That(cut.FindAll(".wiki-table-scroll").Count).IsEqualTo(1);

		cut.Render(p => p.Add(c => c.Article, new WikiArticle("Rules", "Plain rules", null, "<p>Plain rules</p>")));
		await Assert.That(cut.FindAll(".wiki-table-scroll").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("table").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("Plain rules");
		cut.Dispose();
	}
}
