# Declaración de línea base funcional incremental

## Plataforma asistencial y de comunicación con familias — Connect

**Identificador:** `LBF-CONNECT-2026-09-06-V1.1`  
**Versión de la declaración:** 1.1  
**Fecha efectiva:** 6 de septiembre de 2026  
**Estado:** APROBADA COMO LÍNEA BASE FUNCIONAL VIGENTE  
**Sustituye como referencia corriente:** `LBF-CONNECT-2026-09-05-V1`  
**Fuente canónica:** Markdown versionado en GitHub  
**Ámbito:** prototipo con datos exclusivamente ficticios y preparación de persistencia local  

## 1. Declaración formal

Por decisión expresa del responsable del proyecto, se aprueba la línea base incremental `LBF-CONNECT-2026-09-06-V1.1`.

Esta versión incorpora únicamente el cierre funcional de `D1-P01` y `D1-P08`, las versiones documentales necesarias para reflejarlo y la autorización condicionada de validación local de la migración `0001`. Mantiene los seis perfiles, los 140 requisitos funcionales y la estructura general de la línea base v1.

Desde su fecha efectiva, v1.1 es la referencia obligatoria para diseño, implementación y prueba del prototipo.

## 2. Preservación de la línea base v1

`LBF-CONNECT-2026-09-05-V1` no se modifica ni se reetiqueta. Permanecen como evidencia histórica:

- commit `3850238f81dbc6fb7af52816c0033c8baeb31ac9`;
- tag `functional-baseline-2026-09-05-v1`;
- declaración v1.0 y sus nueve artefactos;
- correcciones editoriales posteriores visibles en Git.

V1.1 sustituye a v1 como referencia corriente, no como registro histórico.

## 3. Cambio funcional aprobado

La decisión `DEC-CONNECT-2026-09-06-D1-P01-P08` aprueba:

1. Sexo documentado con códigos `male`, `female`, `other` y `unknown`, etiquetas localizadas, fuente administrativa, sin inferencia ni texto libre.
2. Ayuda técnica con `NINGUNA` para ausencia conocida, `NO_DOCUMENTADO` para desconocimiento y rechazo de `NO_APLICA`.
3. `NO_APLICA` en textura y líquidos solo con vía exclusivamente `ENTERAL`; la vía `MIXTA` exige dato oral o `NO_DOCUMENTADO`.
4. Descripción breve no vacía para toda opción estructurada `OTRO/OTRA`; sin dicha opción, el texto debe permanecer vacío.

No cambia ningún permiso. `D1-P04`, `D1-P05` y `D1-P06` continúan diferidas y denegadas por defecto.

## 4. Manifiesto de artefactos

Las huellas se calculan sobre los bytes UTF-8 exactos de cada archivo Markdown.

| Artefacto | Ruta | Versión | SHA-256 |
| --- | --- | --- | --- |
| PRD | `docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md` | 0.5 | `3575bfcef1cb9e2fea40e892bcd2218c1fe8b226ba3d9e8f35702afd73ab214d` |
| Permisos | `docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md` | 0.2.1 | `36f87c4498fa98386a6f4e60e42be0c70d632db4270207c52e4719279422b5f3` |
| Wireframe Auxiliar | `docs/product/wireframes/2026-09-05-wireframe-funcional-auxiliar-v0.2.md` | 0.2 | `384ac7587abb5fd280f7321f5fe826bf28d10a39812b171904c38dd5127b880e` |
| Wireframe Enfermería | `docs/product/wireframes/2026-09-06-wireframe-funcional-enfermeria-v0.3.md` | 0.3 | `872b7bea05c92083620f0abc08a3c185be3bb9a29902e42d6c147181d788e708` |
| Wireframe Medicina | `docs/product/wireframes/2026-09-06-wireframe-funcional-medicina-v0.3.md` | 0.3 | `83034d6156007ecbbd5b6c010c19e1002799a53f460b914986202b7ed76589ec` |
| Wireframe Portal Familiar | `docs/product/wireframes/2026-09-05-wireframe-funcional-portal-familiar-v0.2.md` | 0.2 | `2314bbcc202889731f9735c730c41807f41db5ae6efba4ad80977a42b16b235a` |
| Wireframe Administración | `docs/product/wireframes/2026-09-06-wireframe-funcional-administracion-v0.3.md` | 0.3 | `4a6413804420bd98f225fa0245365e24a427fffc33b4b71a3aecac9a793da19e` |
| Wireframe Dirección Clínica | `docs/product/wireframes/2026-09-05-wireframe-funcional-direccion-coordinacion-clinica-v0.1.md` | 0.1 | `f0d450846bfc304970910318a370b1b78172a92de68340fae910a7ca3b924c07` |
| Trazabilidad | `docs/product/traceability/2026-09-06-matriz-trazabilidad-funcional-v0.2.md` | 0.2 | `b88672ad5a6a3c50ec9459ecbbe1b2e4f902d5aab9305b8caba0c6a0448c376e` |

Los wireframes de Auxiliar, Portal Familiar y Dirección Clínica se reutilizan sin modificación. Sus huellas coinciden con v1. Los restantes artefactos se versionan para hacer visible el cambio.

## 5. Jerarquía

1. Decisiones posteriores expresamente aprobadas y registradas.
2. PRD v0.5.
3. Matriz de permisos v0.2.1.
4. Wireframe canónico aplicable.
5. `AGENTS.md` v1.4.
6. Implementación y pruebas como evidencia del estado técnico.

La trazabilidad v0.2 enlaza las capas anteriores, pero no crea requisitos.

## 6. Efecto sobre D1/Drizzle y 0001

D1 y Drizzle permanecen aprobados para el prototipo ficticio.

Se autoriza:

- preparar el esquema y la migración `0001`;
- aplicarla únicamente sobre una D1 local desechable;
- utilizar exclusivamente datos sintéticos;
- ejecutar `DB-T01`–`DB-T16` y `pnpm check`.

`0001` no se considera aceptada hasta revisar el SQL y superar todas las pruebas aplicables. Continúan prohibidos la aplicación remota, los datos reales, el piloto real y producción.

## 7. Exclusiones

Esta línea base no aprueba:

- traslado, reactivación, cancelación excepcional o inicio de rectificación;
- topología productiva definitiva;
- autenticación productiva;
- DPO/EIPD, retención, RPO/RTO o medidas necesarias para datos reales;
- ninguna decisión clínica automática.

## 8. Control de cambios

Cualquier cambio posterior de significado, catálogo, permiso, flujo, pantalla, entidad lógica o criterio de aceptación requiere:

1. decisión registrada;
2. actualización de los documentos afectados;
3. trazabilidad actualizada;
4. nueva declaración incremental;
5. commit posterior visible, sin reescribir tags históricos.

Las correcciones puramente ortográficas o de enlaces pueden realizarse en commits posteriores si no alteran significado.

## 9. Criterio de verificación

La línea base es reproducible cuando:

- existen los nueve archivos del manifiesto;
- sus SHA-256 coinciden;
- README, índice documental y AGENTS declaran v1.1;
- el ADR 0003 autoriza solo validación local condicionada de `0001`;
- v1 y su tag siguen disponibles e intactos.

---

**Fin de `LBF-CONNECT-2026-09-06-V1.1`.**
