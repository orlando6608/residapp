# Dominio

Los contratos de este directorio son independientes de la interfaz y de la persistencia:

- `access/`: perfiles funcionales del sistema.
- `residents/`: identidad administrativa y ámbito del residente.
- `baseline/`: vocabulario cerrado por el PRD y ciclo versionado del basal.
- `shared/`: identificadores UUID v4 opacos, con generación y validación runtime compartidas.

El modelo inicial está documentado en `docs/architecture/0001-modelo-minimo-residente-basal.md`; el contrato campo a campo sujeto a aprobación se encuentra en `docs/architecture/0002-contrato-datos-residente-basal.md`. La identidad explicita estado administrativo activo/inactivo y la activación basal valida catálogo, secuencia, ámbito, tiempos e identificadores antes de producir snapshots congelados.

No hay contenido clínico ficticio, catálogo Barthel, material CFS, persistencia ni comando server-side de firma implementados. La política de quién puede firmar un borrador iniciado por otra persona sigue pendiente de producto.
