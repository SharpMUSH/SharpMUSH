using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class StubLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}

/// <summary>Serves a fixed character roster on GET http/characters, mirroring GET`CHARACTERS rows.</summary>
file sealed class RosterHandler : HttpMessageHandler
{
    private record Row(string Name, string Objid, long Created, string Category);

    private static readonly Row[] Roster =
    [
        new("Pleb", "#10:1000", 1000, ""),
        new("Gus", "#11:1100", 1100, "Guest"),
        new("Roy", "#12:1200", 1200, "Royalty"),
        new("Wizzy", "#13:1300", 1300, "Wizard"),
    ];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/http/characters"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Roster) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>
/// Verifies the directory widget's category grouping comes entirely from the data: named
/// categories alphabetical, blanks pooled untitled at the bottom, no invented labels.
/// (Who is listed at all is MUSH-side policy — FN`CHARVIS — so the widget does no filtering.)
/// </summary>
public class CharacterDirectoryWidgetTests : BunitContext
{
    public CharacterDirectoryWidgetTests()
    {
        var apiClient = new HttpClient(new RosterHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("api").Returns(apiClient);

        Services
            .AddSingleton(apiClient)
            .AddMudServices()
            .AddSingleton(factory)
            .AddSingleton(sp => new CharacterDirectoryService(
                sp.GetRequiredService<IHttpClientFactory>(),
                NullLogger<CharacterDirectoryService>.Instance))
            .AddSingleton<IStringLocalizer<SharedResource>, StubLocalizer<SharedResource>>();

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TUnit.Core.Test]
    public async Task GroupsAlphabetically_WithBlankCategoryUntitledLast()
    {
        var cut = Render<CharacterDirectoryWidget>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Pleb")) throw new InvalidOperationException("roster not loaded yet");
        }, TimeSpan.FromSeconds(5));

        var markup = cut.Markup;
        // Named categories in alphabetical order, then the blank group's member last, headerless.
        await Assert.That(markup.IndexOf(">Guest<", StringComparison.Ordinal)).IsGreaterThan(-1);
        await Assert.That(markup.IndexOf(">Guest<", StringComparison.Ordinal))
            .IsLessThan(markup.IndexOf(">Royalty<", StringComparison.Ordinal));
        await Assert.That(markup.IndexOf(">Royalty<", StringComparison.Ordinal))
            .IsLessThan(markup.IndexOf(">Wizard<", StringComparison.Ordinal));
        await Assert.That(markup.IndexOf(">Wizzy<", StringComparison.Ordinal))
            .IsLessThan(markup.IndexOf(">Pleb<", StringComparison.Ordinal));
        // The widget invents no label for the blank category.
        await Assert.That(markup).DoesNotContain("Player");
    }
}
