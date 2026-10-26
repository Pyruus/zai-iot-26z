FROM node:22-alpine AS frontend
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/src/Zai.Api/Zai.Api.csproj src/Zai.Api/
RUN dotnet restore src/Zai.Api/Zai.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/Zai.Api/Zai.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
COPY --from=backend /app ./
COPY --from=frontend /src/dist/frontend/browser ./wwwroot
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zai.Api.dll"]
