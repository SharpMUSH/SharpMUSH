ObjAttrib = MUSH.Data.GetAttribute(Caller, "Editing")
NameAttrib = MUSH.Data.GetAttribute(Caller, "EditingName")

if "#-1" not in ObjAttrib and "#-1" not in NameAttrib:
    command = MUSH.Data.GetCommand(int(ObjAttrib), NameAttrib)
    command.Value += Input + "\n"
    MUSH.Data.SetCommand(command)
    MUSH.Pemit(MUSH.Caller, "Editing " + NameAttrib + " on #" + ObjAttrib + ":\n" + Input + "\nSend . to save and exit.")
else:
    MUSH.Pemit(MUSH.Caller, "Editing Error has occured, exiting edit mode.")
    MUSH.Data.SetPlayerEditMode(Caller, False)

    
