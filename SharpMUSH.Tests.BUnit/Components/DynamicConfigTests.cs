using Bunit;
using SharpMUSH.Client.Pages.Admin.Config;
using SharpMUSH.Library.API;
using PropertyMetadata = SharpMUSH.Library.API.PropertyMetadata;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Tests for the DynamicConfig page — schema-driven configuration editing.
/// </summary>
public class DynamicConfigTests
{
	[Test]
	public async Task DynamicConfig_CustomPageCategory_ShowsRoutingError()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "bannednames"));

		await Assert.That(cut.Markup).Contains("RoutingError");
	}

	[Test]
	public async Task DynamicConfig_ValidCategory_ShowsCategoryTitle()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "net"));
		await Task.Delay(200); // wait for async load

		await Assert.That(cut.Markup).Contains("Network Settings");
	}

	[Test]
	public async Task DynamicConfig_ValidCategory_ShowsGroupCard()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "net"));
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("Connection Settings");
	}

	[Test]
	public async Task DynamicConfig_UnknownCategory_ShowsWarning()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "nonexistent"));
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("CategoryNotFound");
	}

	[Test]
	public async Task DynamicConfig_NoGroupsDefined_ShowsFallbackMessage()
	{
		var schema = new ConfigurationSchema
		{
			Categories =
			[
				new CategoryMetadata
				{
					Name = "Test",
					DisplayName = "Test Category",
					Order = 1,
					Groups = [] // No groups
				}
			],
			Properties = new Dictionary<string, PropertyMetadata>
			{
				["Test.Foo"] = new()
				{
					Name = "Foo", DisplayName = "Foo",
					Category = "Test", Type = "string", Component = "text",
					Path = "Test.Foo", Order = 1
				}
			}
		};

		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler(schema);
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "test"));
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("NoGroupingNote");
	}

	[Test]
	public async Task DynamicConfig_ValidCategory_ShowsPropertyFields()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "net"));
		await Task.Delay(200);

		// Should render property display names
		await Assert.That(cut.Markup).Contains("Port Number");
	}

	[Test]
	public async Task DynamicConfig_ValidCategory_ShowsMudNameField()
	{
		await using var ctx = new BunitContext();
		var handler = AdminTestHelpers.CreateConfigApiHandler();
		AdminTestHelpers.SetupAdminContext(ctx, handler);

		var cut = ctx.Render<DynamicConfig>(p => p.Add(x => x.Category, "net"));
		await Task.Delay(200);

		await Assert.That(cut.Markup).Contains("MUD Name");
	}
}
