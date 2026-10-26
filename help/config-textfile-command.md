<!-- help-article
{
  "corpus": "help",
  "id": "config-textfile-command",
  "lookup": "@config textfile",
  "aliases": [],
  "sections": []
}
-->
# @config textfile

Where the help, news and other text files come from.

- `text_files_directory=<string>`: The folder the text files are read from. Each folder in it is a category, such as `help`, `ahelp` and `news`, read by the help commands and [textfile()]. A relative path is beside the server program. `TextFiles` by default.
- `text_files_cache_on_startup=<boolean>`: Read and index every text file when the server starts? On by default. When it is off, no text file is found until a wizard runs [@readcache].
- `enable_markdown_rendering=<boolean>`: Accepted but ignored.
