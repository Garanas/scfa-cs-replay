# The hosted viewer: FAForever.Replay.Server serving the Blazor WebAssembly app (FAForever.Replay.Viewer)
# plus its OAuth token proxy. Built in CI and pushed to ghcr.io/garanas/scfa-cs-replay; the server
# that runs it is configured in github.com/Garanas/jipwijnia-vps.
#
#   docker build -t scfa-cs-replay .
#   docker run --rm -p 8080:8080 scfa-cs-replay      # http://localhost:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files only, so the layer is reused while only code changes.
COPY global.json Directory.Packages.props ./
COPY FAForever.Replay/FAForever.Replay.csproj FAForever.Replay/
COPY FAForever.Replay.Viewer/FAForever.Replay.Viewer.csproj FAForever.Replay.Viewer/
COPY FAForever.Replay.Server/FAForever.Replay.Server.csproj FAForever.Replay.Server/
RUN dotnet restore FAForever.Replay.Server/FAForever.Replay.Server.csproj

# The committed wwwroot/css/app.css is used: the Tailwind CLI (tools/tailwindcss.exe) is Windows-only.
COPY FAForever.Replay/ FAForever.Replay/
COPY FAForever.Replay.Viewer/ FAForever.Replay.Viewer/
COPY FAForever.Replay.Server/ FAForever.Replay.Server/
# The commit, shown in the viewer's footer; .git is not in the build context, so CI passes it.
ARG SOURCE_REVISION=
RUN dotnet publish FAForever.Replay.Server/FAForever.Replay.Server.csproj -c Release -o /app --no-restore \
    -p:SourceRevisionId=${SOURCE_REVISION}

# Chiseled: no shell or package manager, runs as the non-root "app" user, listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .

# Workstation GC: server GC reserves far more memory than a static host with a token proxy needs.
ENV DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "FAForever.Replay.Server.dll"]
