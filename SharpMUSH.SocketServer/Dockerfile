# Stage 1: Build
# Built on the runner's own platform and compiled ahead of time for the image's (PublishAot in the
# project): an arm64 image needs no emulated compile. Native AOT links with clang; for the other
# architecture it links against that architecture's cross C library.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
ARG BUILDARCH
RUN case "$TARGETARCH" in amd64) TRIPLE=x86-64-linux-gnu ;; arm64) TRIPLE=aarch64-linux-gnu ;; \
      *) echo "Unsupported TARGETARCH '$TARGETARCH': build with BuildKit (docker buildx)" >&2; exit 1 ;; esac \
    && CROSS= && if [ "$TARGETARCH" != "$BUILDARCH" ]; then CROSS="binutils-$TRIPLE gcc-$TRIPLE libc6-dev-$TARGETARCH-cross"; fi \
    && apt-get update && apt-get install -y --no-install-recommends clang llvm $CROSS \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN case "$TARGETARCH" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; esac \
    && dotnet publish SharpMUSH.SocketServer/SharpMUSH.SocketServer.csproj -c Release -o /app -r $RID \
    && rm -f /app/*.pdb /app/*.dbg

# Stage 2: Run. A native binary needs no .NET runtime, only the libraries it calls (ICU, OpenSSL).
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
EXPOSE 4201 4202 4203
ENV ASPNETCORE_URLS=http://+:4202
ENTRYPOINT ["/app/SharpMUSH.SocketServer"]
