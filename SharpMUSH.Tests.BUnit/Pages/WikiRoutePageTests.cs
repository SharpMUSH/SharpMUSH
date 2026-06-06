using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Null-stub localizer that returns the key as the string value.
/// Avoids requiring .resx resource files in the test project.
/// </summary>
file sealed class StubLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}

/// <summary>Helper to register the full wiki service stack needed for client component tests.</summary>
file static class WikiServiceSetup
{
    public static void AddWikiTestServices(this BunitContext ctx)
    {
        // bUnit's built-in fake auth — handles AuthorizeView/CascadingAuthenticationState
        ctx.AddAuthorization();

        ctx.Services
            .AddMudServices()
            .AddSingleton<WikiMarkdigPipeline>()
            .AddSingleton<IWikiService, InMemoryWikiService>()
            .AddSingleton<WikiService>()
            .AddSingleton<IStringLocalizer<SharedResource>, StubLocalizer<SharedResource>>();

        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }
}

/// <summary>
/// bUnit tests verifying that the wiki route pages render WikiView
/// with the correct Slug and Mode parameters.
/// </summary>
public class WikiPageRouteTests : BunitContext
{
    public WikiPageRouteTests()
    {
        this.AddWikiTestServices();
    }

    [TUnit.Core.Test]
    public async Task WikiIndex_RendersWikiViewWithWikiIndexSlug()
    {
        var cut = Render<SharpMUSH.Client.Pages.WikiIndex>();

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("wiki-index");
    }

    [TUnit.Core.Test]
    public async Task WikiPage_RendersWikiViewWithParameterSlug()
    {
        var cut = Render<SharpMUSH.Client.Pages.WikiPage>(p => p
            .Add(c => c.Slug, "Magic_System"));

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("Magic_System");
        await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.View);
    }

    [TUnit.Core.Test]
    public async Task WikiPageEdit_RendersWikiViewInEditMode()
    {
        // Seed a page so WikiEdit receives a non-null article.
        // InMemoryWikiService.Slugify: title.ToLowerInvariant().Replace(' ', '_')
        // so "Magic System" → slug "magic_system"
        var wikiSvc = Services.GetRequiredService<IWikiService>();
        await wikiSvc.CreateAsync("Magic System", "Content here.", authorDbref: "#1", WikiNamespace.Main);

        var cut = Render<SharpMUSH.Client.Pages.WikiPageEdit>(p => p
            .Add(c => c.Slug, "magic_system"));

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("magic_system");
        await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.Edit);
    }
}

public class CharacterRouteTests : BunitContext
{
    public CharacterRouteTests()
    {
        this.AddWikiTestServices();
    }

    [TUnit.Core.Test]
    public async Task CharacterProfile_RendersWikiViewWithCharacterName()
    {
        var cut = Render<SharpMUSH.Client.Pages.CharacterProfile>(p => p
            .Add(c => c.Name, "Gandalf"));

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("Gandalf");
        await Assert.That(wikiView.Instance.Mode).IsEqualTo(WikiView.WikiMode.View);
    }
}

public class HelpRouteTests : BunitContext
{
    public HelpRouteTests()
    {
        this.AddWikiTestServices();
    }

    [TUnit.Core.Test]
    public async Task HelpIndex_RendersWikiViewWithHelpIndexSlug()
    {
        var cut = Render<SharpMUSH.Client.Pages.Help>();

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("help-index");
    }

    [TUnit.Core.Test]
    public async Task HelpTopic_RendersWikiViewWithParameterSlug()
    {
        var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p
            .Add(c => c.Slug, "commands"));

        var wikiView = cut.FindComponent<WikiView>();
        await Assert.That(wikiView).IsNotNull();
        await Assert.That(wikiView.Instance.Slug).IsEqualTo("commands");
    }
}
