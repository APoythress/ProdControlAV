# build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# copy solution + src (paths must match your repo)
COPY ProdControlAV.sln ./
COPY src/ ./src/

# restore
RUN dotnet restore ./src/ProdControlAV.Agent/ProdControlAV.Agent.csproj

# publish to /out (glibc, arm64, self-contained; no trim/single-file for now)
RUN dotnet publish ./src/ProdControlAV.Agent/ProdControlAV.Agent.csproj \
    -c $BUILD_CONFIGURATION \
    -r linux-arm64 \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -o /out

# sanity: fail the build if output missing
RUN test -f /out/ProdControlAV.Agent || (echo "Publish missing /out/ProdControlAV.Agent"; ls -R /out; exit 1)
