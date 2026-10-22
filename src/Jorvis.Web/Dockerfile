FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools

COPY src/Directory.Build.props src/Directory.Packages.props src/
COPY src/Jorvis.Core/Jorvis.Core.csproj src/Jorvis.Core/
COPY src/Jorvis.Web.Theming/Jorvis.Web.Theming.csproj src/Jorvis.Web.Theming/
COPY src/Jorvis.Web/Jorvis.Web.csproj src/Jorvis.Web/
COPY src/Jorvis.Web.Auth/Jorvis.Web.Auth.csproj src/Jorvis.Web.Auth/
RUN dotnet restore src/Jorvis.Web/Jorvis.Web.csproj && dotnet restore src/Jorvis.Web.Auth/Jorvis.Web.Auth.csproj

COPY src/Jorvis.Core/ src/Jorvis.Core/
COPY src/Jorvis.Web.Theming/ src/Jorvis.Web.Theming/
COPY src/Jorvis.Web/ src/Jorvis.Web/
COPY src/Jorvis.Web.Auth/ src/Jorvis.Web.Auth/
RUN dotnet publish src/Jorvis.Web/Jorvis.Web.csproj -c Release -o /app --no-restore
RUN dotnet publish src/Jorvis.Web.Auth/Jorvis.Web.Auth.csproj -c Release -o /auth --no-restore
RUN python3 src/Jorvis.Web/nginx/make-app-csp.py /app/wwwroot/index.html /app/app-csp.conf
RUN python3 src/Jorvis.Web/nginx/make-app-csp.py /auth/wwwroot/index.html /auth/auth-csp.conf

FROM nginx:alpine AS runtime
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY --from=build /auth/wwwroot /usr/share/nginx/html/auth
COPY src/Jorvis.Web/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY src/Jorvis.Web/nginx/security-headers.conf src/Jorvis.Web/nginx/proxy-headers.conf /etc/nginx/snippets/
COPY --from=build /app/app-csp.conf /etc/nginx/snippets/app-csp.conf
COPY --from=build /auth/auth-csp.conf /etc/nginx/snippets/auth-csp.conf
EXPOSE 80
