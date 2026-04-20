#!/bin/sh
# Inject API_BASE_URL environment variable into Blazor WASM appsettings at runtime
envsubst '${API_BASE_URL}' < /usr/share/nginx/html/appsettings.template.json \
  > /usr/share/nginx/html/appsettings.json
exec nginx -g 'daemon off;'
