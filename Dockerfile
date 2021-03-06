﻿FROM microsoft/dotnet:2.1-sdk-alpine AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY TcpMux/*.csproj ./TcpMux/
WORKDIR /app/TcpMux

# Remove net452 from the target frameworks (causes issues in dotnet restore and dotnet publish, even when explicitly specifying the framework :( )
RUN sed 's/<TargetFrameworks>netcoreapp2.1;net452<\/TargetFrameworks>/<TargetFrameworks>netcoreapp2.1<\/TargetFrameworks>/' TcpMux.csproj > new.csproj && mv new.csproj TcpMux.csproj
RUN dotnet restore

# copy and build app and libraries
WORKDIR /app/
COPY TcpMux/. ./TcpMux/
WORKDIR /app/TcpMux

# Remove net452 from the target frameworks (causes issues in dotnet restore and dotnet publish, even when explicitly specifying the framework :( )
RUN sed 's/<TargetFrameworks>netcoreapp2.1;net452<\/TargetFrameworks>/<TargetFrameworks>netcoreapp2.1<\/TargetFrameworks>/' TcpMux.csproj > new.csproj && mv new.csproj TcpMux.csproj

# add IL Linker package
RUN dotnet add package ILLink.Tasks -v 0.1.5-preview-1841731 -s https://dotnet.myget.org/F/dotnet-core/api/v3/index.json
RUN dotnet publish -c Release -r linux-musl-x64 -f netcoreapp2.1 -o out /p:ShowLinkerSizeComparison=true

FROM microsoft/dotnet:2.1-runtime-deps-alpine AS runtime
WORKDIR /app
COPY --from=build /app/TcpMux/out ./
ENTRYPOINT ["./TcpMux"]
