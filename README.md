# TcpMux

TCP Mux is a TCP router that supports optional SSL re-encryption and offloading

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