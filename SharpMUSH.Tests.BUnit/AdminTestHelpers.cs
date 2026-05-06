using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PropertyMetadata = SharpMUSH.Library.API.PropertyMetadata;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// A pass-through string localizer that returns the key as the localized value.
/// </summary>
public class PassThroughStringLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] =>
		new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Helpers for building admin config page test contexts.
/// </summary>
public static class AdminTestHelpers
{
	/// <summary>
	/// Creates a ConfigurationSchema with a single category and a set of properties.
	/// </summary>
	public static ConfigurationSchema CreateNetSchema() => new()
	{
		Categories =
		[
			new CategoryMetadata
			{
				Name = "Net",
				DisplayName = "Network Settings",
				Description = "Configure network and connection options",
				Order = 1,
				Groups =
				[
					new GroupMetadata { Name = "Connection", DisplayName = "Connection Settings", Order = 1 }
				]
			}
		],
		Properties = new Dictionary<string, PropertyMetadata>
		{
			["Net.Port"] = new()
			{
				Name = "Port", DisplayName = "Port Number",
				Description = "The port the MUD listens on",
				Category = "Net", Group = "Connection",
				Type = "integer", Component = "numeric",
				Path = "Net.Port", Required = true,
				Min = 1, Max = 65535, DefaultValue = 4201, Order = 1
			},
			["Net.MudName"] = new()
			{
				Name = "MudName", DisplayName = "MUD Name",
				Description = "The name of the MUD",
				Category = "Net", Group = "Connection",
				Type = "string", Component = "text",
				Path = "Net.MudName", Required = true, Order = 2
			},
			["Net.UseSSL"] = new()
			{
				Name = "UseSSL", DisplayName = "Use SSL",
				Description = "Enable SSL connections",
				Category = "Net", Group = "Connection",
				Type = "boolean", Component = "switch",
				Path = "Net.UseSSL", DefaultValue = false, Order = 3
			}
		}
	};

	/// <summary>
	/// Creates a standard ConfigurationResponse suitable for API mocking.
	/// </summary>
	public static ConfigurationResponse CreateConfigResponse(ConfigurationSchema? schema = null) =>
		new()
		{
			Schema = schema ?? CreateNetSchema(),
			Metadata = new Dictionary<string, SharpConfigAttribute>()
		};

	/// <summary>
	/// Creates a MockApiHandler pre-configured for the configuration endpoint.
	/// </summary>
	public static MockApiHandler CreateConfigApiHandler(ConfigurationSchema? schema = null)
	{
		var response = CreateConfigResponse(schema);
		return new MockApiHandler()
			.OnGet("/api/configuration", response);
	}

	/// <summary>
	/// Registers all admin config services into a BunitContext using a mock API handler.
	/// </summary>
	public static BunitContext SetupAdminContext(BunitContext ctx, MockApiHandler handler)
	{
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();
		ctx.Services.AddSingleton(typeof(IStringLocalizer<>), typeof(PassThroughStringLocalizer<>));

		var factory = handler.CreateFactory();
		ctx.Services.AddSingleton(factory);
		ctx.Services.AddSingleton(new ConfigSchemaService(factory));
		ctx.Services.AddSingleton(new AdminConfigService(
			Substitute.For<ILogger<AdminConfigService>>(), factory));
		ctx.Services.AddSingleton(new SitelockService(factory));
		ctx.Services.AddSingleton(new BannedNamesService(factory));
		ctx.Services.AddSingleton(new RestrictionsService(factory));
		ctx.Services.AddSingleton(new DatabaseConversionService(
			Substitute.For<ILogger<DatabaseConversionService>>(), factory));

		return ctx;
	}
}
