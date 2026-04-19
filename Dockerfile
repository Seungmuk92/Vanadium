FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Vanadium.Note.REST/Vanadium.Note.REST.csproj Vanadium.Note.REST/
RUN dotnet restore Vanadium.Note.REST/Vanadium.Note.REST.csproj
COPY . .
RUN dotnet publish Vanadium.Note.REST -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p uploads
ENTRYPOINT ["dotnet", "Vanadium.Note.REST.dll"]
