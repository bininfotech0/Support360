FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Support360.slnx ./
COPY src/Support360.Api/Support360.Api.csproj src/Support360.Api/
COPY src/Support360.Application/Support360.Application.csproj src/Support360.Application/
COPY src/Support360.Contracts/Support360.Contracts.csproj src/Support360.Contracts/
COPY src/Support360.Domain/Support360.Domain.csproj src/Support360.Domain/
COPY src/Support360.Infrastructure/Support360.Infrastructure.csproj src/Support360.Infrastructure/
COPY src/Support360.Workers/Support360.Workers.csproj src/Support360.Workers/
RUN dotnet restore src/Support360.Api/Support360.Api.csproj
COPY src/ src/
RUN dotnet publish src/Support360.Api/Support360.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "Support360.Api.dll"]
