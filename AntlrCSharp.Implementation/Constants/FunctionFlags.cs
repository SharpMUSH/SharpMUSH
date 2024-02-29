﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AntlrCSharp.Implementation.Constants
{
	[Flags]
	public enum FunctionFlags
	{
		Regular = 0,
		NoParse = 1 << 0, // Implemented
		Literal = 1 << 1,
		Arg_Mask = 1 << 2,
		Disabled = 1 << 3,
		NoGagged = 1 << 4,
		NoGuest = 1 << 5,
		NoFixed = 1 << 6,
		WizardOnly = 1 << 7,
		AdminOnly = 1 << 8,
		GodOnly = 1 << 9,
		BuiltIn = 1 << 10,
		Override = 1 << 11,
		NoSideFX = 1 << 12,
		LogName = 1 << 13,
		LogArgs = 1 << 14,
		Localize = 1 << 15,
		UserFunction = 1 << 16,
		StripAnsi = 1 << 17,
		Deprecated = 1 << 18,
		Clone = 1 << 19
	}
}
