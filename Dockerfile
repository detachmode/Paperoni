FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY *.props .
COPY *.slnx .
COPY src/ ./src/

RUN dotnet restore src/Paperoni/Paperoni.csproj

RUN case "$TARGETARCH" in \
        arm64) RID=linux-arm64 ;; \
        *)     RID=linux-x64 ;; \
    esac && \
    dotnet publish src/Paperoni/Paperoni.csproj \
        -c Release \
        -r "$RID" \
        -o /app \
        --self-contained false \
        -p:PublishSingleFile=true

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["./Paperoni"]
