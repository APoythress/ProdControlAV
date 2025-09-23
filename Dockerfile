# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /workspace

# Copy solution + project files first (to leverage layer cache)
COPY ProdControlAV.sln ./
COPY src/ProdControlAV.Core/ProdControlAV.Core.csproj                     src/ProdControlAV.Core/
COPY src/ProdControlAV.Infrastructure/ProdControlAV.Infrastructure.csproj src/ProdControlAV.Infrastructure/
COPY src/ProdControlAV.WebApp/ProdControlAV.WebApp.csproj                 src/ProdControlAV.WebApp/
COPY src/ProdControlAV.API/ProdControlAV.API.csproj                       src/ProdControlAV.API/

# Restore the whole solution so all ProjectReferences (incl. WebApp) resolve
RUN dotnet restore ProdControlAV.sln

# Now copy the rest of the source
COPY src/ ./src/

# Publish ONLY the API (the WebApp can be a referenced project; that's OK)
RUN dotnet publish src/ProdControlAV.API/ProdControlAV.API.csproj -c Release -o /app /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80 \
    DOTNET_EnableDiagnostics=0 \
    COMPlus_GCHeapCount=1
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ProdControlAV.API.dll"]
