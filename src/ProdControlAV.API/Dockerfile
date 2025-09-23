FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ProdControlAV.API/ProdControlAV.API.csproj", "src/ProdControlAV.API/"]
COPY ["src/ProdControlAV.Infrastructure/ProdControlAV.Infrastructure.csproj", "src/ProdControlAV.Infrastructure/"]
COPY ["src/ProdControlAV.Core/ProdControlAV.Core.csproj", "src/ProdControlAV.Core/"]
COPY ["src/ProdControlAV.WebApp/ProdControlAV.WebApp.csproj", "src/ProdControlAV.WebApp/"]
RUN dotnet restore "src/ProdControlAV.API/ProdControlAV.API.csproj"
COPY . .
WORKDIR "/src/src/ProdControlAV.API"
RUN dotnet build "./ProdControlAV.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ProdControlAV.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ProdControlAV.API.dll"]

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# Container Apps ingress is set to 80 on your app — bind Kestrel to 80.
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
# (Optional hardening)
ENV DOTNET_EnableDiagnostics=0 \
    COMPlus_GCHeapCount=1
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProdControlAV.API.dll"]