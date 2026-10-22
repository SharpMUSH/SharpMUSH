using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public enum NameContext { Plain, Accented, UnaccentedMoniker, AccentedMoniker, Speech, NoSpoof }

/// <summary>Formats actual object names; cosmetic attributes never replace identity with an alias.</summary>
public sealed class NameFormatter(IAttributeService attributes, IOptionsWrapper<SharpMUSHOptions> configuration)
{
	public async ValueTask<MString> FormatAsync(AnySharpObject obj, NameContext context)
	{
		if (context is NameContext.Speech or NameContext.NoSpoof
			&& await IsInvisibleAsync(obj, configuration.CurrentValue.Command.FullInvisibility))
			return MString.Plain(obj.IsPlayer ? "Someone" : "Something");
		var name = obj.Object().Name;
		if (context is NameContext.Accented or NameContext.AccentedMoniker or NameContext.Speech
			&& await attributes.GetAttributeAsync(obj, obj, "NAMEACCENT", IAttributeService.AttributeMode.Read) is SharpAttribute[] accent)
			name = AccentTemplate.Apply(name, accent.Last().Value.ToPlainText());
		var moniker = context is NameContext.UnaccentedMoniker or NameContext.AccentedMoniker
			|| context == NameContext.Speech && await ShowsMonikerAsync(obj, MonikerPlace.Say);
		return moniker ? await MonikeredAsync(obj, name) : MString.Plain(name);
	}

	/// <summary>
	/// PennMUSH's <c>AName</c>: the object's name, coloured by its <c>@moniker</c> when the <c>monikers</c> option
	/// names <paramref name="place"/> and the object's type, or names the place and the object is set MONIKER.
	/// </summary>
	public async ValueTask<MString> NameAsync(AnySharpObject obj, MonikerPlace place, string? name = null)
		=> (await MonikerAsync(obj, place, name ?? obj.Object().Name)).Name;

	/// <summary>
	/// The name as PennMUSH's <c>real_unparse</c> writes it: coloured by its <c>@moniker</c> where
	/// <paramref name="place"/> shows one, hilighted otherwise.
	/// </summary>
	public async ValueTask<MString> UnparseNameAsync(AnySharpObject obj, MonikerPlace place)
		=> await MonikerAsync(obj, place, obj.Object().Name) is (var styled, true) ? styled : obj.Object().Name.Hilight();

	/// <summary>Whether the <c>monikers</c> option shows <paramref name="obj"/>'s moniker in <paramref name="place"/>.</summary>
	public async ValueTask<bool> ShowsMonikerAsync(AnySharpObject obj, MonikerPlace place)
	{
		var shown = MonikerPlaces.Parse(configuration.CurrentValue.Cosmetic.MonikerList);
		if ((shown & place) == 0) return false;
		var type = obj.IsPlayer ? MonikerPlace.Players : obj.IsRoom ? MonikerPlace.Rooms : obj.IsExit ? MonikerPlace.Exits : MonikerPlace.Things;
		return (shown & type) != 0 || await obj.HasFlag("MONIKER");
	}

	/// <summary><paramref name="name"/> coloured by the object's <c>@moniker</c> where <paramref name="place"/>
	/// shows one, and whether it was: an unset or uncoloured <c>@moniker</c> leaves the name as it is.</summary>
	private async ValueTask<(MString Name, bool Monikered)> MonikerAsync(AnySharpObject obj, MonikerPlace place, string name)
		=> await ShowsMonikerAsync(obj, place)
			&& await attributes.GetAttributeAsync(obj, obj, "MONIKER", IAttributeService.AttributeMode.Read) is SharpAttribute[] style
			&& style.Last().Value.Runs.Length > 0
				? (ApplyMoniker(name, style.Last().Value), true)
				: (MString.Plain(name), false);

	private async ValueTask<MString> MonikeredAsync(AnySharpObject obj, string name)
		=> await attributes.GetAttributeAsync(obj, obj, "MONIKER", IAttributeService.AttributeMode.Read) is SharpAttribute[] style
			? ApplyMoniker(name, style.Last().Value) : MString.Plain(name);

	/// <summary>Uses each grapheme's UTF-16 start position to select template styling, extending the
	/// final template position for longer names without splitting surrogate pairs or combining clusters.</summary>
	public static MString ApplyMoniker(string name, MString template)
	{
		if (template.Length == 0 || template.Runs.Length == 0) return MString.Plain(name);
		var pieces = new List<MString>();
		var runIndex = 0;
		var graphemes = System.Globalization.StringInfo.GetTextElementEnumerator(name);
		while (graphemes.MoveNext())
		{
			var position = Math.Min(graphemes.ElementIndex, template.Length - 1);
			while (runIndex < template.Runs.Length && template.Runs[runIndex].End <= position) runIndex++;
			pieces.Add(runIndex < template.Runs.Length && template.Runs[runIndex].Start <= position
				? MString.Wrap(template.Runs[runIndex].Markups, graphemes.GetTextElement()) : MString.Plain(graphemes.GetTextElement()));
		}
		return MString.Concat(pieces);
	}

	private static async ValueTask<bool> IsInvisibleAsync(AnySharpObject obj, bool fullInvisibility)
		=> fullInvisibility && await obj.IsDarkLegal(ExecutionBudget.CurrentToken);

	private static async ValueTask<bool> EffectiveFlag(AnySharpObject obj, string flag)
		=> await obj.HasFlag(flag) || await obj.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken) is { } owner
			&& await ((AnySharpObject)owner).HasFlag(flag);

	/// <summary>Final-recipient attribution, also used by direct puppet publication after its own prefix.</summary>
	public static async ValueTask<MString> HeaderAsync(AnySharpObject recipient, AnySharpObject speaker,
		INotifyService.NotificationType type, bool fullInvisibility = false, AnySharpObject? puppet = null)
	{
		if (type is INotifyService.NotificationType.Announce or INotifyService.NotificationType.NSAnnounce
			or INotifyService.NotificationType.NSEmit or INotifyService.NotificationType.NSPrivateEmit
			or INotifyService.NotificationType.NSSay or INotifyService.NotificationType.NSPose or INotifyService.NotificationType.NSSemiPose)
			return MString.Empty;
		if (!await EffectiveFlag(recipient, "NOSPOOF") && (puppet is null || !await EffectiveFlag(puppet, "NOSPOOF"))) return MString.Empty;
		var paranoid = await EffectiveFlag(recipient, "PARANOID") || puppet is not null && await EffectiveFlag(puppet, "PARANOID");
		if (!paranoid && recipient.Object().DBRef == speaker.Object().DBRef) return MString.Empty;
		if (paranoid)
		{
			var owner = await speaker.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			var identity = $"{speaker.Object().Name}(#{speaker.Object().DBRef.Number})";
			return MString.Plain(owner is not null && owner.Object.DBRef != speaker.Object().DBRef
				? $"[{owner.Object.Name}(#{owner.Object.DBRef.Number})'s {identity}] " : $"[{identity}] ");
		}
		var name = await IsInvisibleAsync(speaker, fullInvisibility) ? speaker.IsPlayer ? "Someone" : "Something" : speaker.Object().Name;
		return MString.Plain($"[{name}:] ");
	}
}
