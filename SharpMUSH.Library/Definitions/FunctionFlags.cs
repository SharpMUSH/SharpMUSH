namespace SharpMUSH.Library.Definitions;

[Flags]
public enum FunctionFlags
{
	Regular = 0,
	NoParse = 1 << 0,
	Literal = 1 << 1,
	Arg_Mask = NoParse | Literal,
	Disabled = 1 << 3,
	NoGagged = 1 << 4,
	NoGuest = 1 << 5,
	NoFixed = 1 << 6,
	WizardOnly = 1 << 7,
	AdminOnly = 1 << 8,
	GodOnly = 1 << 9,
	HasSideFX = 1 << 12,
	LogName = 1 << 13,
	LogArgs = 1 << 14,
	Localize = 1 << 15,
	StripAnsi = 1 << 17,
	Deprecated = 1 << 18,
	IntegersOnly = 1 << 20,
	PositiveIntegersOnly = 1 << 21,
	DecimalsOnly = 1 << 22,
	EvenArgsOnly = 1 << 23,
	UnEvenArgsOnly = 1 << 24,
	/// <summary>Arguments accepted by double.TryParse; empty arguments mean zero.</summary>
	NumbersOnly = 1 << 25
}