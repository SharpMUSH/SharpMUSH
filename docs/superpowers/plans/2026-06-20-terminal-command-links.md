# Terminal Command Links Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render markup links in the terminal as command-invocation links (click runs a MUSH command, e.g. `help topic`) distinct from navigation links (click opens a URL), across HTML/Pueblo/MXP/BBCode/ANSI.

**Architecture:** Add an explicit `LinkKind` (Url/Command) to the shared markup layer (`AnsiStructure`). Each format renderer branches on it (HTML `xch_cmd` vs `href`; Pueblo `XCH_CMD` vs `HREF`; MXP `SEND` vs `A`; BBCode/ANSI command→plain text). The markdown renderer tags help-topic `[topic]` links as commands and ordinary links/autolinks as URLs, carrying markdown link titles as hints. The WASM terminal intercepts clicks on `a[xch_cmd]` via a JS delegate that calls back into Blazor to send the command.

**Tech Stack:** .NET 10, C# (tabs, indent size 2), Blazor WASM, MudBlazor, Markdig, System.Text.Json, TUnit tests, JS interop.

## Global Constraints

- C# files: tabs, indent size 2. Razor files: spaces, indent size 4.
- `TreatWarningsAsErrors` is enabled in every project — code must compile clean.
- Prefer `var`; no `this.` qualifier.
- Test framework is TUnit. Run tests with: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/<Class>/<Method>"`.
- Markup link attribute values are HTML-encoded with `System.Net.WebUtility.HtmlEncode` (existing convention — keep it).
- `LinkKind.Url` MUST be enum value `0` so legacy serialized markup (no field) deserializes to navigation behavior.
- Branch: `feature/terminal-command-links` (already created off `origin/main`).

---

## File Structure

- `SharpMUSH.MarkupString/Markup/Markup.cs` — owns `LinkKind`, the `AnsiStructure.LinkKind` field, the `AnsiMarkup.Create` parameter, and all format renderers (`WrapAsHtmlClass`, `WrapAsPueblo`, `WrapAsMxp`, `WrapAsBBCode`, `ApplyDetails`).
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicInlineParser.cs` — tags `[topic]` links with a command-marker data key.
- `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.Inlines.cs` — maps the marker + markdown title to `LinkKind`/hint.
- `SharpMUSH.Client/wwwroot/js/mush-monaco.js` — `SharpMUSH.Terminal.attachCommandLinks`.
- `SharpMUSH.Client/Components/GlobalTerminal.razor` — DotNetObjectReference + `[JSInvokable] RunCommandLinkAsync` + lifecycle.
- `SharpMUSH.Client/wwwroot/css/custom.css` — `.ms-cmd-link` style.
- `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs` — new test file (data model + serialization + all renderers).
- `SharpMUSH.Tests/Documentation/RecursiveMarkdownRendererTests.cs` — add producer tests; update one existing test.

---

## Task 1: LinkKind data model + serialization

**Files:**
- Modify: `SharpMUSH.MarkupString/Markup/Markup.cs` (add enum ~line 10; field in `AnsiStructure` ~line 20; param in `Create` ~line 85-116)
- Test: `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs` (create)

**Interfaces:**
- Produces:
  - `enum MarkupString.MarkupImplementation.LinkKind { Url = 0, Command = 1 }`
  - `AnsiStructure.LinkKind { get; init; }` (type `LinkKind`)
  - `AnsiMarkup.Create(..., LinkKind linkKind = LinkKind.Url, ...)` — new optional parameter (added after the existing `linkUrl` parameter)

- [ ] **Step 1: Write the failing tests**

Create `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs`:

```csharp
using MarkupString;
using MarkupString.MarkupImplementation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Tests for LinkKind (command vs navigation links) across the data model,
/// serialization, and every output renderer.
/// </summary>
public class LinkKindRenderTests
{
	[Test]
	public async Task Create_DefaultLinkKind_IsUrl()
	{
		var markup = AnsiMarkup.Create(linkUrl: "https://example.com");
		await Assert.That(markup.Details.LinkKind).IsEqualTo(LinkKind.Url);
	}

