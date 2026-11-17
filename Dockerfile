# Multi-stage build for the Order Router API (OR-007, NFR-07).

# ---- Build stage: full SDK, never shipped ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src

# Copy only the build inputs and project files first, so the restore layer is cached until a package changes.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OrderRouter.Domain/OrderRouter.Domain.csproj src/OrderRouter.Domain/
COPY src/OrderRouter.Infrastructure/OrderRouter.Infrastructure.csproj src/OrderRouter.Infrastructure/
COPY src/OrderRouter.Api/OrderRouter.Api.csproj src/OrderRouter.Api/
RUN dotnet restore src/OrderRouter.Api/OrderRouter.Api.csproj

# Source only. Docs/ and the seed CSVs are not copied: the API never reads them (FR-17, SC-22).
COPY src/ src/
# UseAppHost=false: the image starts the API with `dotnet OrderRouter.Api.dll`, so the native host is not needed.
RUN dotnet publish src/OrderRouter.Api/OrderRouter.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- Final stage: ASP.NET Core runtime only (the plain runtime image cannot host ASP.NET) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS final
WORKDIR /app
COPY --from=build /app .

# Run as the image's built-in non-root user (NFR-07).
USER $APP_UID

# The .NET 10 images listen on 8080 by default; non-root cannot bind port 80. TLS is terminated by ingress.
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderRouter.Api.dll"]
