# SharpMUSH
<img align="left" width="300em" src="./Solution Files/Logo.svg"/>
SharpMUSH is an experiment in writing parsers and server code.

It takes its basis and functionality heavily from PennMUSH and its brethren with the intent of a compatibility layer.

![F#](https://img.shields.io/badge/f%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)
![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white)
![.Net](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)
![Visual Studio](https://img.shields.io/badge/Visual%20Studio-5C2D91.svg?style=for-the-badge&logo=visual-studio&logoColor=white)

[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/SharpMUSH/SharpMUSH/dotnet.yml?style=for-the-badge)](https://github.com/SharpMUSH/SharpMUSH/actions/workflows/dotnet.yml)
[![Discord](https://img.shields.io/discord/1216626296642343044?style=for-the-badge&refresh=1)](https://discord.gg/jYErRbqaC9)

<br/>
<br/>
<br/>
<br/>
<br/>

## How to Download
At this time, there is no release candidate, as this project is not ready for production. You are invited to contribute.

## How to Build and Test
- Install [.Net 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Install [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Install [Java](https://adoptium.net/) (Optional) to change the Parser.

Build with:
```bash
dotnet build
```

Run the tests with:
```bash
dotnet Test
```

The main entrypoint to set as a Startup Project is SharpMUSH.Server
