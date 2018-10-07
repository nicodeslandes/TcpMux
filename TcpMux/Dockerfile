FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY TcpMux.csproj .

#RUN dotnet restore
COPY * ./
RUN ls
WORKDIR /src

ARG VERSION
RUN echo Building version $VERSION
RUN dotnet build -c Release -f netcoreapp2.1 -p:Version=$VERSION -o /app

FROM build AS publish
ARG VERSION
RUN dotnet publish -c Release -f netcoreapp2.1 -p:Version=$VERSION --no-build -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "TcpMux.dll"]