FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer, keyed only on the project file, so a source-only
# change reuses the cached NuGet restore instead of re-downloading the whole
# package graph on every publish.
COPY Vanadium.Note.REST.csproj .
RUN dotnet restore Vanadium.Note.REST.csproj

# .dockerignore keeps bin/, obj/, uploads/ and appsettings.Development.json out
# of this copy — the last one matters because the Web SDK publishes **/*.json,
# so a stray dev config would ship its DB password and JWT secret in the image.
COPY . .
RUN dotnet publish Vanadium.Note.REST.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

LABEL org.opencontainers.image.title="vanadium-rest" \
      org.opencontainers.image.description="Vanadium Note REST API (ASP.NET Core)"

# Pre-create the uploads directory owned by the non-root app user (APP_UID is
# provided by the base image) so a named volume mounted here inherits its
# ownership and the app can write uploads without root.
RUN mkdir -p /app/uploads && chown -R $APP_UID:$APP_UID /app/uploads

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Vanadium.Note.REST.dll"]
