# Multi-stage build for the extracted InventoryService. Build context = repo root.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/LagerMeister.Inventory/LagerMeister.Inventory.csproj src/LagerMeister.Inventory/
RUN dotnet restore src/LagerMeister.Inventory/LagerMeister.Inventory.csproj
COPY src/LagerMeister.Inventory/ src/LagerMeister.Inventory/
RUN dotnet publish src/LagerMeister.Inventory/LagerMeister.Inventory.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LagerMeister.Inventory.dll"]
