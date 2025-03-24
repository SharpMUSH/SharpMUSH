# SharpMUSH
<img align="left" width="300em" src="./Solution Files/Logo.svg"/>
SharpMUSH is an experiment in writing parsers and server code.

It takes its basis and functionality heavily from PennMUSH and its brethren with the intent of a compatibility layer.

![F#](https://img.shields.io/badge/f%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)
![.Net](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)
![Visual Studio](https://img.shields.io/badge/Visual%20Studio-5C2D91.svg?style=for-the-badge&logo=visual-studio&logoColor=white)<br/>
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/SharpMUSH/SharpMUSH/dotnet.yml?style=for-the-badge)](https://github.com/SharpMUSH/SharpMUSH/actions/workflows/dotnet.yml)
[![Discord](https://img.shields.io/discord/1216626296642343044?style=for-the-badge&refresh=1)](https://discord.gg/jYErRbqaC9)

<br/>
<br/>
<br/>
<br/>
<br/>
<br/>
<br/>

# Why SharpMUSH
SharpMUSH is a modern iteration of the time tested MUSH frameworks. It provides a layer of compatibility for PennMUSH for transferability, and building a modern tech landscape around it that does away with many of the limitations that have made MUSHes harder use, without losing what makes them great.

## Advantages
* A database layer that allows the use of other databases to store, access and maintain its data - such as ArangoDB - as well as expanding upon its capabilities.
* Full Unicode support, as well as both Websocket and Telnet negotiation and communication.
* A buffer size that does not get in the way, allowing for better integration with modern web applications, use cases, and avoids user confusion.
* Written in C#, allowing memory safety and easier community maintenance and improvements with support for all Operating Systems.
* Designed with a wide range of Unit Tests, ensuring the performance and capability remains as the project matures: from Telnet to ANSI to commands and function parsing.
* Uses Docker to make Installation as easy as possible.

## Disadvantage
* A less mature code base.
* Dropped support for non-standard Pueblo items, such as Panes.
* One way database transfers - SharpMUSH provides a tool to take a PennMUSH database and stores it in its own. It does not provide a way to go back beyond decompiling.

## Incompatibilities
SharpMUSH does away with some of the more unique aspects of PennMUSH parser curiosities.
* No support for unbalanced parenthesis.
  * In PennMUSH, the following is legal. It is not in SharpMUSH: 
    * `think add(1,2` -> `3`
    * `think add(1,2))` -> `3)`
* No support for full recursion parsing.
  * In PennMUSH, TinyMUX, etc, you can do the following:
    * `&fn me=ucstr; think [v(fn)](foo)` -> `FOO`.
  * This is an awesome feature of MUSH-likes, but we explicitly do not support this, as it makes the parser non-deterministic and harder to maintain. Most Softcode does not rely on this behavior.
  * SharpMUSH introduces `callfn` and `@callcmd` to replace this loss.

## Future
* Direct web integrations, such as Scene Sys and a webclient integration.
* Modern development tools such as syntax highlighting and multi line editing of MUSHcode in Command or Function mode.
* Mod/Plugin support for commands, functions, etc, allowing for easier community improvements to the SharpMUSH ecosystem. This allows code to be written in C# instead of MUSHcode if so preferred.

## How to Download
At this time, there is no release candidate, as this project is not ready for production. You are invited to contribute.

## How to Build and Test
- Install [.Net 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Install [Docker Desktop](https://www.docker.com/products/docker-desktop/)

Build with:
```bash
dotnet build
```

Run the tests with:
```bash
dotnet test
```

The main entrypoint to set as a Startup Project is SharpMUSH.Server
