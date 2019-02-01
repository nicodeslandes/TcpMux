# TcpMux

TCP Mux is a TCP router that supports optional SSL re-encryption and offloading

[![Build Status](https://dev.azure.com/nicodeslandes/TcpMux/_apis/build/status/nicodeslandes.TcpMux)](https://dev.azure.com/nicodeslandes/TcpMux/_build/latest?definitionId=1)

## Installation

`TcpMux` can be installed as a [.NET Core global tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). To set it up, install the latest [.NET Core SDK](https://dotnet.microsoft.com/download), and run:
```
dotnet tool install -g tcpmux
```
This will allow you to launch `TcpMux` directly on the command line by entering `tcpmux`.

## Usage

```
 tcpmux [options] <listen_port> <target_host> <target_port>
```

## Options
```
     -v: Verbose mode; display traffic
     -hex: Hex mode; display traffic as hex dump
     -text: Text mode; display traffic as text dump
     -ssl: perform ssl decoding and reencoding
     -sslOff: perform ssl off-loading (ie connect to the target via SSL, and expose a decrypted port)
     -sslCn: CN to use in the generated SSL certificate (defaults to <target_host>)
     -regCA: register self-signed certificate CA
```
