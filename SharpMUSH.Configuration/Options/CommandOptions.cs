namespace SharpMUSH.Configuration.Options;

public class CommandOptions(
	bool NoisyWhisper = false,
	bool PossessiveGet = true,
	bool PossessiveGetD = false,
	bool LinkToObject = true,
	bool OwnerQueues = false,
	bool FullInvisibility = false,
	bool WizardNoAEnter = false,
	bool ReallySafe = true,
	bool DestroyPossessions = true,
	uint ProbateJudge = 1
);