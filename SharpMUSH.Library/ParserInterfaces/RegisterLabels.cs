using System.Globalization;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// RhostMUSH's q-register labels: <c>setq(&lt;register&gt;, &lt;value&gt;, &lt;label&gt;)</c> gives a register a
/// second name, so <c>%q&lt;label&gt;</c>, <c>r(label)</c> and <c>setq(label, ...)</c> read and write that
/// register. A register has at most one label and a label names one register.
/// <para>Labels live in the register frame itself, under keys no register name can take (register names
/// are <c>[A-Z0-9_.-]+</c>), so everything that saves, clears, copies or restores a frame — localize(),
/// ulocal(), <c>/localize</c>, <c>/clearregs</c>, a queued action's snapshot — carries the labels along
/// with the values, as RhostMUSH saves and wipes <c>global_regsname</c> beside <c>global_regs</c>.
/// Anything that lists a frame's registers skips these keys with <see cref="IsLabelKey"/>.</para>
/// </summary>
public static class RegisterLabels
{
	/// <summary>Prefix of the key holding a register's label: <c>\u0001REGISTER</c> → label as written.</summary>
	private const char LabelOfRegister = '\u0001';

	/// <summary>Prefix of the key naming a label's register: <c>\u0002LABEL</c> → register name.</summary>
	private const char RegisterOfLabel = '\u0002';

	/// <summary>The registers <c>setq(+, ...)</c> and <c>setq(!, ...)</c> choose from, in order.</summary>
	public static readonly string[] PickOrder =
		[.. "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString())];

	/// <summary>Whether <paramref name="key"/> is a label entry rather than a register.</summary>
	public static bool IsLabelKey(string key) => key.Length > 0 && key[0] is LabelOfRegister or RegisterOfLabel;

	/// <summary>
	/// A label RhostMUSH accepts: at least two characters, no <c>&gt;</c> (which separates register and
	/// label in setqm()), and not a number, since numbers name registers here.
	/// </summary>
	public static bool IsValidLabel(string label)
		=> label.Length >= 2
			&& !label.Contains('>')
			&& !decimal.TryParse(label, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture, out _);

	/// <summary>The register <paramref name="name"/> names: the labelled register for a label, else the name itself.</summary>
	public static string Resolve(Dictionary<string, MString> frame, string name)
		=> frame.TryGetValue(RegisterOfLabel + name.ToUpperInvariant(), out var register)
			? register.ToPlainText()
			: name;

	/// <summary>The label of <paramref name="register"/>, as it was written.</summary>
	public static bool TryGetLabel(Dictionary<string, MString> frame, string register, out string label)
	{
		var found = frame.TryGetValue(LabelOfRegister + register.ToUpperInvariant(), out var value);
		label = found ? value!.ToPlainText() : string.Empty;
		return found;
	}

	/// <summary>Whether <paramref name="label"/> names a register in this frame.</summary>
	public static bool IsInUse(Dictionary<string, MString> frame, string label)
		=> frame.ContainsKey(RegisterOfLabel + label.ToUpperInvariant());

	/// <summary>
	/// Gives <paramref name="register"/> the label <paramref name="label"/>. The register's old label is
	/// dropped, and so is the label's hold on any other register.
	/// </summary>
	public static void Set(Dictionary<string, MString> frame, string register, string label)
	{
		register = register.ToUpperInvariant();
		Clear(frame, register);
		if (frame.TryGetValue(RegisterOfLabel + label.ToUpperInvariant(), out var previous))
			frame.Remove(LabelOfRegister + previous.ToPlainText());
		frame[LabelOfRegister + register] = MarkupText.Plain(label);
		frame[RegisterOfLabel + label.ToUpperInvariant()] = MarkupText.Plain(register);
	}

	/// <summary>Removes <paramref name="register"/>'s label, if it has one.</summary>
	public static void Clear(Dictionary<string, MString> frame, string register)
	{
		register = register.ToUpperInvariant();
		if (!frame.Remove(LabelOfRegister + register, out var old)) return;
		frame.Remove(RegisterOfLabel + old.ToPlainText().ToUpperInvariant());
	}

	/// <summary>Every register of the frame with a value or a label, by name.</summary>
	public static IEnumerable<string> Registers(Dictionary<string, MString> frame)
		=> frame.Keys
			.Select(key => key.Length > 0 && key[0] == LabelOfRegister ? key[1..] : key)
			.Where(key => !IsLabelKey(key))
			.Distinct();

	/// <summary>
	/// The register <c>setq(+, ...)</c> or <c>setq(!, ...)</c> uses for <paramref name="label"/>: the one
	/// it already names, else the first of 0-9 and A-Z with no label, skipping ones holding a value when
	/// <paramref name="skipSet"/> (the <c>+</c> form).
	/// </summary>
	public static bool TryPick(Dictionary<string, MString> frame, string label, bool skipSet, out string register)
	{
		if (frame.TryGetValue(RegisterOfLabel + label.ToUpperInvariant(), out var named))
		{
			register = named.ToPlainText();
			return true;
		}

		register = PickOrder.FirstOrDefault(candidate => !frame.ContainsKey(LabelOfRegister + candidate)
			&& !(skipSet && frame.TryGetValue(candidate, out var value) && value.Length > 0)) ?? string.Empty;
		return register.Length > 0;
	}
}
