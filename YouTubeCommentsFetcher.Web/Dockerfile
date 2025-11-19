FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080


FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["YouTubeCommentsFetcher.Web/YouTubeCommentsFetcher.Web.csproj", "YouTubeCommentsFetcher.Web/"]
RUN dotnet restore "./YouTubeCommentsFetcher.Web/YouTubeCommentsFetcher.Web.csproj"
COPY . .
WORKDIR "/src/YouTubeCommentsFetcher.Web"
RUN dotnet build "./YouTubeCommentsFetcher.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YouTubeCommentsFetcher.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YouTubeCommentsFetcher.Web.dll"]
