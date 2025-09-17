# (multi-stage, references project by repo-relative path to avoid path duplication)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /workspace

# Copy everything (context must be repo root when building)
COPY . .

# Restore using the API csproj path relative to the workspace
RUN dotnet restore "src/ProdControlAV.API/ProdControlAV.API.csproj"

# Build the API project (outputs to /app/build)
RUN dotnet build "src/ProdControlAV.API/ProdControlAV.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "src/ProdControlAV.API/ProdControlAV.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
EXPOSE 5000 5001

ENTRYPOINT ["dotnet", "ProdControlAV.API.dll"]