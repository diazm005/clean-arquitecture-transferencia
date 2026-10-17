# Indice de contratos de cache compartido

## Objetivo

Este documento existe para mantener compatibilidad con referencias antiguas a `MEMCACHEDBTABLE.md`.

El estandar de almacenamiento se divide ahora en dos documentos:

- [from-backend.md](/Users/vcaxi/Desktop/vcaxi/mio/system-api-3.0/from-backend.md): contrato para cache generado y administrado por el backend.
- [from-channel.md](/Users/vcaxi/Desktop/vcaxi/mio/system-api-3.0/from-channel.md): contrato para cache originado por canales, mensajes externos e idempotencia.

## Regla de uso

- Si el backend construye la informacion y define la clave, usar `from-backend.md`.
- Si la informacion llega desde un canal con `messageId`, usar `from-channel.md`.
- Si el equipo esta implementando el consumo desde casos de uso, partir de [MEMCACHE.md](/Users/vcaxi/Desktop/vcaxi/mio/system-api-3.0/MEMCACHE.md).
