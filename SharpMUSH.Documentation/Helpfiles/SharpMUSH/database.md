<!-- help-article
{
  "corpus": "help",
  "id": "database",
  "lookup": "database",
  "aliases": [
    "DBREFS",
    "DBREF NUMBER",
    "DBREF #"
  ],
  "sections": [
    {
      "id": "finding-object-references",
      "heading": "Finding object references",
      "lookup": "database finding object references"
    }
  ],
  "redirects": {
    "DBREF2": "database finding object references"
  }
}
-->
# Database

You will find the term "dbref" or "dbref number" used frequently in these help files and in MUSHcode. It is an abbreviation of "database reference number".

The database is the part of MUSH that stores all the information about this particular MUSH. Players, things, rooms, and exits are all objects in the database. Each object in the database has a unique dbref number that is set when the object is created. You can use the dbref number to refer to an object that is not in your current location, For long-lived or global code, store the full object ID returned by `objid()` (`#N:creation`) instead of a bare dbref. A recycled dbref may identify a replacement object, while an objid identifies one particular lifetime. See [objid()].

Using DBREFs is also faster than using names, even if the object is in your location. This is because whenever you try to do something with an object (such as look at it, take it, etc.), the MUSH first has to locate the object. Since the dbref is unique, it can immediately find the object rather than checking through all the contents of your area to see if one matches the name.

## Finding object references

If you own or control an object, you will see its dbref number listed right after its name when you look at it (unless you are set MYOPIC).


Example:
```sharp
    > look me
    Cyclonus(#3PWenAMc)
    A very short desc.
```
  
The dbref number is indicated by the number/pound sign (#). Cyclonus's dbref is #3. The letters following the dbref are the abbreviations of the flags set on the object. NOTE: the abbreviation of the OPAQUE flag is 'O' (o), which looks like '0' (zero) on some clients. Make sure you have the right number before using it in your code!

::: seealso
- [MYOPIC]
- [OPAQUE]
- [mushcode]
- [MATCHING]
- [OBJIDS]
:::
