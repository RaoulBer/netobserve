# Multi-stage build for the LagerMeister monolith.
# Build context is the REPO ROOT (see docker-compose.yml) so paths are src/-relative.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/LagerMeister.Monolith/LagerMeister.Monolith.csproj src/LagerMeister.Monolith/
RUN dotnet restore src/LagerMeister.Monolith/LagerMeister.Monolith.csproj
COPY src/LagerMeister.Monolith/ src/LagerMeister.Monolith/
RUN dotnet publish src/LagerMeister.Monolith/LagerMeister.Monolith.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LagerMeister.Monolith.dll"]
