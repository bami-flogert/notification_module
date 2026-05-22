# Pin OS variant (not bare 8.0) — bare tags intermittently 403 on GitHub Actions MCR pulls.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
WORKDIR /src
COPY . .
RUN dotnet restore "src/NotificationModule.Consumer/NotificationModule.Consumer.csproj"
RUN dotnet publish "src/NotificationModule.Consumer/NotificationModule.Consumer.csproj" \
    -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotificationModule.Consumer.dll"]

