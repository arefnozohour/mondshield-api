# MondShield API — Linux container (Stub MT5 mode).
# The MT5 native DLLs are Windows-only, so live MT5 can't run here; -p:TargetLinux=true skips the
# x64/win-x64 pinning and native-DLL copy and produces a portable build. Live MT5 needs a Windows
# host (see DEPLOY.md).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching): copy the manifests the restore graph needs.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config ./.config
COPY src/MondShield.Domain/MondShield.Domain.csproj        src/MondShield.Domain/
COPY src/MondShield.Application/MondShield.Application.csproj src/MondShield.Application/
COPY src/MondShield.Infrastructure/MondShield.Infrastructure.csproj src/MondShield.Infrastructure/
COPY src/MondShield.Api/MondShield.Api.csproj               src/MondShield.Api/
# The MT5 managed wrappers are referenced by HintPath, so they must exist for restore/build.
COPY libs/ libs/
RUN dotnet restore src/MondShield.Api/MondShield.Api.csproj -p:TargetLinux=true

# Copy the rest and publish a portable (framework-dependent) build.
COPY src/ src/
RUN dotnet publish src/MondShield.Api/MondShield.Api.csproj \
    -c Release -p:TargetLinux=true --no-restore -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
# Kestrel listens on 8080 inside the container; compose maps it to the host.
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MondShield.Api.dll"]
