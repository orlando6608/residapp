# Fuentes funcionales vigentes

La fuente canónica del prototipo es la línea base funcional `LBF-CONNECT-2026-09-06-V1.1`. Los Markdown versionados gobiernan la implementación; los PDF/DOCX y las versiones Markdown sustituidas se conservan como evidencia histórica.

## Control de la línea base

- Declaración vigente: `product/baselines/2026-09-06-declaracion-linea-base-funcional-v1.1.md`
- Decisión incremental: `product/decisions/2026-09-06-cierre-d1-p01-d1-p08-v1.0.md`
- Línea base histórica: `LBF-CONNECT-2026-09-05-V1`
- Commit histórico: `3850238f81dbc6fb7af52816c0033c8baeb31ac9`
- Tag histórico inmutable: `functional-baseline-2026-09-05-v1`

La línea base v1.1 sustituye a v1 como fuente corriente, pero no modifica su commit ni su tag.

## Jerarquía

1. Decisión explícita posterior aprobada y registrada.
2. PRD v0.5 consolidado.
3. Matriz de permisos de seis perfiles v0.2.1.
4. Wireframe Markdown canónico del perfil afectado.
5. `AGENTS.md` v1.4.
6. Implementación y pruebas como evidencia del estado técnico.

La trazabilidad enlaza requisitos, permisos, pantallas, entidades lógicas y pruebas; no crea reglas.

## Artefactos canónicos de v1.1

| Tipo | Archivo |
| --- | --- |
| Producto | `product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md` |
| Autorización | `product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md` |
| Auxiliar | `product/wireframes/2026-09-05-wireframe-funcional-auxiliar-v0.2.md` |
| Enfermería | `product/wireframes/2026-09-06-wireframe-funcional-enfermeria-v0.3.md` |
| Medicina | `product/wireframes/2026-09-06-wireframe-funcional-medicina-v0.3.md` |
| Portal Familiar | `product/wireframes/2026-09-05-wireframe-funcional-portal-familiar-v0.2.md` |
| Administración | `product/wireframes/2026-09-06-wireframe-funcional-administracion-v0.3.md` |
| Dirección Clínica | `product/wireframes/2026-09-05-wireframe-funcional-direccion-coordinacion-clinica-v0.1.md` |
| Trazabilidad | `product/traceability/2026-09-06-matriz-trazabilidad-funcional-v0.2.md` |

## Material histórico

PRD v0.4, permisos v0.2, wireframes sustituidos y la declaración v1.0 siguen disponibles para auditoría. No pueden prevalecer sobre v1.1 ni utilizarse para recuperar reglas retiradas.

## Decisiones técnicas derivadas

`architecture/0001` y `0002` están alineados con v1.1. D1 y Drizzle están configurados y `0001_resident_baseline_foundation.sql` está integrada en `main`. El ADR 0003 registra su revisión SQL, 29 tablas, 56 índices, 56 triggers y la superación de `DB-T01`–`DB-T16` y pruebas adicionales en D1 local desechable con datos sintéticos. La migración `0001` es inmutable: toda evolución del esquema debe usar `0002` o una posterior. D1 remota, datos reales, piloto y producción siguen prohibidos.

La matriz de trazabilidad v0.2 conserva referencias al diseño físico como pendiente porque es una fotografía funcional congelada; no describe el estado técnico corriente ni debe reescribirse por este cierre documental.

`D1-P04`, `D1-P05` y `D1-P06` permanecen diferidas y denegadas por defecto.
