# Build stage: restore, compile, and publish the Web API.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . ./
RUN dotnet restore
RUN dotnet build
RUN dotnet publish -c Release -o /app/publish

# Runtime stage: smaller ASP.NET image, no SDK.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Listen on 8080 so Docker/Render port mapping matches EXPOSE.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReactionVideoAggregator.dll"]
