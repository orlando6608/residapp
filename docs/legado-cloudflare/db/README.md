# Persistencia D1/Drizzle

Primera base física de Residente y Basal conforme a `ADR-0003-D1-DRIZZLE` y a
`LBF-CONNECT-2026-09-06-V1.1`. Solo puede usarse con datos ficticios en una D1 local
desechable.

La migración `0001_resident_baseline_foundation.sql` está integrada en `main`, contiene
29 tablas, 56 índices y 56 triggers, y su SHA-256 final es
`39B05B14AB2E9A31C2396A2D0665A36207D735C4921FD752B97C8C408AFA2EE8`. La revisión SQL,
`DB-T01`–`DB-T16` y las pruebas adicionales se superaron en D1 local con fixtures
sintéticos. `0001` es inmutable: cualquier cambio físico posterior debe realizarse en
`0002` o una migración posterior.

- `schema/`: contrato Drizzle modular.
- `migrations/`: SQL versionado y revisable; Wrangler es el aplicador.
- `repositories/`: operaciones server-side atómicas y con denegación por defecto.

Comandos autorizados:

```text
pnpm db:generate
pnpm db:check
pnpm db:migrate:local
pnpm db:test
```

No existe ni se autoriza script de aplicación remota y no debe usarse `drizzle-kit push`. Los flujos
`D1-P04`, `D1-P05` y `D1-P06` permanecen sin repositorios, endpoints ni grants operativos.
