using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Verifies the form renderer's advisory required-field check blocks a final submit and surfaces the
/// error before any POST is attempted (softcode stays the authoritative validator).
/// </summary>
public class SchemaFormRendererTests : BunitContext
{
	public SchemaFormRendererTests()
	{
		// SchemaAppService is required by the component; with validation blocking the submit, no HTTP
		// call is made, so a stub factory suffices.
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(new HttpClient { BaseAddress = new Uri("https://localhost:8081/") });

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(
				sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance));

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static PortalSchemaDocument RequiredNameForm() => new(
		Kind: "form",
		SchemaVersion: 1,
		Title: "Application",
		DataSource: null,
		Pages:
		[
			new SchemaPage("main", null, 1,
			[
				new SchemaSection("About", 1, null,
				[
					new SchemaElement(Kind: "field", Key: "charname", Label: "Character Name", Type: "text",
						Validation: new SchemaValidation(Required: true, null, null, null, null))
				])
			], null, null)
		],
		Actions: new Dictionary<string, SchemaAction>
		{
			["submit"] = new("http", "POST", "/http/chargen/submit", "fields", null,
				new SchemaActionError(BindFieldErrors: true))
		});

	[TUnit.Core.Test]
	public async Task Submit_WithEmptyRequiredField_BlocksAndShowsError()
	{
		var cut = Render<SchemaFormRenderer>(parameters => parameters.Add(p => p.Document, RequiredNameForm()));

		var submit = cut.FindAll("button").First(b => b.TextContent.Contains("Submit"));
		submit.Click();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("required", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("required-field error not shown yet");
			}
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("required");
	}
}