	[Test]
	public async Task Create_CommandLinkKind_IsPreserved()
	{
		var markup = AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command);
		await Assert.That(markup.Details.LinkKind).IsEqualTo(LinkKind.Command);
	}

	[Test]
	public async Task Serialization_LinkKind_RoundTripsJson()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		await Assert.That(json).Contains("LinkKind");

		// Deserialise then re-serialise: the LinkKind survives the round-trip unchanged.
		var back = MModule.deserialize(json);
		await Assert.That(MModule.serialize(back)).IsEqualTo(json);
	}
}
```

> These three tests are rendering-independent, so Task 1's suite is fully green at commit time. The serialization tests whose assertions depend on the HTML renderer (command→`xch_cmd`, legacy→`href`) live in Task 2, where that renderer distinguishes the kinds.

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: FAIL — does not compile (`LinkKind` type and `linkKind` parameter do not exist yet).

- [ ] **Step 3: Add the enum**

In `SharpMUSH.MarkupString/Markup/Markup.cs`, immediately after the `namespace MarkupString.MarkupImplementation;` line (line 8) and before the `AnsiStructure` doc comment, add:

```csharp
/// <summary>
/// Distinguishes a link that runs a MUSH command when clicked (e.g. "help topic")
/// from one that navigates to a URL. <see cref="Url"/> is value 0 and the default so
/// legacy markup with no LinkKind deserialises to navigation behaviour.
/// </summary>
public enum LinkKind { Url = 0, Command = 1 }
```

- [ ] **Step 4: Add the field to AnsiStructure**

In the `AnsiStructure` record (after the `LinkUrl` line, currently line 20), add:

```csharp
    public LinkKind LinkKind       { get; init; }
```

- [ ] **Step 5: Add the parameter to AnsiMarkup.Create**

In `AnsiMarkup.Create` (lines 85-116), add a parameter after `string? linkUrl = null,`:

```csharp
        LinkKind   linkKind    = LinkKind.Url,
```

and in the returned `new AnsiStructure { ... }`, after the `LinkUrl = linkUrl ?? string.Empty,` line, add:

```csharp
            LinkKind      = linkKind,
