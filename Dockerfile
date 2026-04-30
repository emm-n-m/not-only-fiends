FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore
COPY NotOnlyFiendsStudio/NotOnlyFiendsStudio.csproj NotOnlyFiendsStudio/
COPY NotOnlyFiendsFeed/NotOnlyFiendsFeed.csproj NotOnlyFiendsFeed/
RUN dotnet restore NotOnlyFiendsFeed/NotOnlyFiendsFeed.csproj

# Copy everything and publish
COPY NotOnlyFiendsStudio/ NotOnlyFiendsStudio/
COPY NotOnlyFiendsFeed/ NotOnlyFiendsFeed/
RUN dotnet publish NotOnlyFiendsFeed/NotOnlyFiendsFeed.csproj -c Release -o /app

# Copy bundled content packs
COPY NotOnlyFiendsStudio/Content/packs/ /app/content/packs/
COPY content-public.json /app/content/

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Configure content paths for Docker mode
ENV Content__BundledPacksPath=/app/content/packs
ENV Content__ExtraPacksPath=/data/extra-packs
ENV Content__CharactersPath=/data/characters
ENV ASPNETCORE_URLS=http://0.0.0.0:5000

EXPOSE 5000

ENTRYPOINT ["dotnet", "NotOnlyFiendsFeed.dll"]
