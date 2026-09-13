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
			|| context == NameContext.Speech && configuration.CurrentValue.Cosmetic.Monikers;
		return moniker && await attributes.GetAttributeAsync(obj, obj, "MONIKER", IAttributeService.AttributeMode.Read) is SharpAttribute[] style
			? ApplyMoniker(name, style.Last().Value) : MString.Plain(name);
	}

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
