using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Themes;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Serves the themes API and the portal's parts CSS, as the server and the portal's own files would.</summary>
file sealed class ThemesHandler(string themes) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var css = path.StartsWith("css/themes/", StringComparison.Ordinal) ? Path.Join(ClientSource.ThemePartsRoot, Path.GetFileName(path)) : null;
		return Task.FromResult(path == "api/themes"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(themes, Encoding.UTF8, "application/json") }
			: css is not null && File.Exists(css)
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(css), Encoding.UTF8, "text/css") }
				: new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// The theme editor's three modes: Simple shows a menu per setting, Complex a menu per part with the decorative
/// colours, Custom those and the stylesheet, its starter listing the rules the theme's parts apply.
/// </summary>
public class AdminThemesModesTests : TrackingBunitContext
{
	public AdminThemesModesTests()
	{
		var own = BuiltInThemes.Fantasy with { Id = "own", Name = "Own", BuiltIn = false };
		var json = JsonSerializer.Serialize(new PortalThemesResponse([own], "own", "own"), JsonSerializerOptions.Web);
		var handler = new ThemesHandler(json);
		var api = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var files = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		factory.CreateClient("help").Returns(files);
		var themes = Substitute.For<IThemeService>();
		themes.Current.Returns(ThemeResolver.Resolve(BuiltInThemes.Phosphor));
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(themes)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddEchoLocalizer()
			.AddSingleton<PortalThemesAdminService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization().SetAuthorized("staff");
	}

	private IRenderedComponent<MudHarness> RenderEditor()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<AdminThemes>());
		cut.WaitForElement("#theme-edit-mode");
		return cut;
	}

	private static Task ChooseAsync(IRenderedComponent<MudHarness> cut, string mode)
		=> cut.Find($"#theme-edit-mode [data-mode='{mode}']").ClickAsync();

	[Test]
	public async Task SimpleShowsOneMenuPerSettingAndNoStylesheet()
	{
		var cut = RenderEditor();

		await Assert.That(cut.Find("#theme-edit-mode [aria-checked='true']").GetAttribute("data-mode")).IsEqualTo(ThemeStyles.Simple);
		await Assert.That(cut.Markup).Contains("AdmThemeStyleFrame").And.DoesNotContain("AdmThemeStyleFrameMarks");
		await Assert.That(cut.FindAll("#theme-stylesheet")).IsEmpty();
		await Assert.That(cut.FindAll("#theme-decorative")).IsEmpty();
	}

	[Test]
	public async Task ComplexShowsEachPartAndTheDecorativeColours()
	{
		var cut = RenderEditor();

		await ChooseAsync(cut, ThemeStyles.Complex);

		cut.WaitForElement("#theme-decorative");
		await Assert.That(cut.Markup).Contains("AdmThemeStyleFrameMarks").And.Contains("AdmThemeStyleEffectShadow");
		await Assert.That(cut.FindAll("#theme-decorative [data-token]").Select(e => e.GetAttribute("data-token")))
			.IsEquivalentTo(ThemeTokens.Decorative);
		await Assert.That(cut.FindAll("#theme-stylesheet")).IsEmpty();
	}

	[Test]
	public async Task CustomStartsTheStylesheetFromTheThemesPartRules()
	{
		var cut = RenderEditor();

		await ChooseAsync(cut, ThemeStyles.Custom);

		var sheet = cut.WaitForElement("#theme-sheet");
		await Assert.That(sheet.GetAttribute("value")).Contains("/* :root {\n\t--font-display: 'Cinzel'");
		await Assert.That(cut.Markup).Contains("AdmThemeStyleFrameMarks");
	}

	[Test]
	public async Task LeavingCustomWithoutAStylesheetAsksNothing()
	{
		var cut = RenderEditor();
		await ChooseAsync(cut, ThemeStyles.Custom);
		cut.WaitForElement("#theme-sheet");

		await ChooseAsync(cut, ThemeStyles.Simple);

		await Assert.That(cut.FindAll("#theme-stylesheet")).IsEmpty();
		await Assert.That(cut.Find("#theme-edit-mode [aria-checked='true']").GetAttribute("data-mode")).IsEqualTo(ThemeStyles.Simple);
	}
}