```

- [ ] **Step 6: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: PASS — all 3 tests pass (they assert on the data model and JSON round-trip, not on rendering).

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.MarkupString/Markup/Markup.cs SharpMUSH.Tests/Markup/LinkKindRenderTests.cs
git commit -m "feat(markup): add LinkKind to AnsiStructure for command vs navigation links

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: HTML renderer (command vs URL + hint)

**Files:**
- Modify: `SharpMUSH.MarkupString/Markup/Markup.cs` — `WrapAsHtmlClass` (lines 144-165)
- Test: `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs` (add methods)

**Interfaces:**
- Consumes: `LinkKind`, `AnsiStructure.LinkKind`, `AnsiStructure.LinkText` (hint), `AnsiMarkup.Create(..., linkKind, linkText)`.
- Produces: HTML output — command → `<a class="ms-cmd-link" xch_cmd="{enc}"[ title="{enc}"]>text</a>`; url → `<a href="{enc}" target="_blank" rel="noopener noreferrer"[ title="{enc}"]>text</a>`.

- [ ] **Step 1: Write the failing tests**

Add to `LinkKindRenderTests.cs` (inside the class):

```csharp
	[Test]
	public async Task Html_CommandLink_UsesXchCmdNotHref()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"help topic\"");
		await Assert.That(html).Contains("ms-cmd-link");
		await Assert.That(html).Contains(">topic</a>");
		await Assert.That(html).DoesNotContain("href=");
	}

	[Test]
	public async Task Html_UrlLink_UsesHrefWithNewTab()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var html = ms.Render("html");

		await Assert.That(html).Contains("href=\"https://example.com\"");
		await Assert.That(html).Contains("target=\"_blank\"");
		await Assert.That(html).Contains("rel=\"noopener noreferrer\"");
		await Assert.That(html).DoesNotContain("xch_cmd");
	}

	[Test]
	public async Task Html_CommandLink_WithHint_EmitsTitle()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who is online?"),
			"who");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"+who\"");
		await Assert.That(html).Contains("title=\"Who is online?\"");
	}

	[Test]
	public async Task Html_LinkAttributes_AreHtmlEncoded()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "say \"hi\" & <bye>", linkKind: LinkKind.Command), "x");
		var html = ms.Render("html");

		await Assert.That(html).Contains("xch_cmd=\"say &quot;hi&quot; &amp; &lt;bye&gt;\"");
	}

	[Test]
	public async Task Serialization_CommandLink_SurvivesAndRendersXchCmd()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		var back = MModule.deserialize(json);

		// Command kind survives the round-trip: HTML render emits xch_cmd, not href.
		await Assert.That(back.Render("html")).Contains("xch_cmd=\"help topic\"");
		await Assert.That(back.Render("html")).DoesNotContain("href=");
	}

	[Test]
	public async Task Serialization_LegacyPayloadWithoutLinkKind_DefaultsToUrl()
	{
		var cmd = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var json = MModule.serialize(cmd);

		// Simulate a pre-LinkKind payload by stripping the property entirely.
		var legacy = System.Text.RegularExpressions.Regex.Replace(json, "\"LinkKind\":\\d+,?", "");
		legacy = legacy.Replace(",}", "}");

		var back = MModule.deserialize(legacy);

		// Missing field => default(LinkKind) == Url => navigation rendering.
		await Assert.That(back.Render("html")).Contains("href=\"help topic\"");
		await Assert.That(back.Render("html")).DoesNotContain("xch_cmd");
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: the four `Html_*` tests and the two `Serialization_*` tests added in this step FAIL (current renderer emits `<a href="help topic">` for everything — no `xch_cmd`, no `target`); the three Task 1 tests still PASS.

- [ ] **Step 3: Implement the renderer change**

In `WrapAsHtmlClass` (lines 154-157), replace the hyperlink block:

```csharp
        // Wrap hyperlink
        string inner = text;
        if (d.LinkUrl is { Length: > 0 } url)
            inner = $"<a href=\"{WebUtility.HtmlEncode(url)}\">{text}</a>";
```

with:

```csharp
        // Wrap hyperlink. Command links run a MUSH command (xch_cmd, intercepted by the
        // WASM terminal); URL links navigate in a new tab.
        string inner = text;
        if (d.LinkUrl is { Length: > 0 } url)
        {
            var title = d.LinkText is { Length: > 0 } hint
                ? $" title=\"{WebUtility.HtmlEncode(hint)}\""
                : "";
            inner = d.LinkKind == LinkKind.Command
                ? $"<a class=\"ms-cmd-link\" xch_cmd=\"{WebUtility.HtmlEncode(url)}\"{title}>{text}</a>"
                : $"<a href=\"{WebUtility.HtmlEncode(url)}\" target=\"_blank\" rel=\"noopener noreferrer\"{title}>{text}</a>";
        }
```

- [ ] **Step 4: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: PASS — all Task 1 + Task 2 tests pass (including `Serialization_CommandLink_RoundTrips`).

- [ ] **Step 5: Run the broader HTML render suite for regressions**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RenderFormatTests/*"`
Expected: PASS — no existing HTML render test asserts link markup, so adding `target`/`rel` does not break them.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.MarkupString/Markup/Markup.cs SharpMUSH.Tests/Markup/LinkKindRenderTests.cs
git commit -m "feat(markup): render HTML command links as xch_cmd, URL links as href target=_blank

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Pueblo + MXP renderers

**Files:**
- Modify: `SharpMUSH.MarkupString/Markup/Markup.cs` — `WrapAsPueblo` (lines 176-177), `WrapAsMxp` (lines 205-206)
- Test: `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs` (add methods)

**Interfaces:**
- Consumes: `LinkKind`, `AnsiStructure.LinkText`.
- Produces: Pueblo — command → `<A XCH_CMD="{enc}"[ XCH_HINT="{enc}"]>`, url → `<A HREF="{enc}">`. MXP — command → `<SEND HREF="{enc}"[ HINT="{enc}"]>`, url → `<A HREF="{enc}">`.

- [ ] **Step 1: Write the failing tests**

Add to `LinkKindRenderTests.cs`:

```csharp
	[Test]
	public async Task Pueblo_CommandLink_UsesXchCmd()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "who");
		var pueblo = ms.Render("pueblo");

		await Assert.That(pueblo).Contains("<A XCH_CMD=\"+who\"");
		await Assert.That(pueblo).Contains("XCH_HINT=\"Who?\"");
		await Assert.That(pueblo).DoesNotContain("HREF=");
	}

	[Test]
	public async Task Pueblo_UrlLink_UsesHref()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var pueblo = ms.Render("pueblo");

		await Assert.That(pueblo).Contains("<A HREF=\"https://example.com\">");
		await Assert.That(pueblo).DoesNotContain("XCH_CMD");
	}

	[Test]
	public async Task Mxp_CommandLink_UsesSend()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "+who", linkKind: LinkKind.Command, linkText: "Who?"), "who");
		var mxp = ms.Render("mxp");

		await Assert.That(mxp).Contains("<SEND HREF=\"+who\"");
		await Assert.That(mxp).Contains("HINT=\"Who?\"");
	}

	[Test]
	public async Task Mxp_UrlLink_UsesAnchorNotSend()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var mxp = ms.Render("mxp");

		await Assert.That(mxp).Contains("<A HREF=\"https://example.com\">");
		await Assert.That(mxp).DoesNotContain("<SEND");
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/Pueblo_*"` then the `Mxp_*` filter.
Expected: FAIL — Pueblo currently always emits `<A HREF>`; MXP currently always emits `<SEND HREF>`.

- [ ] **Step 3: Implement WrapAsPueblo**

In `WrapAsPueblo`, replace lines 176-177:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"<A HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</A>";
```

with:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)
        {
            if (d.LinkKind == LinkKind.Command)
            {
                var hint = d.LinkText is { Length: > 0 } h
                    ? $" XCH_HINT=\"{WebUtility.HtmlEncode(h)}\""
                    : "";
                t = $"<A XCH_CMD=\"{WebUtility.HtmlEncode(url)}\"{hint}>{t}</A>";
            }
            else
            {
                t = $"<A HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</A>";
            }
        }
```

- [ ] **Step 4: Implement WrapAsMxp**

In `WrapAsMxp`, replace lines 205-206:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"<SEND HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</SEND>";
```

with:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)
        {
            if (d.LinkKind == LinkKind.Command)
            {
                var hint = d.LinkText is { Length: > 0 } h
                    ? $" HINT=\"{WebUtility.HtmlEncode(h)}\""
                    : "";
                t = $"<SEND HREF=\"{WebUtility.HtmlEncode(url)}\"{hint}>{t}</SEND>";
            }
            else
            {
                t = $"<A HREF=\"{WebUtility.HtmlEncode(url)}\">{t}</A>";
            }
        }
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: PASS — all LinkKind tests pass.

- [ ] **Step 6: Run existing Pueblo/MXP suite for regressions**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/PuebloMxpRenderTests/*"`
Expected: PASS — existing tests use `HtmlMarkup.Create("send", ...)` (raw HTML markup), not `AnsiStructure.LinkUrl`, so they are unaffected.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.MarkupString/Markup/Markup.cs SharpMUSH.Tests/Markup/LinkKindRenderTests.cs
git commit -m "feat(markup): Pueblo XCH_CMD and MXP SEND for command links vs anchors for URLs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: BBCode + ANSI/OSC8 renderers

**Files:**
- Modify: `SharpMUSH.MarkupString/Markup/Markup.cs` — `WrapAsBBCode` (lines 191-192), `ApplyDetails` (line 222)
- Test: `SharpMUSH.Tests/Markup/LinkKindRenderTests.cs` (add methods)

**Interfaces:**
- Consumes: `LinkKind`.
- Produces: BBCode — url → `[url={url}]text[/url]`, command → plain `text` (no wrap). ANSI — url → OSC8 link (unchanged), command → plain `text` (no OSC8).

- [ ] **Step 1: Write the failing tests**

Add to `LinkKindRenderTests.cs`:

```csharp
	[Test]
	public async Task Ansi_CommandLink_RendersPlainTextNoOsc8()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var ansi = ms.Render("ansi");

		await Assert.That(ansi).DoesNotContain("]8;;");
		await Assert.That(ansi.Contains("topic")).IsTrue();
	}

	[Test]
	public async Task Ansi_UrlLink_StillRendersOsc8()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var ansi = ms.Render("ansi");

		await Assert.That(ansi).Contains("]8;;https://example.com");
	}

	[Test]
	public async Task BBCode_UrlLink_UsesUrlTag()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "https://example.com", linkKind: LinkKind.Url), "site");
		var bbcode = ms.Render("bbcode");

		await Assert.That(bbcode).Contains("[url=https://example.com]");
	}

	[Test]
	public async Task BBCode_CommandLink_RendersPlainText()
	{
		var ms = MModule.MarkupSingle(
			AnsiMarkup.Create(linkUrl: "help topic", linkKind: LinkKind.Command), "topic");
		var bbcode = ms.Render("bbcode");

		await Assert.That(bbcode).DoesNotContain("[url=");
		await Assert.That(bbcode.Contains("topic")).IsTrue();
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/Ansi_*"` then `BBCode_*`.
Expected: FAIL — ANSI currently emits OSC8 for command links; BBCode currently emits `[url=]` for command links.

- [ ] **Step 3: Implement WrapAsBBCode**

In `WrapAsBBCode`, replace lines 191-192:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)
            t = $"[url={url}]{t}[/url]";
```

with:

```csharp
        // BBCode has no command-link concept — only URL links are wrapped.
        if (d.LinkUrl is { Length: > 0 } url && d.LinkKind == LinkKind.Url)
            t = $"[url={url}]{t}[/url]";
```

- [ ] **Step 4: Implement ApplyDetails (ANSI/OSC8)**

In `ApplyDetails`, replace line 222:

```csharp
        if (d.LinkUrl is { Length: > 0 } url)  s = s.LinkANSI(url);
```

with:

```csharp
        // OSC 8 hyperlinks can only navigate, so command links render as plain text.
        if (d.LinkUrl is { Length: > 0 } url && d.LinkKind == LinkKind.Url)  s = s.LinkANSI(url);
```

- [ ] **Step 5: Run tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"`
Expected: PASS — all LinkKind tests pass.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.MarkupString/Markup/Markup.cs SharpMUSH.Tests/Markup/LinkKindRenderTests.cs
git commit -m "feat(markup): command links render as plain text in BBCode and ANSI/OSC8

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Markdown producer — tag help-topic links as commands, carry titles as hints

**Files:**
- Modify: `SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicInlineParser.cs` (add const + `SetData`)
- Modify: `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.Inlines.cs` — `RenderLink` (lines 63-90)
- Test: `SharpMUSH.Tests/Documentation/RecursiveMarkdownRendererTests.cs` (add methods; update one existing test)

**Interfaces:**
- Consumes: `LinkKind` (from `MarkupString.MarkupImplementation`), `Ansi.Create(linkUrl, linkKind, linkText)`.
- Produces: `HelpTopicInlineParser.CommandDataKey` (public const string); help-topic links → `LinkKind.Command`; ordinary links/autolinks → `LinkKind.Url`; markdown link title → hint.

- [ ] **Step 1: Update the one existing test that changes behavior, and write new tests**

In `SharpMUSH.Tests/Documentation/RecursiveMarkdownRendererTests.cs`, REPLACE the body of `RenderHelpTopicLink_ShouldCreateHelpUrl` (lines ~519-533) with:

```csharp
	[Test]
	public async Task RenderHelpTopicLink_ShouldCreateCommandLink()
	{
		// Arrange - bare [topic] with no URL definition is parsed by HelpTopicInlineParser
		// into a command LinkInline whose URL is "help <topic>".
		var markdown = "See [newbie2] for more.";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - plain text contains the topic name without brackets
		await Assert.That(result.ToPlainText()).IsEqualTo("See newbie2 for more.");

		// It is a command link: HTML renders xch_cmd (not href), ANSI has no OSC 8 link.
		await Assert.That(result.Render("html")).Contains("xch_cmd=\"help newbie2\"");
		await Assert.That(result.ToString()).DoesNotContain("]8;;");
	}

	[Test]
	public async Task RenderRegularLink_ShouldBeNavigationLink()
	{
		// Arrange - an explicit [text](url) link is a navigation link, not a command.
		var markdown = "[Site](https://example.com)";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.Render("html")).Contains("href=\"https://example.com\"");
		await Assert.That(result.Render("html")).Contains("target=\"_blank\"");
		await Assert.That(result.Render("html")).DoesNotContain("xch_cmd");
	}

	[Test]
	public async Task RenderLink_WithTitle_CarriesHint()
	{
		// Arrange - markdown link title becomes the link hint (HTML title / xch_hint / MXP HINT).
		var markdown = "[Site](https://example.com \"Open the site\")";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.Render("html")).Contains("title=\"Open the site\"");
	}
```

Note: `RenderLink_ShouldShowTextAndUrl` (line ~285, uses `[Link Text](https://example.com)`) is a regular URL link and stays OSC8 — leave it unchanged.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderHelpTopicLink_ShouldCreateCommandLink"`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderRegularLink_ShouldBeNavigationLink"`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderLink_WithTitle_CarriesHint"`
Expected: FAIL — the renderer currently produces `Ansi.Create(linkUrl: url)` with default `LinkKind.Url` for everything and ignores titles, so help links still render OSC8/`href` and no `title` is emitted.

- [ ] **Step 3: Add the command-marker key and tag help-topic links**

In `SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicInlineParser.cs`, add a public const inside the class (after the `OpeningCharacters` constructor, before `Match`):

```csharp
	/// <summary>
	/// Markdig data key set on the <see cref="LinkInline"/> nodes this parser produces, marking
	/// them as command links (run "help &lt;topic&gt;") rather than URL navigation links.
	/// </summary>
	public const string CommandDataKey = "sharpmush:command-link";
```

Then, in `Match`, right before `processor.Inline = link;` (line 87), add:

```csharp
		link.SetData(CommandDataKey, true);
```

- [ ] **Step 4: Map marker + title to LinkKind/hint in RenderLink**

In `SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.Inlines.cs`:

First, add a using at the top (after the existing `using SharpMUSH.Library.Services;`, line 3):

```csharp
using MarkupString.MarkupImplementation;
```

Then replace the link-creation block in `RenderLink` (lines 87-89):

```csharp
		// Create hyperlink markup with linkUrl parameter
		var linkMarkup = Ansi.Create(linkUrl: url);
		return MModule.MarkupSingle(linkMarkup, contentText);
```

with:

```csharp
		// Help-topic shortcuts ([topic]) are command links; ordinary links navigate.
		// A markdown link title ([text](url "title")) becomes the link hint.
		var isCommand = link.GetData(HelpTopicInlineParser.CommandDataKey) is true;
		var hint = string.IsNullOrWhiteSpace(link.Title) ? null : link.Title;
		var linkMarkup = Ansi.Create(
			linkUrl: url,
			linkKind: isCommand ? LinkKind.Command : LinkKind.Url,
			linkText: hint);
		return MModule.MarkupSingle(linkMarkup, contentText);
```

(`RenderAutolink` already calls `Ansi.Create(linkUrl: ...)` with the default `LinkKind.Url` — no change needed.)

- [ ] **Step 5: Run the new/updated tests**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderHelpTopicLink_ShouldCreateCommandLink"`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderRegularLink_ShouldBeNavigationLink"`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/RenderLink_WithTitle_CarriesHint"`
Expected: PASS.

- [ ] **Step 6: Run the full markdown + markdown-function suites for regressions**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/*"`
Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MarkdownFunctionUnitTests/*"`
Expected: PASS — the URL-link and autolink tests (`RenderLink_ShouldShowTextAndUrl`, `RenderMarkdown_Link_*`) stay `LinkKind.Url` and keep their OSC8 assertions.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Documentation/MarkdownToAsciiRenderer/HelpTopicInlineParser.cs \
        SharpMUSH.Documentation/MarkdownToAsciiRenderer/RecursiveMarkdownRenderer.Inlines.cs \
        SharpMUSH.Tests/Documentation/RecursiveMarkdownRendererTests.cs
git commit -m "feat(docs): help-topic markdown links become command links; titles become hints

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: WASM terminal — intercept command-link clicks and send the command

**Files:**
- Modify: `SharpMUSH.Client/wwwroot/js/mush-monaco.js` — extend `SharpMUSH.Terminal` (lines 426-431)
- Modify: `SharpMUSH.Client/Components/GlobalTerminal.razor` — usings, fields, `OnAfterRenderAsync`, `[JSInvokable]`, `DisposeAsync`
- Modify: `SharpMUSH.Client/wwwroot/css/custom.css` — add `.ms-cmd-link` rule (after line 762)

**Interfaces:**
- Consumes: `ITerminalService.SendAsync(string)`; rendered HTML `<a class="ms-cmd-link" xch_cmd="...">` from Task 2.
- Produces: `SharpMUSH.Terminal.attachCommandLinks(outputId, dotNetRef)` (JS, returns `{ dispose() }`); `GlobalTerminal.RunCommandLinkAsync(string)` (`[JSInvokable]`).

- [ ] **Step 1: Add the JS click delegate**

In `SharpMUSH.Client/wwwroot/js/mush-monaco.js`, replace the `window.SharpMUSH.Terminal = { ... };` block (lines 426-431) with:

```javascript
    window.SharpMUSH.Terminal = {
        scrollToBottom: function (elementId) {
            var el = document.getElementById(elementId);
            if (el) el.scrollTop = el.scrollHeight;
        },

        // Registers a delegated click handler on the terminal output container.
        // Clicking a command link (<a xch_cmd="...">) runs the command via .NET
        // (RunCommandLinkAsync) instead of navigating. Plain <a href> links are left
        // untouched so they open normally. Returns a disposable for cleanup.
        attachCommandLinks: function (outputId, dotNetRef) {
            var el = document.getElementById(outputId);
            if (!el) return { dispose: function () { } };

            function onClick(e) {
                var a = e.target.closest('a[xch_cmd]');
                if (!a || !el.contains(a)) return;
                e.preventDefault();
                var cmd = a.getAttribute('xch_cmd');
                if (cmd) dotNetRef.invokeMethodAsync('RunCommandLinkAsync', cmd);
            }

            el.addEventListener('click', onClick);
            return {
                dispose: function () { el.removeEventListener('click', onClick); },
            };
        },
    };
```

- [ ] **Step 2: Add the CSS for command links**

In `SharpMUSH.Client/wwwroot/css/custom.css`, after the `.ms-blink` rule and its `@keyframes` (after line 766), add:

```css
/* Command links (xch_cmd) — clickable, run a MUSH command. Distinct from URL links. */
.ms-cmd-link {
    color: var(--primary, #6cf);
    text-decoration: underline;
    text-decoration-style: dotted;
    cursor: pointer;
}
.ms-cmd-link:hover {
    text-decoration-style: solid;
}
```

- [ ] **Step 3: Wire up the component**

In `SharpMUSH.Client/Components/GlobalTerminal.razor`:

(a) After the existing `@using` lines (after line 4 `@using Microsoft.AspNetCore.Components.WebAssembly.Hosting`), add:

```razor
@using Microsoft.JSInterop
```

(b) In `@code`, add fields next to the other private fields (after line 234 `private bool _showCharacterPicker;`):

```csharp
    private DotNetObjectReference<GlobalTerminal>? _selfRef;
    private IJSObjectReference? _cmdLinkHandle;
```

(c) Replace `OnAfterRenderAsync` (lines 317-321):

```csharp
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_autoScroll)
            await ScrollToBottomAsync();
    }
