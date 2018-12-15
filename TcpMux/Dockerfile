FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:3.0-sdk AS build
WORKDIR /src
COPY TcpMux/TcpMux.csproj TcpMux/

RUN dotnet restore TcpMux/TcpMux.csproj
COPY . .
WORKDIR /src/TcpMux

ARG VERSION=1.0.0.0
ARG CONFIGURATION=Debug
RUN echo Building version $VERSION && \
	dotnet build TcpMux.csproj -c $CONFIGURATION -f netcoreapp2.1 -p:Version=$VERSION -o /app

FROM build AS publish
ARG VERSION=1.0.0.0
ARG CONFIGURATION=Debug
RUN dotnet publish TcpMux.csproj -c $CONFIGURATION -f netcoreapp2.1 -p:Version=$VERSION -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "TcpMux.dll"]