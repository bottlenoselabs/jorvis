FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Directory.Build.props src/Directory.Packages.props src/
COPY src/Jorvis.Core/Jorvis.Core.csproj src/Jorvis.Core/
COPY src/Jorvis.Infrastructure/Jorvis.Infrastructure.csproj src/Jorvis.Infrastructure/
COPY src/Jorvis.Api/Jorvis.Api.csproj src/Jorvis.Api/
RUN dotnet restore src/Jorvis.Api/Jorvis.Api.csproj

COPY src/Jorvis.Core/ src/Jorvis.Core/
COPY src/Jorvis.Infrastructure/ src/Jorvis.Infrastructure/
COPY src/Jorvis.Api/ src/Jorvis.Api/
RUN dotnet publish src/Jorvis.Api/Jorvis.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Jorvis.Api.dll"]
