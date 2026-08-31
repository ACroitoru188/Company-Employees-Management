FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
RUN mkdir -p /app/App_Data /app/dp-keys && chown -R $APP_UID /app
USER $APP_UID
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Frontend/CompanyEmployees.Web/CompanyEmployees.Web.csproj", "src/Frontend/CompanyEmployees.Web/"]
COPY ["src/Backend/CompanyEmployees.Domain/CompanyEmployees.Domain.csproj", "src/Backend/CompanyEmployees.Domain/"]
COPY ["src/Backend/CompanyEmployees.Persistence/CompanyEmployees.Persistence.csproj", "src/Backend/CompanyEmployees.Persistence/"]
COPY ["src/Backend/CompanyEmployees.Persistence.Contracts/CompanyEmployees.Persistence.Contracts.csproj", "src/Backend/CompanyEmployees.Persistence.Contracts/"]
COPY ["src/Backend/CompanyEmployees.Persistence.Providers.SqlServer/CompanyEmployees.Persistence.Providers.SqlServer.csproj", "src/Backend/CompanyEmployees.Persistence.Providers.SqlServer/"]
COPY ["src/Backend/CompanyEmployees.Persistence.Providers.PostgreSql/CompanyEmployees.Persistence.Providers.PostgreSql.csproj", "src/Backend/CompanyEmployees.Persistence.Providers.PostgreSql/"]
COPY ["src/Backend/CompanyEmployees.Gateway/CompanyEmployees.Gateway.csproj", "src/Backend/CompanyEmployees.Gateway/"]
COPY ["src/Backend/CompanyEmployees.Application/CompanyEmployees.Application.csproj", "src/Backend/CompanyEmployees.Application/"]
COPY ["src/Backend/CompanyEmployees.Infrastructure/CompanyEmployees.Infrastructure.csproj", "src/Backend/CompanyEmployees.Infrastructure/"]
RUN dotnet restore "src/Frontend/CompanyEmployees.Web/CompanyEmployees.Web.csproj"
RUN dotnet restore "src/Backend/CompanyEmployees.Persistence.Providers.SqlServer/CompanyEmployees.Persistence.Providers.SqlServer.csproj"
RUN dotnet restore "src/Backend/CompanyEmployees.Persistence.Providers.PostgreSql/CompanyEmployees.Persistence.Providers.PostgreSql.csproj"
COPY . .
WORKDIR "/src/src/Frontend/CompanyEmployees.Web"
RUN dotnet build "./CompanyEmployees.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./CompanyEmployees.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --chown=$APP_UID --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CompanyEmployees.Web.dll"]
