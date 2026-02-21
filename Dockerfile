# syntax=docker/dockerfile:1

FROM debian:trixie-slim AS build
WORKDIR /src


RUN apt-get update && apt-get install -y --no-install-recommends \
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

ARG VERSION
ARG BUILD_DATE
ARG VCS_REF

LABEL org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.title="Exodus Monolith Server" \
      org.opencontainers.image.description="SS14 Exodus Monolith Server"

RUN groupadd -r ss14 && useradd -r -g ss14 -d /app ss14

COPY --from=build /src/server/ .

RUN chown -R ss14:ss14 /app
USER ss14

ENTRYPOINT [ "./Robust.Server" ]
