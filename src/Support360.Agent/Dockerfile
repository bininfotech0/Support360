FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Support360.Agent/Support360.Agent.csproj src/Support360.Agent/
RUN dotnet restore src/Support360.Agent/Support360.Agent.csproj
COPY src/ src/
RUN dotnet publish src/Support360.Agent/Support360.Agent.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
# UID/GID 100:101 preserve compatibility with existing identity/queue volumes
# that may already be mounted into the container.
RUN groupadd -r -g 101 support360 && useradd -r -u 100 -g support360 -d /var/lib/support360 support360 \
    && mkdir -p /etc/support360 /var/lib/support360 /var/cache/support360 \
    && chown -R support360:support360 /etc/support360 /var/lib/support360 /var/cache/support360
COPY --from=build /out .
USER support360
ENTRYPOINT ["/app/support360-agent"]
