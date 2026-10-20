FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools

COPY src/Directory.Build.props src/Directory.Packages.props src/
COPY src/Jorvis.Core/Jorvis.Core.csproj src/Jorvis.Core/
COPY src/Jorvis.Web/Jorvis.Web.csproj src/Jorvis.Web/
RUN dotnet restore src/Jorvis.Web/Jorvis.Web.csproj

COPY src/Jorvis.Core/ src/Jorvis.Core/
COPY src/Jorvis.Web/ src/Jorvis.Web/
RUN dotnet publish src/Jorvis.Web/Jorvis.Web.csproj -c Release -o /app --no-restore

FROM nginx:alpine AS runtime
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY src/Jorvis.Web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
