FROM debian:trixie-slim AS build
WORKDIR /src

RUN apt update \
    && apt install -y --no-install-recommends \
        unzip \
        ca-certificates \
        curl \
    && rm -rf /var/lib/apt/lists/*


COPY release/SS14.Server_linux-x64.zip /tmp/server.zip
RUN unzip /tmp/server.zip -d server/ \
    && rm /tmp/server.zip

RUN chmod +x /src/server/Robust.Server

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

COPY --from=build /src/server/ .

ENTRYPOINT [ "./Robust.Server" ]
