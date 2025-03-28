# SharpMUSH
<img align="left" width="300em" src="./Solution Files/Logo.svg" alt="A sharp logo for SharpMUSH."/>
SharpMUSH is modern iteration of a style of <em>text-based role-playing</em> servers referred to as '<b>MUSHes</b>' or '<b>MU*</b>' written with more modern needs in mind.
<br/>
<br/>
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

# Documentation
You can find our documentation here: [SharpMUSH Documentation](https://sharpmush.com).
This features such elements as our Compatibility, Installation, and API documentation, and how to download SharpMUSH and get up and running!

# Release Status
Currently, there is no Release Candidate for SharpMUSH. 
We are still in the early stages of development, and are working on getting a stable release out.

# Quick Contribution Guide
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
