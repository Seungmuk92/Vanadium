FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Pre-create the uploads directory owned by the non-root app user (APP_UID is
# provided by the base image) so a named volume mounted here inherits its
# ownership and the app can write uploads without root.
RUN mkdir -p /app/uploads && chown -R $APP_UID:$APP_UID /app/uploads

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Vanadium.Note.REST.dll"]
