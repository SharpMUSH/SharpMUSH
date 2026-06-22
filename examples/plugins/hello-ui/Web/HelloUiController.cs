using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelloUiPlugin.Web;

/// <summary>
/// The plugin-owned web surface for the "Hello UI" example application.
///
/// <para>Discovered by the host through the MVC ApplicationPart the plugin registers in
/// <see cref="HelloUiPlugin.RegisterServices"/> (<c>AddControllers().AddApplicationPart(thisAssembly)</c>),
/// so its attribute routes are served by the host's own <c>MapControllers()</c> — the host carries no
/// hello-ui controller, and removing the plugin removes this REST surface entirely.</para>
///
/// <para>It serves the two endpoints the <see cref="SharpMUSH.Library.Models.Portal.Applications.RegisteredApplication"/>
/// descriptor points at:</para>
/// <list type="bullet">
///   <item><c>GET /api/hello-ui/schema</c> — a read-only Area-21 Portal Schema Document the WASM client renders
///   generically (snake_case JSON, <c>kind: "view"</c>).</item>
///   <item><c>GET /api/hello-ui/data</c> — the field values the schema's view binds to.</item>
/// </list>
///
/// <para>JSON is hand-rolled as anonymous objects in the snake_case the client's <c>SchemaJson.Options</c>
/// expects, so this example takes no dependency on the client's model types (which a runtime-loaded plugin
/// cannot compile-reference anyway — every client↔plugin boundary is serialization).</para>
/// </summary>
[ApiController]
[Route("api/hello-ui")]
[AllowAnonymous]
public sealed class HelloUiController : ControllerBase
{
	/// <summary>
	/// A minimal read-only Area-21 schema: one section with two read-only fields and a small table. The client
	/// resolves it via the application descriptor's <c>SchemaUrl</c>, sees <c>kind: "view"</c>, and renders it
	/// through its read-only view renderer — no editable inputs, no submit action.
	/// </summary>
	[HttpGet("schema")]
	public IActionResult GetSchema() => Ok(new
	{
		kind = "view",
		schema_version = 1,
		title = "Hello UI",
		pages = new object[]
		{
			new
			{
				key = "p1",
				title = "Welcome",
				order = 1,
				sections = new object[]
				{
					new
					{
						name = "About this example",
						order = 1,
						columns = 1,
						elements = new object[]
						{
							new
							{
								kind = "markdown",
								value =
									"This page is served **entirely by a managed plugin DLL**. "
									+ "The browser loaded no plugin code — it rendered this view from the "
									+ "schema JSON the plugin's controller returned."
							},
							new
							{
								kind = "field",
								key = "greeting",
								label = "Greeting",
								type = "text"
							},
							new
							{
								kind = "field",
								key = "served_by",
								label = "Served by",
								type = "text"
							},
							new
							{
								kind = "table",
								label = "Plugin seams demonstrated",
								rows_field = "seams",
								columns = new object[]
								{
									new { key = "seam", label = "Seam" },
									new { key = "purpose", label = "Purpose" }
								}
							}
						}
					}
				}
			}
		}
	});

	/// <summary>
	/// The data values the view binds to. Each field is <c>{ value, visible }</c>; the table's rows are a JSON
	/// array under the key the table element's <c>rows_field</c> names (<c>seams</c>).
	/// </summary>
	[HttpGet("data")]
	public IActionResult GetData() => Ok(new
	{
		fields = new Dictionary<string, object>
		{
			["greeting"] = new { value = "Hello from the hello-ui managed package!", visible = true },
			["served_by"] = new { value = "HelloUiController (api/hello-ui/data)", visible = true },
			["seams"] = new
			{
				value = new object[]
				{
					new { seam = "IServiceRegistrar", purpose = "AddControllers().AddApplicationPart(thisAssembly)" },
					new { seam = "IApplicationSource", purpose = "Contributes the /apps/hello-ui page + NavBar entry" },
					new { seam = "kind: managed package", purpose = "Distributes the DLL with a SHA-256-verified install" }
				},
				visible = true
			}
		}
	});
}
