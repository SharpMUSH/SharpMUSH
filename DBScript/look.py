
Location = MUSH.Data.GetPlayerLocation(MUSH.Caller)
Description = MUSH.Data.GetAttribute(Location.Id, "DESCRIBE")
MUSH.Pemit(MUSH.Caller, Location.Name + "(#" + str(Location.Id) + ")\n" + Description + "\n\nContents:\n")
Contents = MUSH.Data.Contents(Location.Id)
for Object in Contents:
    Object.GetType() is not null:
        MUSH.Pemit(MUSH.Caller, "\t" + Object.Name + "(#" + str(Object.Id) + ")\n")