```

with:

```csharp
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _selfRef = DotNetObjectReference.Create(this);
            try
            {
                _cmdLinkHandle = await JsRuntime.InvokeAsync<IJSObjectReference>(
                    "SharpMUSH.Terminal.attachCommandLinks", _outputId, _selfRef);
            }
            catch { /* ignore if JS not ready */ }
        }

        if (_autoScroll)
            await ScrollToBottomAsync();
    }

    /// <summary>
    /// Invoked from JS when a command link (&lt;a xch_cmd="..."&gt;) is clicked in the
    /// terminal output. Sends the command exactly as if the player had typed it.
    /// </summary>
    [JSInvokable]
    public async Task RunCommandLinkAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || !_connected) return;
        await Terminal.SendAsync(command);
    }
```

(d) Replace `DisposeAsync` (lines 506-511):

```csharp
    public async ValueTask DisposeAsync()
    {
        Terminal.LineReceived -= OnLineReceived;
        Terminal.ConnectionStateChanged -= OnConnectionChanged;
        await Task.CompletedTask;
    }
```

with:

```csharp
    public async ValueTask DisposeAsync()
    {
        Terminal.LineReceived -= OnLineReceived;
        Terminal.ConnectionStateChanged -= OnConnectionChanged;

        if (_cmdLinkHandle is not null)
        {
            try { await _cmdLinkHandle.InvokeVoidAsync("dispose"); } catch { /* ignore */ }
            try { await _cmdLinkHandle.DisposeAsync(); } catch { /* ignore */ }
        }
        _selfRef?.Dispose();
    }
