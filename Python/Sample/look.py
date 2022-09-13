
Location = DB.GetPlayerLocation(Caller)
Description = DB.GetAttribute(Location.Id, "DESCRIBE")
Notify.Pemit(Caller, Format.Color("#FFCC00", "#000000",Location.Name + "(#" + str(Location.Id) + ")") + "\n" + Description + "\n\n"+ Format.Color("#0000CD", "#000000","Contents:") +"\n")
Contents = DB.Contents(Location.Id)
for Object in Contents:
    if Object is not None:
        Notify.Pemit(Caller, "\t" + Object.Name + "(#" + str(Object.Id) + ")\n")
