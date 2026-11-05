# The API ships to Render (and anywhere Docker runs) from here:
# two-stage build — the .NET 10 SDK compiles, the slim ASP.NET runtime
# image carries the result. Build context is this folder (Api + Data).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Layer the restore: only the project files first, so package restores
# cache between code changes. nuget.config is copied too — restore looks
# for feeds from the config chain nearest the project.
COPY nuget.config ./
COPY Api/Api.csproj Api/
COPY Data/Data.csproj Data/
RUN dotnet restore Api/Api.csproj

# The sources, then the publish (Release, ready-to-run output to /publish).
COPY Api/ Api/
COPY Data/ Data/
RUN dotnet publish Api/Api.csproj -c Release -o /publish --no-restore

# The final image — runtime only, nothing that compiles or seeds.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish .
# Kestrel inside the container: HTTP only, the default ASPNETCORE_HTTP_PORTS
# (8080) is fine — set that var in the host's dashboard if it needs another
# port (Render's Docker default is 10000 → set ASPNETCORE_HTTP_PORTS=10000).
ENTRYPOINT ["dotnet", "Api.dll"]
