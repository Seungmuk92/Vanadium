FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY entrypoint.sh /entrypoint.sh

# Rename appsettings.json to template for runtime substitution
RUN mv /usr/share/nginx/html/appsettings.json \
       /usr/share/nginx/html/appsettings.template.json \
    && sed -i 's/\r//' /entrypoint.sh \
    && chmod +x /entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/entrypoint.sh"]
