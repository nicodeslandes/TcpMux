FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY ["TcpMux/TcpMux.csproj", "TcpMux/"]

ENV PROJECT_DIR TcpMux
RUN dotnet restore "$PROJECT_DIR/TcpMux.csproj"
COPY . .
WORKDIR "/src/TcpMux"
RUN dotnet build "TcpMux.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "TcpMux.csproj" -c Release -f netcoreapp2.1 -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "TcpMux.dll"]