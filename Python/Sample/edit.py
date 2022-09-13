
CmdObj = int(MUSH.Args[0])
CmdName = MUSH.Args[1]
CmdCommand = MUSH.Command

MUSH.Data.SetPlayerEditMode(MUSH.Caller, True)
MUSH.Data.SetPlayerEditObject(MUSH.Caller, CmdObj, CmdName, CmdCommand)

MUSH.Pemit(MUSH.Caller, "Entering Edit Mode for " + CmdName + " on #" + str(CmdObj) + ":\n" + CmdCommand + "\nSend . to save and exit.")