```

- [ ] **Step 4: Build the client to verify it compiles**

Run: `dotnet build SharpMUSH.Client`
Expected: Build succeeded, 0 warnings (warnings are errors).

- [ ] **Step 5: Manual end-to-end verification (JS interop cannot be unit-tested here)**

Start infrastructure and both servers, then the client:

```bash
docker compose up -d
dotnet run --project SharpMUSH.Server &
dotnet run --project SharpMUSH.ConnectionServer &
dotnet run --project SharpMUSH.Client
```

In the browser terminal (dev auto-connects as the wizard):
1. Type `help` and press Enter.
2. Confirm topic links (e.g. `[ATTRIBUTES]`) render underlined-dotted (`.ms-cmd-link`) and have NO normal-link cursor behavior of navigating.
3. Click a topic link → the terminal runs `help <topic>` (new help page appears) and the browser does NOT navigate/refresh.
4. Hover a link that has a hint → tooltip shows.
5. If any help text contains a real `[text](https://…)` link, clicking it opens a new browser tab (does not run a command).

Document the observed result (pass/fail per step) in the task notes.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/wwwroot/js/mush-monaco.js \
        SharpMUSH.Client/wwwroot/css/custom.css \
        SharpMUSH.Client/Components/GlobalTerminal.razor
git commit -m "feat(client): intercept terminal command-link clicks and send the command

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Final verification

- [ ] **Run the full markup + documentation test suites:**

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LinkKindRenderTests/*"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RenderFormatTests/*"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/PuebloMxpRenderTests/*"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/RecursiveMarkdownRendererTests/*"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/MarkdownFunctionUnitTests/*"
```

Expected: all PASS.

- [ ] **Build the whole solution (warnings are errors):**

```bash
dotnet build
```

Expected: Build succeeded.

---

## Self-Review Notes (coverage vs. spec)

- Data model (`LinkKind` enum, `AnsiStructure` field, `Create` param, serialization + legacy default) → Task 1.
- Render matrix: HTML → Task 2; Pueblo + MXP → Task 3; BBCode + ANSI/OSC8 → Task 4. Hint (`LinkText` → `title`/`XCH_HINT`/`HINT`) covered in Tasks 2-3.
- Markdown producer (help-topic marker, title→hint, autolink stays URL) → Task 5, including the one existing behavior-changing test update (`RenderHelpTopicLink`).
- Client (JS delegate, `RunCommandLinkAsync`, lifecycle, CSS) → Task 6, verified by running the app.
- Out-of-scope items (softcode functions, MXP line-security prefixes, wiki links) intentionally untouched.
