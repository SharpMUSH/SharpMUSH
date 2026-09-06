using System.Buffers;

public class RendererTests
{
	private sealed record Tag(string Name) : IMarkup;

	private sealed class TagEmitter(MarkupFormat format) : IMarkupEmitter
	{
		public Type MarkupType => typeof(Tag);
		public MarkupFormat Format => format;

		public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext c, IBufferWriter<char> o)
		{
			var n = ((Tag)markup).Name;
			o.Write("<" + n + ">");
			o.Write(body);
			o.Write("</" + n + ">");
		}
	}

	/// <summary>
	/// Claims any run carrying a <c>Tag("o")</c> layer, writing <c>[o]…[/o]</c> around the body
	/// and delegating every other layer to its own emitter, found through
	/// <see cref="EmitContext.Registry"/>.
	/// </summary>
	private sealed class OuterSetEmitter(MarkupFormat format) : IMarkupSetEmitter
	{
		public MarkupFormat Format => format;

		public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext c, IBufferWriter<char> o)
		{
			var claims = false;
			foreach (var m in set)
			{
				if (m is Tag { Name: "o" }) { claims = true; break; }
			}
			if (!claims) return false;

			var inner = new ArrayBufferWriter<char>(body.Length + 16);
			inner.Write(body);
			for (var i = 0; i < set.Count; i++)
			{
				if (set[i] is Tag { Name: "o" }) continue;
				var emitter = c.Registry.FindEmitter(set[i].GetType(), c.Format);
				if (emitter is null) continue;
				var next = new ArrayBufferWriter<char>(inner.WrittenCount + 16);
				emitter.Emit(set[i], inner.WrittenSpan, c, next);
				inner = next;
			}

			o.Write("[o]");
			o.Write(inner.WrittenSpan);
			o.Write("[/o]");
			return true;
		}
	}

	private sealed class BracketFramer(MarkupFormat format) : IFormatFramer
	{
		public MarkupFormat Format => format;
		public void WritePreamble(IBufferWriter<char> output) => output.Write("{");
		public void WriteEpilogue(bool anyRunEmitted, IBufferWriter<char> output) => output.Write(anyRunEmitted ? "}!" : "}");
	}

	[Test]
	public async Task Render_GapThenRun_EmitsAllText()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var t = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("ab<b>c</b>");
	}

	[Test]
	public async Task Render_NestedSet_WrapsInnermostFirst()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var t = MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new Tag("i"), "x"));
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("<o><i>x</i></o>");
	}

	[Test]
	public async Task Render_UnregisteredMarkup_FallsBackToBody()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Html, MarkupRegistry.Empty)).IsEqualTo("x");

	[Test]
	public async Task Render_HtmlEncodesText()
		=> await Assert.That(MarkupText.Plain("<&>").Render(MarkupFormat.Html, MarkupRegistry.Empty)).IsEqualTo("&lt;&amp;&gt;");

	[Test]
	public async Task Render_PlainStripsControls()
		=> await Assert.That(MarkupText.Plain("a\u001b[31mb\tc").Render(MarkupFormat.Plain, MarkupRegistry.Empty)).IsEqualTo("a[31mb\tc");

	[Test]
	public async Task Render_AnsiFormatKeepsControls()
		=> await Assert.That(MarkupText.Plain("a\u001b[31mb").Render(MarkupFormat.Ansi, MarkupRegistry.Empty)).IsEqualTo("a\u001b[31mb");

	[Test]
	public async Task Render_CustomFormat_IsNotAnsi()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Custom("bogus", TextEncoding.None), MarkupRegistry.Empty)).IsEqualTo("x");

	[Test]
	public async Task Render_WithoutDefaultRegistry_Throws()
	{
		// The test assembly never assigns MarkupRegistry.Default, so the getter must throw.
		await Assert.That(MarkupRegistry.IsConfigured).IsFalse();
		await Assert.That(() => MarkupText.Plain("x").Render(MarkupFormat.Html)).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task Equals_InFormat_ComparesRenderedOutput()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var a = MarkupText.Wrap(new Tag("b"), "x");
		var b = MarkupText.Plain("x");
		await Assert.That(a.Equals(b, MarkupFormat.Plain, reg)).IsTrue();
		await Assert.That(a.Equals(b, MarkupFormat.Html, reg)).IsFalse();
	}

	[Test]
	public async Task Render_IntoBufferWriter_WritesSameAsString()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var t = MarkupText.Wrap(new Tag("b"), "x");
		var writer = new ArrayBufferWriter<char>(16);
		t.RenderTo(MarkupFormat.Html, writer, reg);
		await Assert.That(new string(writer.WrittenSpan)).IsEqualTo("<b>x</b>");
	}

	[Test]
	public async Task Render_EmitContext_SeesNeighboursAndPosition()
	{
		var seen = new List<string>();
		var reg = MarkupRegistry.Empty.With(new ContextProbe(MarkupFormat.Html, seen));
		var t = MarkupText.Concat(
		[
			MarkupText.Wrap(new Tag("a"), "1"),
			MarkupText.Wrap(new Tag("b"), "2"),
			MarkupText.Plain("-"),
			MarkupText.Wrap(new Tag("c"), "3"),
		]);
		t.Render(MarkupFormat.Html, reg);
		await Assert.That(string.Join("|", seen)).IsEqualTo(
			"prev=- next=b first=True last=False"
			+ "|prev=a next=- first=False last=False"
			+ "|prev=- next=- first=False last=True");
	}

	private sealed class ContextProbe(MarkupFormat format, List<string> seen) : IMarkupEmitter
	{
		public Type MarkupType => typeof(Tag);
		public MarkupFormat Format => format;

		public void Emit(IMarkup markup, ReadOnlySpan<char> body, in EmitContext c, IBufferWriter<char> o)
		{
			static string Name(MarkupSet? set) => set is null ? "-" : ((Tag)set.Innermost).Name;
			seen.Add($"prev={Name(c.Previous)} next={Name(c.Next)} first={c.IsFirstRun} last={c.IsLastRun}");
			o.Write(body);
		}
	}

	[Test]
	public async Task Render_SetEmitter_TakesPrecedenceAndDelegatesViaRegistry()
	{
		var reg = MarkupRegistry.Empty
			.With(new TagEmitter(MarkupFormat.Html))
			.With(new OuterSetEmitter(MarkupFormat.Html));
		var t = MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new Tag("i"), "x"));
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("[o]<i>x</i>[/o]");
	}

	[Test]
	public async Task Render_SetEmitter_DecliningRun_FallsBackToPerMarkupEmitters()
	{
		var reg = MarkupRegistry.Empty
			.With(new TagEmitter(MarkupFormat.Html))
			.With(new OuterSetEmitter(MarkupFormat.Html));
		await Assert.That(MarkupText.Wrap(new Tag("i"), "x").Render(MarkupFormat.Html, reg)).IsEqualTo("<i>x</i>");
	}

	[Test]
	public async Task Render_SetEmitter_ForAnotherFormat_IsNotConsulted()
	{
		var reg = MarkupRegistry.Empty
			.With(new TagEmitter(MarkupFormat.Html))
			.With(new OuterSetEmitter(MarkupFormat.BBCode));
		var t = MarkupText.Wrap(new Tag("o"), "x");
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("<o>x</o>");
	}

	[Test]
	public async Task Render_Framer_WrapsOutputAndReportsWhetherRunsWereEmitted()
	{
		var reg = MarkupRegistry.Empty
			.With(new TagEmitter(MarkupFormat.Html))
			.With(new BracketFramer(MarkupFormat.Html));
		await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Html, reg)).IsEqualTo("{<b>x</b>}!");
		await Assert.That(MarkupText.Plain("x").Render(MarkupFormat.Html, reg)).IsEqualTo("{x}");
	}

	/// <summary>
	/// A run exists but carries no markup with a registered emitter — the body passes through
	/// unchanged, and <c>anyRunEmitted</c> must report that no emitter actually wrote, not merely
	/// that a run was present.
	/// </summary>
	[Test]
	public async Task Render_Framer_ReportsFalseWhenARunsLayersHaveNoRegisteredEmitter()
	{
		var reg = MarkupRegistry.Empty.With(new BracketFramer(MarkupFormat.Html));
		await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Html, reg)).IsEqualTo("{x}");
	}

	[Test]
	public async Task Render_Framer_ReportsTrueWhenAPerMarkupEmitterRan()
	{
		var reg = MarkupRegistry.Empty
			.With(new TagEmitter(MarkupFormat.Html))
			.With(new BracketFramer(MarkupFormat.Html));
		await Assert.That(MarkupText.Wrap(new Tag("b"), "x").Render(MarkupFormat.Html, reg)).IsEqualTo("{<b>x</b>}!");
	}

	[Test]
	public async Task Render_Framer_ReportsTrueWhenASetEmitterClaimsTheRun()
	{
		var reg = MarkupRegistry.Empty
			.With(new OuterSetEmitter(MarkupFormat.Html))
			.With(new BracketFramer(MarkupFormat.Html));
		await Assert.That(MarkupText.Wrap(new Tag("o"), "x").Render(MarkupFormat.Html, reg)).IsEqualTo("{[o]x[/o]}!");
	}

	[Test]
	public async Task Render_UnknownMarkupLayer_PassesBodyThroughAndKeepsOtherLayers()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var t = MarkupText.Wrap(new Tag("o"), MarkupText.Wrap(new UnknownMarkup("weird", "{}"), "x"));
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("<o>x</o>");
	}

	[Test]
	public async Task Render_NeutralMarkup_EmitsNothingOfItsOwn()
	{
		var reg = MarkupRegistry.Empty.With(new TagEmitter(MarkupFormat.Html));
		var t = MarkupText.Wrap(new Tag("b"), MarkupText.Wrap(NeutralMarkup.Instance, "x"));
		await Assert.That(t.Render(MarkupFormat.Html, reg)).IsEqualTo("<b>x</b>");
	}

	[Test]
	public async Task Render_Empty_IsEmptyString()
		=> await Assert.That(MarkupText.Empty.Render(MarkupFormat.Html, MarkupRegistry.Empty)).IsEqualTo(string.Empty);

	[Test]
	[Arguments(TextEncoding.None, "a\u001bb\u007fc\td", "a\u001bb\u007fc\td")]
	[Arguments(TextEncoding.StripControls, "a\u001bb\u007fc\td", "abc\td")]
	[Arguments(TextEncoding.StripControls, "a\tb\nc\rd", "a\tb\nc\rd")]
	[Arguments(TextEncoding.StripControls, "\u0008\u000b\u000c\u000e\u001f", "")]
	[Arguments(TextEncoding.Html, "<a href=\"x\">it's</a>", "&lt;a href=&quot;x&quot;&gt;it&#39;s&lt;/a&gt;")]
	[Arguments(TextEncoding.Html, "a\u001b&\u007fb", "a&amp;b")]
	[Arguments(TextEncoding.Html, "a\tb\nc", "a\tb\nc")]
	public async Task EncodeText_AppliesTheEncoding(TextEncoding encoding, string input, string expected)
	{
		var writer = new ArrayBufferWriter<char>(input.Length + 16);
		MarkupTextRenderer.EncodeText(input, encoding, writer);
		await Assert.That(new string(writer.WrittenSpan)).IsEqualTo(expected);
	}

	[Test]
	public async Task Format_EqualityIsByNameIgnoringCase()
	{
		await Assert.That(MarkupFormat.TryParse("HTML")).IsEqualTo(MarkupFormat.Html);
		await Assert.That(MarkupFormat.Custom("bogus", TextEncoding.None)).IsNotEqualTo(MarkupFormat.Html);
		await Assert.That(MarkupFormat.Html == MarkupFormat.TryParse("html")).IsTrue();
		await Assert.That(MarkupFormat.Html.GetHashCode()).IsEqualTo(MarkupFormat.TryParse("Html")!.GetHashCode());
	}

	[Test]
	[Arguments("HTML")]
	[Arguments("html")]
	[Arguments("Plain")]
	[Arguments("ANSI")]
	[Arguments("Pueblo")]
	[Arguments("mxp")]
	[Arguments("BBCode")]
	public async Task Format_Custom_RejectsANameThatMatchesABuiltIn(string name)
		=> await Assert.That(() => MarkupFormat.Custom(name, TextEncoding.None)).Throws<ArgumentException>();

	[Test]
	public async Task Format_Custom_AllowsANameThatIsNotABuiltIn()
	{
		var format = MarkupFormat.Custom("bogus", TextEncoding.None);
		await Assert.That(format.Name).IsEqualTo("bogus");
		await Assert.That(format.Encoding).IsEqualTo(TextEncoding.None);
	}

	[Test]
	[Arguments("plain", TextEncoding.StripControls)]
	[Arguments("ANSI", TextEncoding.None)]
	[Arguments("html", TextEncoding.Html)]
	[Arguments("Pueblo", TextEncoding.Html)]
	[Arguments("mxp", TextEncoding.Html)]
	[Arguments("bbcode", TextEncoding.StripControls)]
	public async Task Format_TryParse_ResolvesBuiltInsCaseInsensitively(string name, TextEncoding encoding)
	{
		var format = MarkupFormat.TryParse(name);
		await Assert.That(format).IsNotNull();
		await Assert.That(format!.Encoding).IsEqualTo(encoding);
	}

	[Test]
	public async Task Format_TryParse_UnknownNameIsNull()
		=> await Assert.That(MarkupFormat.TryParse("bogus")).IsNull();

	[Test]
	public async Task Registry_With_ReturnsANewInstanceAndLeavesTheOriginalAlone()
	{
		var emitter = new TagEmitter(MarkupFormat.Html);
		var reg = MarkupRegistry.Empty.With(emitter);
		await Assert.That(reg).IsNotSameReferenceAs(MarkupRegistry.Empty);
		await Assert.That(MarkupRegistry.Empty.FindEmitter(typeof(Tag), MarkupFormat.Html)).IsNull();
		await Assert.That(reg.FindEmitter(typeof(Tag), MarkupFormat.Html)).IsSameReferenceAs(emitter);
		await Assert.That(reg.FindEmitter(typeof(Tag), MarkupFormat.Ansi)).IsNull();
	}

	[Test]
	public async Task Registry_LastRegistrationWinsForTheSameKey()
	{
		var first = new TagEmitter(MarkupFormat.Html);
		var second = new TagEmitter(MarkupFormat.Html);
		var reg = MarkupRegistry.Empty.With(first).With(second);
		await Assert.That(reg.FindEmitter(typeof(Tag), MarkupFormat.Html)).IsSameReferenceAs(second);
	}

	[Test]
	public async Task Registry_FindsSetEmittersFramersAndCodecs()
	{
		var setEmitter = new OuterSetEmitter(MarkupFormat.Html);
		var framer = new BracketFramer(MarkupFormat.Html);
		var codec = new TagCodec();
		var reg = MarkupRegistry.Empty.With(setEmitter).With(framer).With(codec);
		await Assert.That(reg.FindSetEmitter(MarkupFormat.Html)).IsSameReferenceAs(setEmitter);
		await Assert.That(reg.FindSetEmitter(MarkupFormat.Ansi)).IsNull();
		await Assert.That(reg.FindFramer(MarkupFormat.Html)).IsSameReferenceAs(framer);
		await Assert.That(reg.FindFramer(MarkupFormat.Ansi)).IsNull();
		await Assert.That(reg.FindCodec("tag")).IsSameReferenceAs(codec);
		await Assert.That(reg.FindCodec(typeof(Tag))).IsSameReferenceAs(codec);
		await Assert.That(reg.FindCodec("nope")).IsNull();
		await Assert.That(reg.FindCodec(typeof(string))).IsNull();
	}

	private sealed class TagCodec : IMarkupCodec
	{
		public string Kind => "tag";
		public Type MarkupType => typeof(Tag);
		public void Write(System.Text.Json.Utf8JsonWriter writer, IMarkup markup) => writer.WriteString("n", ((Tag)markup).Name);
		public IMarkup Read(System.Text.Json.JsonElement element) => new Tag(element.GetProperty("n").GetString()!);
	}
}
