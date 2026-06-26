using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class PlayStubLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}

public class PlaySidebarTests : BunitContext
{
    public PlaySidebarTests()
    {
        Services.AddMudServices();
        Services.AddLocalization();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // ── IPlayTerminalService (wired to a real OobChannelStore) ──────────────
        // Store is created here so tests can push payloads into it.
        // Registered first so the field is accessible; actual store set per-test.

        // ── Services injected by GlobalTerminal (child component) ───────────────
        // ITerminalService (the command/default terminal, injected as DefaultTerminal)
        var defaultTerminal = Substitute.For<ITerminalService>();
        defaultTerminal.OobChannels.Returns(new OobChannelStore());
        defaultTerminal.Lines.Returns(Array.Empty<SharpMUSH.Client.Models.TerminalLine>());
        Services.AddSingleton(defaultTerminal);

        // CredentialService depends only on IJSRuntime (already provided by bUnit JSInterop)
        Services.AddSingleton(sp =>
            new CredentialService(sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>()));

        // OttAuthService depends on IHttpClientFactory + ILogger
        var httpFactory = Substitute.For<IHttpClientFactory>();
        Services.AddSingleton(httpFactory);
        Services.AddSingleton(sp =>
            new OttAuthService(
                sp.GetRequiredService<IHttpClientFactory>(),
                NullLogger<OttAuthService>.Instance));

        // AccountAuthService depends on IHttpClientFactory + IJSRuntime + ILogger
        Services.AddSingleton(sp =>
            new AccountAuthService(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
                NullLogger<AccountAuthService>.Instance));

        // IWebAssemblyHostEnvironment (needed by GlobalTerminal for IsDevelopment())
        var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
        hostEnv.Environment.Returns("Production");
        Services.AddSingleton(hostEnv);

        // IStringLocalizer<SharedResource> (injected by Play.razor)
        Services.AddSingleton<IStringLocalizer<SharedResource>, PlayStubLocalizer<SharedResource>>();
    }

    [TUnit.Core.Test]
    public async Task Sidebar_RendersPushedRoomContents()
    {
        var store = new OobChannelStore();
        var play = Substitute.For<IPlayTerminalService>();
        play.OobChannels.Returns(store);
        play.Lines.Returns(Array.Empty<SharpMUSH.Client.Models.TerminalLine>());
        Services.AddSingleton<IPlayTerminalService>(play);

        var cut = Render<Play>();

        store.Set("room.contents", "{\"who\":[{\"dbref\":\"#5\",\"name\":\"Bob\",\"cmd\":\"look #5\"}]}");

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Bob"))
                throw new InvalidOperationException("contents not rendered yet");
        }, TimeSpan.FromSeconds(5));
    }
}
