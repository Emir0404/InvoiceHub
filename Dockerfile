# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so the restore layer is cached until dependencies change.
COPY InvoiceHub.sln ./
COPY src/InvoiceHub.Web/InvoiceHub.Web.csproj src/InvoiceHub.Web/
COPY tests/InvoiceHub.Tests/InvoiceHub.Tests.csproj tests/InvoiceHub.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/InvoiceHub.Web -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# QuestPDF draws PDFs with Skia, which needs fontconfig on Linux.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The database and the cookie-encryption keys live in a volume, so they survive container restarts.
RUN mkdir -p /data && chown -R app /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__DefaultConnection="DataSource=/data/invoicehub.db" \
    DataProtection__KeysPath=/data/keys \
    HttpsRedirection=false
EXPOSE 8080

# Never run a web server as root.
USER app
ENTRYPOINT ["dotnet", "InvoiceHub.Web.dll"]
