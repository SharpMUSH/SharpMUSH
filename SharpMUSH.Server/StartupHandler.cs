using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server;

public class StartupHandler(
	ILogger<StartupHandler> logger,
	IExpandedObjectDataService data,
	IOptionsWrapper<SharpMUSHOptions> options,
	IWikiService wikiService,
	IMessageBus messageBus)
	: IHostedService
{
	private const string ServerVersion = "1.0.0";

	/// <summary>
	/// Body of the seeded Help:Markdown Guide page documenting the CommonMark subset
	/// and SharpMUSH-specific extensions the wiki pipeline supports.
	/// Lives at /wiki/help/markdown_guide and is freely editable after seeding.
	/// </summary>
	public const string MarkdownGuideContent = """
		The wiki uses **CommonMark** Markdown with the extensions described below.
		Raw HTML is **disabled** for security — typing `<b>`, `<img>`, or `<script>` renders
		the text literally instead of acting as HTML. Everything you need has a Markdown
		or SharpMUSH equivalent.

		## Basic formatting

		| You type | You get |
		|---|---|
		| `**bold**` | **bold** |
		| `_italic_` | _italic_ |
		| `` `code` `` | `code` |
		| `~~strikethrough~~` | ~~strikethrough~~ |
		| `# Heading` … `###### Heading` | section headings |
		| `> quoted text` | a blockquote |
		| `---` on its own line | a horizontal rule |

		## Lists

		```
		- Unordered item
		- Another item
		    - Nested item

		1. Ordered item
		2. Second item

		- [ ] Task still open
		- [x] Task done
		```

		## Links

		- External: `[link text](https://example.com)`
		- **Wiki links**: `[[Page Name]]` links to a page in this wiki, displaying "Page Name".
		- Custom text: `[[Display Text|Page Name]]`
		- Other namespaces: `[[Help:Getting Started]]` or `[[Character:Some Name]]`
		- Bare URLs like `https://example.com` auto-link.

		## Images

		Upload images via **Insert image** in the editor (or *Admin → Wiki Assets*), then:

		```
		![alt text](/api/wiki-assets/<id>/<file>)
		```

		Size them with an attribute block right after the image — widths and heights are
		plain numbers (pixels) or percentages:

		```
		![SharpMUSH logo](/assets/Logo.svg){width=200 height=100}
		![Banner](/assets/Logo.svg){width=50%}
		```

		Only `width`, `height`, and CSS class names (`{.my-class}`) are honoured in
		attribute blocks; anything else is stripped.

		## Tables

		```
		| Column A | Column B |
		|----------|----------|
		| cell     | cell     |
		```

		## Code blocks

		Fence multi-line code with triple backticks. A language hint after the opening
		fence is preserved for highlighting:

		````
		```mushcode
		@emit Hello, world!
		```
		````

		## Live listing blocks (SharpMUSH extension)

		Directive blocks render **live, always-current listings** when the page is viewed.
		Each block opens with `:::` plus a directive and closes with a bare `:::`:

		```
		::: category lore
		:::

		::: tag magic
		:::

		::: pagelist help
		:::

		::: recent 10
		:::
		```

		- `category <name>` — every page whose Category matches (build "Category:" pages with this)
		- `tag <name>` — every page carrying the tag
		- `pagelist <namespace>` — every page in a namespace (main, help, character, system)
		- `recent <count>` — the most recently edited pages (1–50)

		For example, the five most recently edited pages right now:

		::: recent 5
		:::

		## Page metadata

		The editor's metadata panel (below the text area) sets the page's **Category**,
		**Tags**, and **Published** switch. Unpublished pages are drafts: invisible to
		visitors who aren't logged in, and excluded from listings and the sitemap.
		""";

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Setting server time data.");
		// Initialize uptime data with current time. NextWarningTime and NextPurgeTime
		// will be managed by ScheduledTaskManagementService based on configuration.
		await data.SetExpandedServerDataAsync(new UptimeData(
			StartTime: DateTimeOffset.UtcNow,
			LastRebootTime: DateTimeOffset.Now,
			Reboots: 0,
			NextWarningTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1),
			NextPurgeTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1)
		));

		var existingMotd = await data.GetExpandedServerDataAsync<MotdData>();
		if (existingMotd is null)
		{
			logger.LogInformation("Seeding default MOTD data.");
			await data.SetExpandedServerDataAsync(new MotdData());
		}
		else
		{
			logger.LogDebug("Default MOTD data already present; skipping seeding.");
		}

		// Seed the "home" wiki page. CreateAsync is a no-op if the slug already exists, so
		// this is safe on every restart.
		var homeResult = await wikiService.CreateAsync(
			title: "Home",
			markdown: """
				![SharpMUSH logo](/assets/Logo.svg){width=20%}
				
				This is your MUSH's home page. It's stored as a wiki article and can be edited
				by any authorised user.

				## Getting started
				- Connect with a MU* client on port **4201**
				- Or use the terminal panel below
				- Create a character with `create <name> <password>`
				- Then log in with `connect <name> <password>`

				## About SharpMUSH
				SharpMUSH is a modern, open-source MUSH server written in .NET, targeting
				PennMUSH compatibility. See the [wiki](/wiki/wiki-index) for more information.
				""",
			authorDbref: "#1",
			ns: WikiNamespace.Main);
		homeResult.Switch(
			page => logger.LogInformation("Home wiki page seeded (id={Id}).", page.Id),
			err => logger.LogDebug("Home wiki page already exists; skipping seed. ({Msg})", err.Value));

		// Seed the Markdown formatting guide in the Help namespace. Like the home page,
		// CreateAsync rejects duplicate slugs, so re-seeding on restart is a no-op and
		// admin edits to the page survive.
		var guideResult = await wikiService.CreateAsync(
			title: "Markdown Guide",
			markdown: MarkdownGuideContent,
			authorDbref: "#1",
			ns: WikiNamespace.Help);
		guideResult.Switch(
			page => logger.LogInformation("Markdown Guide wiki page seeded (id={Id}).", page.Id),
			err => logger.LogDebug("Markdown Guide wiki page already exists; skipping seed. ({Msg})", err.Value));

		logger.LogInformation("Initializing configurable aliases and restrictions from database.");
		var currentOptions = options.CurrentValue;
		Configurable.Initialize(currentOptions.Alias, currentOptions.Restriction);
		Configurable.FloatPrecision = (int)currentOptions.Cosmetic.FloatPrecision;

		// Notify ConnectionServer that the main process is ready
		logger.LogInformation("Publishing MainProcessReadyMessage to ConnectionServer.");
		await messageBus.Publish(new MainProcessReadyMessage(DateTimeOffset.UtcNow, ServerVersion), cancellationToken);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Notify ConnectionServer that the main process is shutting down
		logger.LogInformation("Publishing MainProcessShutdownMessage to ConnectionServer.");
		try
		{
			await messageBus.Publish(new MainProcessShutdownMessage(DateTimeOffset.UtcNow, "Server shutting down"), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			logger.LogDebug("Shutdown message publishing cancelled.");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to publish MainProcessShutdownMessage during shutdown");
		}
	}
}