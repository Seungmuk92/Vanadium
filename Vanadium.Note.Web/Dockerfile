FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer, keyed only on the project file (see the REST
# Dockerfile for the rationale).
COPY Vanadium.Note.Web.csproj .
RUN dotnet restore Vanadium.Note.Web.csproj

# No Node step here on purpose: the self-hosted editor bundle under
# wwwroot/js/vendor/ is committed to the repo, so the image build never needs
# npm. Regenerate it with `npm ci && npm run build` and commit the result
# whenever the editor dependencies in package.json change.
COPY . .
RUN dotnet publish Vanadium.Note.Web.csproj -c Release -o /app/publish --no-restore

# The published index.html carries one inline script: the <script type="importmap">
# the SDK fills with the framework fingerprint map. The site CSP has no
# 'unsafe-inline', so its sha256 has to be allowlisted explicitly or the app
# never boots. Compute it from the actual publish output and substitute it into
# nginx.conf (the tool fails the build if the map or the placeholder is missing).
# Run from /tmp: with a .csproj in the working directory `dotnet run <file>.cs`
# builds the project instead of the file-based app.
RUN cp build/csp-importmap-hash.cs /tmp/csp-importmap-hash.cs \
    && cd /tmp \
    && dotnet run /tmp/csp-importmap-hash.cs -- \
         /app/publish/wwwroot/index.html \
         /src/nginx.conf \
         /app/nginx.conf

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY --from=build /app/nginx.conf /etc/nginx/conf.d/default.conf
COPY entrypoint.sh /entrypoint.sh

LABEL org.opencontainers.image.title="vanadium-web" \
      org.opencontainers.image.description="Vanadium Note web front-end (Blazor WASM on nginx)"

# Rename appsettings.json to template for runtime substitution
RUN mv /usr/share/nginx/html/appsettings.json \
       /usr/share/nginx/html/appsettings.template.json \
    && sed -i 's/\r//' /entrypoint.sh \
    && chmod +x /entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/entrypoint.sh"]
