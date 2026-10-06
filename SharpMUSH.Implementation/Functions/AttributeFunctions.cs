using SharpMUSH.Library.Markup;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "aposs", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> AbsolutePossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService, Mediator, parser, onObject,
				Configuration.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.AbsolutePossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "hers",
					_ => "theirs"
				}));
	}

	/// <summary>
	/// The <c>"&lt;object&gt;/&lt;attr&gt; - Set."</c> confirmation <c>attrib_set()</c> prints:
	/// <c>fun_attrib_set</c> passes <c>0x01</c> to <c>do_set_atr</c> (<c>src/fundb.c:2294-2300</c>),
	/// the flag that asks for this line (<c>src/attrib.c:2446-2452</c>). It goes to the EXECUTOR,
	/// and is suppressed by a QUIET player, a QUIET object they own, or a <c>quiet</c> attribute.
	/// Failures come back as the return value instead.
	/// </summary>
	private async ValueTask NotifyOfSet(AnySharpObject executor, AnySharpObject thing, string attribute,
		bool succeeded, bool wasSet)
	{
		// A player's alias list was reported by the write itself (PlayerAliases).
		// Read back the attribute that was just written, as Penn does, so its own quiet flag counts.
		if (!succeeded || PlayerAliases.Applies(thing, attribute)
				|| await AttributeWriteReport.IsSuppressedAsync(AttributeService, executor, thing, attribute))
		{
			return;
		}

		await NotifyService.Notify(executor,
			$"{thing.Object().Name}/{attribute.ToUpperInvariant()} - {(wasSet ? "Set" : "Cleared")}.");
	}

	[SharpFunction(Name = "attrib_set", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object/attribute"])]
	public ValueTask<CallState> AttributeSet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AttributeSetAsync(parser, _ => string.Empty);

	[SharpFunction(Name = "attrib_set#", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object/attribute"])]
	public ValueTask<CallState> AttributeSetSharp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> AttributeSetAsync(parser, target => $"{target.Object().Name}/{parser.CurrentState.Arguments["0"].Message}");

	/// <summary>
	/// The one body of <c>attrib_set()</c> (PennMUSH's <c>fun_attrib_set</c>, <c>src/fundb.c:2270</c>)
	/// and SharpMUSH's <c>attrib_set#()</c>, which differ only in what a successful set returns.
	/// </summary>
	private async ValueTask<CallState> AttributeSetAsync(IMUSHCodeParser parser, Func<AnySharpObject, string> successResult)
	{
		var args = parser.CurrentState.Arguments;
		var split = HelperFunctions.SplitObjectAndAttr(args["0"].Message!.ToPlainText());
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (split is not { Object: var dbref, Attribute: var attribute })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "ATTRIB_SET"));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, dbref, LocateFlags.All, async realLocated =>
			{
				// Without a value it clears, as do_set_atr(thing, s, NULL, ...) does
				// (src/fundb.c:2295-2296); an empty value still creates the attribute. Clearing one
				// that is not there is AE_NOTFOUND, which PennMUSH notifies as "No such attribute to
				// reset." (src/attrib.c:2411-2412) and SharpMUSH returns. The control check comes first
				// (src/attrib.c:2265), so a caller who may not clear cannot probe what exists, and the
				// lookup is by exact name, as find_atr_in_list's is: an alias such as DESC is not found.
				var hasValue = args.TryGetValue("1", out var contents);
				if (!hasValue)
				{
					if (!await PermissionService.Controls(executor, realLocated))
					{
						return new CallState(ErrorMessages.Returns.AttrSetPermissions);
					}

					if (!await Mediator.CreateStream(new GetAttributesQuery(realLocated.Object().DBRef, attribute, false,
							IAttributeService.AttributePatternMode.Exact)).AnyAsync())
					{
						return new CallState(ErrorMessages.Returns.NoSuchAttribute);
					}
				}

				var setResult = hasValue
					? await AttributeService.SetAttributeAsync(executor, realLocated, attribute, contents!.Message!)
					: await AttributeService.ClearAttributeAsync(executor, realLocated, attribute,
						IAttributeService.AttributePatternMode.Exact);

				await NotifyOfSet(executor, realLocated, attribute, setResult is Success, args.ContainsKey("1"));

				return new CallState(setResult switch
				{
					Success => successResult(realLocated),
					Error<string> failure => failure.Value
				});
			});
	}

	[SharpFunction(Name = "default", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default"])]
	public async ValueTask<CallState> Default(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var hadErrors = false;
		CallState Preserve(CallState result) => result with { HadErrors = hadErrors || result.HadErrors };
		var defaultArg = parser.CurrentState.ArgumentsOrdered.Last().Value;
		var objAndAttrsToCheck = parser.CurrentState.ArgumentsOrdered.SkipLast(1).Select(x => x.Value);

		foreach (var objAndAttr in objAndAttrsToCheck)
		{
			var parsedResult = await objAndAttr.GetParsedResultAsync();
			hadErrors |= parsedResult.HadErrors;
			var parsedMessage = parsedResult.Message ?? MarkupText.Empty;
			if (HelperFunctions.SplitObjectAndAttr(parsedMessage.ToPlainText()) is not { Object: var dbref, Attribute: var attribute })
			{
				return Preserve(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpper())));
			}

			var maybeFound = await LocateService.LocateAndNotifyIfInvalidWithCallState(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			switch (maybeFound)
			{
				case Error<CallState> error:
					return Preserve(error.Value);
				case AnySharpObject found:
					var maybeAttr = await AttributeService.GetAttributeAsync(
						executor,
						found,
						attribute,
						mode: IAttributeService.AttributeMode.Execute,
						parent: true);

					if (maybeAttr is SharpAttribute[])
					{
						return Preserve(maybeAttr.AsCallState);
					}

					break;
			}
		}

		return Preserve(await defaultArg.GetParsedResultAsync());
	}

	/// <summary>
	/// Returns the first non-empty evaluated attribute value, or evaluates and returns the default value.
	/// Similar to default() but evaluates all arguments. Checks attributes in order until one has content.
	/// </summary>
	[SharpFunction(Name = "edefault", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default"])]
	public async ValueTask<CallState> EvaluateDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var hadErrors = false;
		CallState Preserve(CallState result) => result with { HadErrors = hadErrors || result.HadErrors };
		var defaultArg = parser.CurrentState.ArgumentsOrdered.Last().Value;
		var objAndAttrsToCheck = parser.CurrentState.ArgumentsOrdered.SkipLast(1).Select(x => x.Value);

		foreach (var objAndAttr in objAndAttrsToCheck)
		{
			var parsedResult = await objAndAttr.GetParsedResultAsync();
			hadErrors |= parsedResult.HadErrors;
			var parsedMessage = parsedResult.Message ?? MarkupText.Empty;
			if (HelperFunctions.SplitObjectAndAttr(parsedMessage.ToPlainText()) is not { Object: var dbref, Attribute: var attribute })
			{
				return Preserve(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpper())));
			}

			var maybeFound = await LocateService.LocateAndNotifyIfInvalidWithCallState(
				parser,
				executor,
				executor,
				dbref,
				LocateFlags.All);

			switch (maybeFound)
			{
				case Error<CallState> error:
					return Preserve(error.Value);
				case AnySharpObject found:
					var maybeAttr = await AttributeService.GetAttributeAsync(
						executor,
						found,
						attribute,
						mode: IAttributeService.AttributeMode.Execute,
						parent: true);

					if (maybeAttr is SharpAttribute[])
					{
						return Preserve(await AttributeService.EvaluateAttributeFunctionResultAsync(
							parser,
							executor,
							found,
							attribute,
							parser.CurrentState.EnvironmentRegisters));
					}

					break;
			}
		}

		return Preserve(await defaultArg.GetParsedResultAsync());
	}

	[SharpFunction(Name = "eval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["object", "attribute"])]
	public async ValueTask<CallState> Eval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbref = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var attribute = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, dbref,
			LocateFlags.All,
			async actualObject => await AttributeService.EvaluateAttributeFunctionResultAsync(
					parser,
					executor,
					actualObject,
					attribute,
					parser.CurrentState.EnvironmentRegisters));
	}

	[SharpFunction(Name = "flags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Flags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// The parser hands a no-argument call an empty Arguments["0"], so "no argument" is an empty one.
		var arg0 = (parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message ?? MarkupText.Empty).ToPlainText();
		if (arg0.Length == 0)
		{
			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			return string.Concat(await flags.Select(x => x.Symbol).ToArrayAsync());
		}

		var dbrefAndAttr = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (dbrefAndAttr is not { Object: var obj, Attribute: var attributePattern })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpper()));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				if (attributePattern is null)
				{
					// fun_flags is unparse_flags (src/flags.c:1638): the type letter, then the flags in bit order.
					return await MessageFormatting.FlagSymbolsAsync(found.Object(),
						await FlagView.ForAsync(executor, ConnectionService));
				}

				var attr = await AttributeService.LazilyGetAttributeAsync(
					executor, found, attributePattern, IAttributeService.AttributeMode.Read, false);

				return attr switch
				{
					LazySharpAttribute[] attribute => string.Join("", attribute.Last().Flags.Select(x => x.Symbol)),
					None => ErrorMessages.Returns.NoSuchAttribute,
					Error<string> error => error.Value
				};
			});
	}

	[SharpFunction(Name = "get", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object/attribute"])]
	public async ValueTask<CallState> Get(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (HelperFunctions.SplitObjectAndAttr((parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText()) is not { Object: var dbref, Attribute: var attribute })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpper()));
		}

		return await GetAttributeValueAsync(parser, dbref, attribute);
	}

	/// <summary>
	/// The one body of <c>get()</c> and <c>xget()</c>, which differ only in how they take their
	/// arguments: PennMUSH's <c>fun_get</c> and <c>fun_xget</c> both end in <c>do_get_attrib</c>
	/// (<c>src/fundb.c:46-71</c>), whose <c>atr_get</c> walks the <c>@parent</c> chain.
	/// </summary>
	private async ValueTask<CallState> GetAttributeValueAsync(IMUSHCodeParser parser, string dbref, string attribute)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			dbref,
			LocateFlags.All,
			async x =>
			{
				var maybeAttr = await AttributeService.GetAttributeAsync(
					executor,
					x,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: true);

				return maybeAttr switch
				{
					SharpAttribute[] chain => new CallState(chain.Last().Value),
					None => await MissingAttributeGetResultAsync(executor, x, attribute),
					Error<string> error => new CallState(error.Value)
				};
			});
	}

	/// <summary>
	/// What <c>get()</c>/<c>xget()</c> return when the attribute is not set: PennMUSH's
	/// <c>do_get_attrib</c> (<c>src/fundb.c:61-70</c>). A standard attribute name answers empty only
	/// if its table entry would be readable (<c>Can_Read_Attr</c> on the entry's default flags); any
	/// other name answers empty only to someone who can examine the object. Otherwise
	/// <c>#-1 NO PERMISSION TO GET ATTRIBUTE</c>, so a missing attribute reveals no more than a set one.
	/// </summary>
	private async ValueTask<CallState> MissingAttributeGetResultAsync(AnySharpObject executor, AnySharpObject obj,
		string attribute)
	{
		// atr_match finds a standard attribute by its alias too (DESC is DESCRIBE's table entry).
		var name = Library.Services.AttributeService.StandardAttributeAliases.TryGetValue(attribute, out var realName)
			? realName
			: attribute.ToUpperInvariant();
		var readable = await Mediator.Send(new GetAttributeEntryQuery(name)) is { } entry
			? await PermissionService.CanViewAttribute(executor, obj, UnsetAttributeFor(entry, name))
			: await PermissionService.CanExamine(executor, obj);

		return readable ? CallState.Empty : new CallState(ErrorMessages.Returns.AttrPermissions);
	}

	/// <summary>A standard attribute as it would be if set: the table entry's default flags, no value.</summary>
	private static SharpAttribute UnsetAttributeFor(SharpAttributeEntry entry, string name)
		=> new(
			Id: string.Empty,
			Key: string.Empty,
			Name: name,
			Flags: [.. entry.DefaultFlags.Select(flag => new SharpAttributeFlag
			{
				Name = flag,
				Symbol = string.Empty,
				System = true,
				Inheritable = false
			})],
			CommandListIndex: null,
			LongName: name,
			Leaves: new DotNext.Threading.AsyncLazy<IAsyncEnumerable<SharpAttribute>>(
				_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
			Owner: new DotNext.Threading.AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
			SharpAttributeEntry: new DotNext.Threading.AsyncLazy<SharpAttributeEntry?>(
				_ => Task.FromResult<SharpAttributeEntry?>(entry)));

	[SharpFunction(Name = "get_eval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute"])]
	public async ValueTask<CallState> GetEval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (HelperFunctions.SplitObjectAndAttr((parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText()) is not { Object: var dbref, Attribute: var attribute })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(GetEval).ToUpper()));
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, dbref,
			LocateFlags.All,
			async actualObject => await AttributeService.EvaluateAttributeFunctionResultAsync(
					parser,
					executor,
					actualObject,
					attribute,
					parser.CurrentState.EnvironmentRegisters));
	}

	/// <summary>
	/// The one body behind <c>hasattr</c>, <c>hasattrp</c>, <c>hasattrval</c> and
	/// <c>hasattrpval</c>. PennMUSH registers all four on <c>fun_hasattr</c> and reads the pair of
	/// switches out of <c>called_as</c> — <c>strchr(called_as, 'P')</c> for the parent walk and
	/// <c>strstr(called_as, "VAL")</c> for "has a value" rather than "exists"
	/// (<c>src/fundb.c:215-260</c>).
	///
	/// <para>Called with one argument, that argument is the whole
	/// <c>&lt;object&gt;/&lt;attribute&gt;</c> spec (<c>src/fundb.c:222-231</c>); without a slash it
	/// is <c>#-1 BAD ARGUMENT FORMAT TO &lt;called_as&gt;</c>, which is why
	/// <paramref name="calledAs"/> is passed in rather than derived.</para>
	/// </summary>
	private async ValueTask<CallState> HasAttributeAsync(
		IMUSHCodeParser parser, string calledAs, bool checkParents, bool requireValue)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var spec = args["0"].Message!.ToPlainText()!;

		string obj;
		string attribute;

		if (args.Count > 1)
		{
			obj = spec;
			attribute = args["1"].Message!.ToPlainText()!;
		}
		else if (HelperFunctions.SplitDbRefAndOptionalAttr(spec) is { Object: var only, Attribute: { } attr })
		{
			obj = only;
			attribute = attr;
		}
		else
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, calledAs));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async found =>
			{
				// Without the VAL forms only existence and permission matter, so the value is never read.
				if (!requireValue)
				{
					return await AttributeService.LazilyGetAttributeAsync(
							executor,
							found,
							attribute,
							mode: IAttributeService.AttributeMode.Read,
							parent: checkParents) switch
					{
						Error<string> { Value: ErrorMessages.Returns.AttrPermissions } => new CallState(ErrorMessages.Returns.PermissionDenied),
						Error<string> error => new CallState(error.Value),
						LazySharpAttribute[] => new CallState("1"),
						_ => new CallState("0")
					};
				}

				var maybeAttr = await AttributeService.GetAttributeAsync(
					executor,
					found,
					attribute,
					mode: IAttributeService.AttributeMode.Read,
					parent: checkParents);

				// PennMUSH answers 0 only for an attribute that is genuinely absent from an object
				// the caller may examine; an attribute that exists but cannot be read is e_perm,
				// not "no" (src/fundb.c:243-256). Reporting absence for a refusal tells a mortal
				// the attribute is not there, which is a different and wrong answer. The refusal is
				// e_perm, not the NO PERMISSION TO GET ATTRIBUTE that get() gives.
				return maybeAttr switch
				{
					Error<string> { Value: ErrorMessages.Returns.AttrPermissions } => new CallState(ErrorMessages.Returns.PermissionDenied),
					Error<string> error => new CallState(error.Value),
					SharpAttribute[] chain => new CallState(
						!requireValue || HasValue(chain.Last().Value.ToPlainText()) ? "1" : "0"),
					_ => new CallState("0")
				};
			});
	}

	/// <summary>
	/// What the <c>VAL</c> forms count as a value. PennMUSH (<c>src/fundb.c:245-250</c>) treats only
	/// the empty string as empty, plus a value of exactly one space when <c>empty_attrs</c> is off —
	/// the space being what an attribute set to nothing is stored as in that configuration. Anything
	/// else, two spaces included, is a value; a blanket whitespace test answers 0 for it.
	/// </summary>
	private bool HasValue(string value)
		=> value.Length != 0 && !(value == " " && !Configuration.CurrentValue.Attribute.EmptyAttributes);

	[SharpFunction(Name = "hasattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object[/attribute]", "attribute"])]
	public ValueTask<CallState> HasAttribute(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> HasAttributeAsync(parser, "HASATTR", checkParents: false, requireValue: false);

	[SharpFunction(Name = "hasattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object[/attribute]", "attribute"])]
	public ValueTask<CallState> HasAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> HasAttributeAsync(parser, "HASATTRP", checkParents: true, requireValue: false);

	[SharpFunction(Name = "hasattrpval", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object[/attribute]", "attribute"])]
	public ValueTask<CallState> HasAttributeParentValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> HasAttributeAsync(parser, "HASATTRPVAL", checkParents: true, requireValue: true);

	[SharpFunction(Name = "hasattrval", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object[/attribute]", "attribute"])]
	public ValueTask<CallState> HasAttributeValue(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> HasAttributeAsync(parser, "HASATTRVAL", checkParents: false, requireValue: true);

	[SharpFunction(Name = "hasflag", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flag"])]
	public async ValueTask<CallState> HasFlag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objAndAttr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagNameOrSymbol = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAndAttr) is not { Object: var db, Attribute: var attr })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(HasFlag)));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, db, LocateFlags.All,
			async realLocated => attr is null
				? await HasObjectFlag(realLocated)
				: await HasAttributeFlag(realLocated, attr));

		async ValueTask<CallState> HasObjectFlag(AnySharpObject realLocated)
		{
			// CONNECTED is a runtime pseudo-flag (a live play session), not a stored flag; portal-only
			// background connections don't count. See IConnectionService.IsOnline.
			if (string.Equals(flagNameOrSymbol, "CONNECTED", StringComparison.OrdinalIgnoreCase))
				return await ConnectionService.IsOnline(realLocated);

			// Name, alias or letter — the three things flag_hash_lookup resolves (src/flags.c). See #834.
			return await realLocated.Object().HasFlagOrLetter(flagNameOrSymbol);
		}

		async ValueTask<CallState> HasAttributeFlag(AnySharpObject realLocated, string attribute)
		{
			var maybeAttr = await AttributeService.GetAttributeAsync(
				executor,
				realLocated,
				attribute,
				IAttributeService.AttributeMode.Read,
				true);

			// fun_hasflag (fundb.c:1031-1033): parse_attrib reads through atr_get, and a miss is #-1.
			if (maybeAttr is not SharpAttribute[] chain) return "#-1";

			return chain.Last().Flags.Any(f =>
				string.Equals(f.Name, flagNameOrSymbol, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(f.Symbol.ToString(), flagNameOrSymbol, StringComparison.OrdinalIgnoreCase));
		}
	}

	/// <summary>
	/// The one body behind <c>lattr</c>, <c>xattr</c> and their <c>reg</c>- and <c>-p</c> variants.
	/// PennMUSH registers all eight of those names on <c>fun_lattr</c> and all four <c>nattr</c>
	/// names on <c>fun_nattr</c> (<c>src/function.c:529,619,703-707,830</c>), reading the variant
	/// straight out of <c>called_as</c> (<c>src/fundb.c:157-186</c>). Written out twelve times here
	/// they had already drifted: five of the six regular-expression variants reported <c>GET</c> in
	/// the error PennMUSH reports as <c>called_as</c> (<c>src/fundb.c:225</c>), and <c>lattr</c>
	/// ignored the output delimiter it declares a second argument for.
	/// <para>
	/// Every variant needs names or a count, never a value, so the read is the lazy one: the same
	/// permission walk over metadata, with no <c>attr.val</c> row read for a match or for any branch
	/// node the walk checks. <paramref name="project"/> receives the permitted long names in the
	/// order the eager read sorted them. A regular expression that does not compile, or a match that
	/// runs out its time, answers <c>#-1 REGEXP ERROR: INVALID REGULAR EXPRESSION</c> or
	/// <c>#-1 REGEXP TIMEOUT</c>, as <c>regrep</c> does.
	/// </para>
	/// </summary>
	private ValueTask<CallState> AttributePatternAsync(
		IMUSHCodeParser parser,
		string calledAs,
		bool checkParents,
		IAttributeService.AttributePatternMode mode,
		Func<string[], CallState> project)
		=> AttributePatternAsync(parser, calledAs, checkParents, mode,
			async names => project(await names.ToArrayAsync(ExecutionBudget.CurrentToken)));

	/// <summary>
	/// As above, with the names handed over as the stream they are read from, for a caller that needs
	/// only a window of them or their count. The stream is enumerated inside the regexp error handling.
	/// </summary>
	private async ValueTask<CallState> AttributePatternAsync(
		IMUSHCodeParser parser,
		string calledAs,
		bool checkParents,
		IAttributeService.AttributePatternMode mode,
		Func<IAsyncEnumerable<string>, ValueTask<CallState>> project)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(
			(parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText());

		if (split is not { Object: var obj, Attribute: var attributePattern })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, calledAs.ToUpperInvariant()));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, obj, LocateFlags.All,
			async found =>
			{
				var attributes = await AttributeService.LazilyGetAttributePatternAsync(executor, found,
					attributePattern ?? (mode is IAttributeService.AttributePatternMode.Regex ? ".*" : "*"),
					checkParents, mode);

				return attributes switch
				{
					Error<string> error => error,
					IAsyncEnumerable<LazySharpAttribute> matched => await ProjectNamesAsync(matched, project)
				};
			});
	}

	private static async ValueTask<CallState> ProjectNamesAsync(IAsyncEnumerable<LazySharpAttribute> matched,
		Func<IAsyncEnumerable<string>, ValueTask<CallState>> project)
	{
		try
		{
			return await project(matched.Select(x => x.LongName));
		}
		catch (System.Text.RegularExpressions.RegexParseException)
		{
			return new CallState(ErrorMessages.Returns.RegexpInvalid);
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			return new CallState(ErrorMessages.Returns.RegexpTimeout);
		}
	}

	/// <summary>
	/// The <c>x</c> forms' extra prologue. PennMUSH validates the start and the count before it
	/// splits the object from the pattern (<c>src/fundb.c:157-171</c>).
	/// </summary>
	private ValueTask<CallState> AttributeRangeAsync(
		IMUSHCodeParser parser, string calledAs, bool checkParents, IAttributeService.AttributePatternMode mode)
	{
		var args = parser.CurrentState.Arguments;
		if (!ArgHelpers.TryStrictInteger(args["1"].Message!.ToPlainText(), out int start) ||
			!ArgHelpers.TryStrictInteger(args["2"].Message!.ToPlainText(), out int count))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integer);
		}

		if (start < 1 || count < 1)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.ArgRange);
		}

		// The window is taken from the stream: names past it are never read.
		return AttributePatternAsync(parser, calledAs, checkParents, mode,
			async names => new CallState(string.Join(AttributeListSeparator(parser, 3),
				await names.Skip(start - 1).Take(count).ToArrayAsync(ExecutionBudget.CurrentToken))));
	}

	/// <summary>
	/// The optional output delimiter, a space when absent — PennMUSH's <c>delim_check</c> default
	/// (<c>src/fundb.c:172,177</c>).
	/// </summary>
	private static string AttributeListSeparator(IMUSHCodeParser parser, int argument)
		=> ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, argument, " ").ToPlainText();

	[SharpFunction(Name = "lattr", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "delimiter"])]
	public ValueTask<CallState> ListAttributes(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Wildcard,
			matched => string.Join(AttributeListSeparator(parser, 1), matched));

	[SharpFunction(Name = "lattrp", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "delimiter"])]
	public ValueTask<CallState> ListAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Wildcard,
			matched => string.Join(AttributeListSeparator(parser, 1), matched));

	[SharpFunction(Name = "lflags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// The parser hands a no-argument call an empty Arguments["0"], so "no argument" is an empty one.
		var arg0 = (parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message ?? MarkupText.Empty).ToPlainText();
		if (arg0.Length == 0)
		{
			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			return string.Join(" ", await flags.Select(x => x.Name).ToArrayAsync());
		}

		var dbrefAndAttr = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (dbrefAndAttr is not { Object: var obj, Attribute: var attributePattern })
		{
			return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpper()));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, obj, LocateFlags.All,
			async found =>
			{
				if (attributePattern is null)
				{
					// fun_lflags is bits_to_string (src/flags.c:1432): names in bit order, no type.
					return string.Join(" ", (await MessageFormatting.VisibleFlagsAsync(found.Object(),
						await FlagView.ForAsync(executor, ConnectionService))).Select(x => x.Name));
				}

				var attr = await AttributeService.LazilyGetAttributeAsync(
					executor, found, attributePattern, IAttributeService.AttributeMode.Read, false);

				return attr switch
				{
					LazySharpAttribute[] attribute => string.Join(" ", attribute.Last().Flags.Select(x => x.Name)),
					None => ErrorMessages.Returns.NoSuchAttribute,
					Error<string> error => error.Value
				};
			});
	}

	[SharpFunction(Name = "nattr", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> NumberAttributes(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Wildcard,
			async names => new CallState(await names.CountAsync(ExecutionBudget.CurrentToken)));

	[SharpFunction(Name = "nattrp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> NumberAttributesParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributePatternAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Wildcard,
			async names => new CallState(await names.CountAsync(ExecutionBudget.CurrentToken)));

	[SharpFunction(Name = "obj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object/attribute"])]
	public async ValueTask<CallState> ObjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService, Mediator, parser, onObject,
				Configuration.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.ObjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "him",
					"F" or "Female" => "her",
					_ => "them"
				}));
	}

	[SharpFunction(Name = "objeval", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "expression"])]
	public async ValueTask<CallState> ObjectEvaluation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var sideFxEnabled = Configuration.CurrentValue.Function.FunctionSideEffects;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		// NoParse holds BOTH arguments back, but only the expression is meant to wait for its new
		// executor: PennMUSH's fun_objeval (src/funufun.c) runs process_expression over args[0] as
		// the caller before it locates anything, so objeval(%0,...) and objeval([num(here)],...)
		// name an object. Reading the raw text here looked "%0" up as a name and answered
		// #-1 NO MATCH for every dynamic object.
		var objectArg = await parser.CurrentState.Arguments["0"].GetParsedResultAsync();
		var expression = parser.CurrentState.Arguments["1"];

		// fun_objeval is not an error path: when match_thing finds nothing (it notifies "I can't see
		// that here.") or the executor may not evaluate as what it found, the expression is evaluated
		// as the executor itself. Control is required when function side effects are on, control or
		// See_All when they are off.
		var located = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor,
			objectArg.Message!.ToPlainText(), LocateFlags.All);
		var evaluator = located is AnySharpObject found && await MayEvaluateAs(found) ? found : executor;

		var result = await parser.With(state => state with { Executor = evaluator.Object().DBRef },
			async newParser => await newParser.FunctionParse(expression.Message!)) ?? CallState.Empty;
		return result with { HadErrors = objectArg.HadErrors || result.HadErrors };

		async ValueTask<bool> MayEvaluateAs(AnySharpObject target) =>
			await PermissionService.Controls(executor, target)
			|| (!sideFxEnabled && await executor.IsSee_All());
	}

	/// <summary>
	/// The object id: the dbref, a colon, and the creation time in <em>milliseconds</em>.
	/// </summary>
	/// <remarks>
	/// PennMUSH documents objid as <c>[num(&lt;object&gt;)]:[csecs(&lt;object&gt;)]</c> because it stores
	/// creation times in seconds. SharpMUSH stores milliseconds, and objid carries them, so that
	/// identity does NOT hold here: <c>[num(%0)]:[csecs(%0)]</c> is a thousand-fold-truncated stamp
	/// and will not resolve. Use <c>objid()</c>, or <c>csecs(&lt;object&gt;,ms)</c> for the field alone.
	/// See docs/superpowers/specs/2026-09-08-millisecond-precision-design.md.
	/// </remarks>
	[SharpFunction(Name = "objid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ObjectId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			found => ValueTask.FromResult<CallState>(found.Object().DBRef));
	}

	[SharpFunction(Name = "objmem", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> ObjectMemory(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("0");

	[SharpFunction(Name = "owner", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Owner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var dbrefAndMaybeArg =
			HelperFunctions.SplitDbRefAndOptionalAttr((parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText());
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (dbrefAndMaybeArg is not { Object: var obj, Attribute: var attribute })
		{
			return new CallState(ErrorMessages.Returns.CantSeeThat);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			obj,
			LocateFlags.All,
			async actualObject =>
			{
				if (attribute is null)
				{
					var objOwner = await actualObject.Object().Owner.WithCancellation(CancellationToken.None);
					return new CallState($"#{objOwner.Object.DBRef.Number}");
				}

				var attributeObject = await AttributeService.GetAttributeAsync(executor, actualObject, attribute,
					IAttributeService.AttributeMode.Read, true);

				// fun_owner (fundb.c:1717-1724): the attribute comes through atr_get, and a missing or
				// unreadable one is a bare #-1.
				return attributeObject is SharpAttribute[] attr
					? new CallState($"#{(await attr.Last().Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number}")
					: new CallState("#-1");
			}
		);
	}

	[SharpFunction(Name = "poss", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> PossessivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, arg0, LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService, Mediator, parser, onObject,
				Configuration.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.PossessivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "his",
					"F" or "Female" => "her",
					_ => "their"
				}));
	}

	[SharpFunction(Name = "regedit", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["string", "pattern", "replacement"])]
	public async ValueTask<CallState> RegularExpressionEdit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, false, false);
	}

	[SharpFunction(Name = "regeditall", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["string", "pattern", "replacement"])]
	public async ValueTask<CallState> RegularExpressionEditAll(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, false, true);
	}

	[SharpFunction(Name = "regeditalli", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "pattern", "string", "replacement"])]
	public async ValueTask<CallState> RegularExpressionAllCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, true, true);
	}

	[SharpFunction(Name = "regediti", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse, ParameterNames = ["object", "pattern", "string", "replacement"])]
	public async ValueTask<CallState> RegularExpressionEditCaseInsensitive(IMUSHCodeParser parser,
		SharpFunctionAttribute _2)
	{
		return await RegEditInternal(parser, true, false);
	}

	/// <summary>
	/// Internal helper for regedit, regediti, regeditall, regeditalli: PennMUSH's <c>fun_regreplace</c>
	/// (<c>src/funlist.c</c>).
	/// </summary>
	/// <remarks>
	/// Each replacement is evaluated inside a regexp capture context holding its match, which
	/// <c>$&lt;digit&gt;</c> and <c>$&lt;name&gt;</c> read. The captures are never pasted into the
	/// replacement: they are the subject's text, and evaluating them would run it as softcode.
	/// </remarks>
	private async ValueTask<CallState> RegEditInternal(IMUSHCodeParser parser, bool caseInsensitive, bool all)
	{
		var stringArg = await parser.CurrentState.Arguments["0"].GetParsedResultAsync();
		var hadErrors = stringArg.HadErrors;
		var mstr = stringArg.Message ?? MarkupText.Empty;
		var str = mstr.ToPlainText();

		var args = parser.CurrentState.ArgumentsOrdered.Skip(1).ToList();

		var options = RegexOptions.None;
		if (caseInsensitive)
		{
			options |= RegexOptions.IgnoreCase;
		}

		var captures = new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		parser.CurrentState.RegexRegisters.Push(captures);

		async ValueTask<MString> Replacement(CallState template, Regex regex, Match match)
		{
			captures.Fill(regex, match, mstr);
			var replacement = await template.GetParsedResultAsync();
			hadErrors |= replacement.HadErrors;
			return replacement.Message ?? MarkupText.Empty;
		}

		try
		{
			for (int i = 0; i < args.Count - 1; i += 2)
			{
				var pattern = await args[i].Value.GetParsedResultAsync();
				hadErrors |= pattern.HadErrors;
				var patternStr = pattern.Message?.ToPlainText() ?? "";
				var template = args[i + 1].Value;

				var regex = SoftcodeRegex.Create(patternStr, options);

				if (all)
				{
					// Every match is replaced against the text the pattern ran on, in one splice, so the
					// indexes never shift under the edits.
					var edits = new List<MarkupString.Edit>();
					foreach (Match match in regex.Matches(str))
					{
						edits.Add(new MarkupString.Edit(match.Index, match.Length, await Replacement(template, regex, match)));
					}

					if (edits.Count > 0)
					{
						mstr = mstr.Splice(CollectionsMarshal.AsSpan(edits));
						str = mstr.ToPlainText();
					}
				}
				else
				{
					var match = regex.Match(str);
					if (match.Success)
					{
						mstr = mstr.Replace(match.Index, match.Length, await Replacement(template, regex, match));
						str = mstr.ToPlainText();
					}
				}
			}
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// A player's pattern that cannot finish within SoftcodeRegex.MatchTimeout: an answer, not a crash.
			return new CallState(ErrorMessages.Returns.RegexpTimeout) { HadErrors = hadErrors };
		}
		catch (ArgumentException)
		{
			return new CallState(ErrorMessages.Returns.RegexpInvalid) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.RegexRegisters.TryPop(out _);
		}

		return new CallState(mstr) { HadErrors = hadErrors };
	}

	[SharpFunction(Name = "set", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object/attribute", "flag or attribute:value"])]
	public async ValueTask<CallState> Set(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var result = await SetHelpers.DoSet(parser, LocateService, AttributeService, FlagAndPowerService,
			NotifyService, executor,
			parser.CurrentState.Arguments["0"].Message!,
			parser.CurrentState.Arguments["1"].Message!);

		// fun_set (src/fundb.c) hands do_set the call and writes NOTHING to buff afterwards, whatever
		// do_set made of it — "This function returns nothing" (help set()). do_set has already told the
		// executor about any failure, so returning the #-1 here would report it a second time, as the
		// enclosing think/@pemit's own output. The command keeps the return; Penn's cmd_set has none
		// to keep, and SharpMUSH's @SET result is asserted on by AttributeTreePatternVisibilityTests.
		return result.Message?.ToPlainText().StartsWith("#-1", StringComparison.Ordinal) == true
			? CallState.Empty
			: result;
	}

	[SharpFunction(Name = "subj", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> SubjectivePronoun(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
			LocateFlags.All,
			async onObject => await AttributeHelpers.GetPronoun(AttributeService, Mediator, parser, onObject,
				Configuration.CurrentValue.Attribute.GenderAttribute,
				Configuration.CurrentValue.Attribute.SubjectivePronounAttribute,
				x => x switch
				{
					"M" or "Male" => "he",
					"F" or "Female" => "she",
					_ => "they"
				}));
	}

	[SharpFunction(Name = "udefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse, ParameterNames = ["object/attribute", "default", "arguments..."])]
	public async ValueTask<CallState> UserAttributeDefault(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var selector = await parser.FunctionParse(parser.CurrentState.Arguments["0"].Message!) ?? CallState.Empty;
		var hadErrors = selector.HadErrors;
		CallState Preserve(CallState result) => result with { HadErrors = hadErrors || result.HadErrors };
		var objectAndAttribute = selector.Message ?? MarkupText.Empty;
		var split = HelperFunctions.SplitObjectAndAttr(objectAndAttribute.ToPlainText());
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (split is not { Object: var objectName, Attribute: var attributeName })
			return Preserve(new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, nameof(Get).ToUpperInvariant())));
		return Preserve(await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			objectName, LocateFlags.All, async actualObject =>
			{
				var attribute = await AttributeService.GetAttributeAsync(executor, actualObject, attributeName,
					mode: IAttributeService.AttributeMode.Execute, parent: true);
				if (attribute is not SharpAttribute[] chain)
					return await parser.FunctionParse(parser.CurrentState.Arguments["1"].Message!) ?? CallState.Empty;

				var arguments = new Dictionary<string, CallState>();
				foreach (var argument in parser.CurrentState.ArgumentsOrdered.Skip(2))
				{
					var result = await parser.FunctionParse(argument.Value.Message!) ?? CallState.Empty;
					hadErrors |= result.HadErrors;
					arguments[arguments.Count.ToString()] = result;
				}
				var value = chain.Last();
				return (await parser.With(state => state with
				{
					CurrentEvaluation = new DBAttribute(actualObject.Object().DBRef, value.Name),
					Arguments = arguments,
					EnvironmentRegisters = arguments,
					Executor = actualObject.Object().DBRef,
					Caller = state.Executor
				}, p => p.FunctionParse(value.Value)))!;
			}));
	}

	[SharpFunction(Name = "uldefault", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse | FunctionFlags.Localize, ParameterNames = ["object/attribute", "default", "arguments..."])]
	public ValueTask<CallState> UserAttributeLocalizedDefault(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> UserAttributeDefault(parser, attribute);

	[SharpFunction(Name = "ufun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<CallState> UserAttributeFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var result = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		return result;
	}

	/// <summary>
	/// PennMUSH's <c>fun_pfun</c> (<c>funufun.c:244-292</c>): the attribute is read from the executor's
	/// parent through <c>atr_get</c> (the parent's own chain and type ancestor), with no permission
	/// check; a no_inherit or internal attribute is refused even on the parent itself; and the code runs
	/// as the calling object, not the parent. No parent, no attribute: nothing.
	/// </summary>
	[SharpFunction(Name = "pfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["attribute", "arguments..."])]
	public async ValueTask<CallState> ParentFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var attributeName = parser.CurrentState.Arguments["0"].Message!.ToPlainText().ToUpperInvariant();

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.Object().Parent.WithCancellation(CancellationToken.None) is not AnySharpObject parentObject)
		{
			return CallState.Empty;
		}

		var god = await HelperFunctions.GetGod(Mediator);
		if (await AttributeService.GetAttributeAsync(god, parentObject, attributeName,
					IAttributeService.AttributeMode.Read, parent: true) is not SharpAttribute[] chain)
		{
			return CallState.Empty;
		}

		var found = chain.Last();
		if (found.IsInternal() || found.IsNoInherit())
		{
			return CallState.Empty;
		}

		var arguments = parser.CurrentState.ArgumentsOrdered.Skip(1)
			.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
			.ToDictionary();

		return await AttributeService.CallAttributeFunctionAsync(parser.Push(parser.CurrentState with
		{
			Arguments = arguments,
			EnvironmentRegisters = new Dictionary<string, CallState>(arguments)
		}), new AttributeFunction(executor, found.LongName.ToUpperInvariant(), found.Value));
	}

	[SharpFunction(Name = "ulambda", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["parameters", "expression", "arguments..."])]
	public async ValueTask<CallState> UserFunctionLambda(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var result = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser,
			executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary());

		return result;
	}

	[SharpFunction(Name = "ulocal", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular | FunctionFlags.Localize, ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<CallState> UserAttributeLocalized(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.ArgumentsOrdered.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value)).ToDictionary());
	}

	[SharpFunction(Name = "v", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["attribute"])]
	public async ValueTask<CallState> Variable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var plainText = arg0.ToPlainText();

		switch (plainText)
		{
			case "#":
				return (await parser.CurrentState.KnownEnactorObject(Mediator)).Object().DBRef;
			case "@":
				return (await parser.CurrentState.KnownCallerObject(Mediator)).Object().DBRef;
			case "!":
				return (await parser.CurrentState.KnownExecutorObject(Mediator)).Object().DBRef;
			case "n" or "N":
				return (await parser.CurrentState.KnownEnactorObject(Mediator)).Object().Name;
			case "l" or "L":
				return (await (await parser.CurrentState.KnownEnactorObject(Mediator)).Where()).Object().DBRef;
			case "c" or "C":
				return Substitutions.Substitutions.CommandBeforeEvaluation(parser);
			default:
				// fun_v reads %0-%9 only for a single digit (src/fundb.c:452-468); anything longer,
				// "10" included, is an attribute name.
				if (plainText is [>= '0' and <= '9'])
				{
					return parser.CurrentState.EnvironmentRegisters.TryGetValue(plainText, out var value)
						? value
						: CallState.Empty;
				}

				// v(attributename) is equivalent to get(me/attributename)
				var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
				var maybeAttr = await AttributeService.GetAttributeAsync(
					executor,
					executor,
					plainText,
					mode: IAttributeService.AttributeMode.Read,
					parent: true);

				return maybeAttr switch
				{
					SharpAttribute[] chain => new CallState(chain.Last().Value),
					None => CallState.Empty,
					Error<string> error => new CallState(error.Value)
				};
		}
	}

	[SharpFunction(Name = "visible", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "looker"])]
	public async ValueTask<CallState> Visible(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;

		var obj = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var victimAttribute = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var victAttr = HelperFunctions.SplitDbRefAndOptionalAttr(victimAttribute);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (victAttr is not { Object: var victim, Attribute: var attr })
		{
			return ErrorMessages.Returns.BadArgumentFormat;
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, obj,
			LocateFlags.All,
			async foundObj =>
			{
				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, victim,
					LocateFlags.All,
					async foundVictim =>
					{
						if (attr is null)
						{
							return await PermissionService.CanSee(foundObj, foundVictim);
						}

						// fun_visible (fundb.c:981): atr_get, then Can_Read_Attr for the looker.
						var realAttr = await AttributeService.GetAttributeAsync(foundObj, foundVictim, attr,
							IAttributeService.AttributeMode.Read, true);

						if (realAttr is not SharpAttribute[] chain)
						{
							return false;
						}

						return await PermissionService.CanViewAttribute(foundObj, foundVictim, chain);
					});
			}
		);
	}

	[SharpFunction(Name = "xattr", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "start", "count", "delimiter"])]
	public ValueTask<CallState> NumberRangeAttribute(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributeRangeAsync(parser, attribute.Name, false, IAttributeService.AttributePatternMode.Wildcard);

	[SharpFunction(Name = "xattrp", MinArgs = 3, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "start", "count", "delimiter"])]
	public ValueTask<CallState> NumberRangeAttributeParent(IMUSHCodeParser parser, SharpFunctionAttribute attribute)
		=> AttributeRangeAsync(parser, attribute.Name, true, IAttributeService.AttributePatternMode.Wildcard);

	[SharpFunction(Name = "xget", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "attribute"])]
	public ValueTask<CallState> AlternativeGet(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> GetAttributeValueAsync(parser,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			parser.CurrentState.Arguments["1"].Message!.ToPlainText());

	[SharpFunction(Name = "zfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular, ParameterNames = ["zone", "attribute", "arguments..."])]
	public async ValueTask<CallState> ZoneFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (await enactor.Object().Zone.WithCancellation(CancellationToken.None) is not AnySharpObject zone)
		{
			return new CallState(ErrorMessages.Returns.NoZoneSet);
		}

		var result = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser,
			zone,
			objAndAttribute: parser.CurrentState.Arguments["0"].Message!,
			args: parser.CurrentState.Arguments.Skip(1)
				.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
				.ToDictionary(),
			ignoreLambda: true);

		return result;
	}
}