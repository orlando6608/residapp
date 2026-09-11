# Spike técnico de autenticación para ADR 0005

- **Fecha:** 10 de septiembre de 2026.
- **Estado:** concluido, limpiado y conservado como evidencia documental; se usaron datos y credenciales exclusivamente sintéticos.
- **Rama:** `spike/authentication-adr-0005`.
- **Base:** `origin/main` en `15746e54631d24a0f7dc4ae09b0a03a58ea5ef4e`.
- **ADR evaluado:** [ADR 0005 — Selección del proveedor de autenticación productiva](../architecture/0005-seleccion-proveedor-autenticacion.md), que permanece **Propuesto**.
- **Alcance de la recomendación:** candidato preferente pendiente de validación; no selección definitiva, contratación, integración, uso de datos reales ni autorización de producción.
- **Estado final de la rama:** solo este informe difiere de `main`; el arnés, la ruta, las pruebas específicas y los SDK temporales fueron retirados.

## 1. Conclusión: candidato preferente pendiente de validación

Se identifica **Auth0 Customer Identity con tenant primario en región UE** como **candidato preferente pendiente de validación**, con Universal Login y una eventual sesión stateful de Connect revocable en servidor. Los ámbitos y permisos permanecerían exclusivamente en D1 mediante la frontera ya implementada. Esto no selecciona definitivamente el proveedor ni acepta ADR 0005: antes requiere una prueba con tenant sandbox sintético, oferta vinculante, DPA/subencargados, evaluación de transferencias y aprobación de Seguridad y DPO.

La evidencia nueva que inclina el empate documental del ADR es:

1. `@auth0/nextjs-auth0` 4.29.0 declara peers compatibles con Next.js 16 y React 19, compila con las versiones exactas de Connect y carga e instancia su entrada servidor dentro del bundle vinext ejecutado por Workers.
2. El SDK instalado expone `SessionDataStore`, incluidas lectura, alta/actualización, borrado y borrado por logout token opcional. Esto permite diseñar una sesión stateful de Connect con revocación server-side sin introducir roles o ámbitos en Auth0. La implementación D1 de ese store no se ha creado en este spike.
3. Auth0 ofrece el mejor encaje relativo observado para situar la identidad primaria en la UE y descargar en un proveedor maduro la custodia de contraseñas, MFA, recuperación y protección antiabuso. La residencia no es integral: soporte, correo, logs, backups y subencargados requieren revisión contractual antes de confirmar la preferencia.
4. Better Auth demuestra el mejor encaje puramente técnico: el spike completó en D1 local generación de esquema aislado, alta por email, entrada por usuario y contraseña, cookie segura, lectura de sesión y revocación. Sin embargo, trasladaría a Connect la operación de credenciales, correo, factores, antiabuso, auditoría, recuperación, backups y respuesta a incidentes. No existe evidencia de equipo operativo suficiente para asumir ese riesgo.

### Por qué los demás candidatos no quedan como preferentes

- **Clerk:** el paquete carga en Workers y compila, pero vinext sigue declarando compatibilidad parcial para `auth()` en Server Components. Su cookie `__session` estándar es legible por JavaScript, aunque dure aproximadamente 60 segundos; no satisface el diseño HttpOnly exclusivo de Connect sin añadir otra sesión propia. Su infraestructura primaria estadounidense tampoco mejora el encaje de privacidad frente a Auth0 UE.
- **WorkOS AuthKit:** su SDK Next y su cliente Workers cargan e instancian en el runtime real, y su ciclo de vida gestionado es sólido. Se mantiene como respaldo técnico si Auth0 falla por compatibilidad o precio. No se recomienda primero porque el servicio cloud estándar examinado procesa identidad en EE. UU.; además, MFA para accesos SSO y recuperación/passkeys exigen comprobaciones específicas.
- **Better Auth:** no se descarta por incompatibilidad: es la alternativa mejor probada en local. Se descarta como primera opción productiva por carga operativa y radio de impacto propios, no por coste de licencia. Podría reabrirse si Auth0 no supera contrato/precio o si una exigencia de residencia impide los servicios gestionados, siempre con operación y presupuesto explícitos.

## 2. Precondiciones y conservación del repositorio

Antes de editar se ejecutó `git fetch origin`. El árbol estaba limpio, la rama activa era `main` y `git rev-parse origin/main` devolvió exactamente:

```text
15746e54631d24a0f7dc4ae09b0a03a58ea5ef4e
```

La rama se creó desde esa referencia exacta. Se revisaron `AGENTS.md`, README, índice documental, línea base `LBF-CONNECT-2026-09-06-V1.1`, PRD v0.5, matriz de permisos v0.2.1, wireframes de Administración y Portal Familiar y los ADR 0003, 0004 y 0005.

No se ha modificado ningún documento de la línea base, tag histórico, esquema, migración ni binding D1. `0001` sigue inmutable y no se ha creado `0002`. Todas las bases utilizadas fueron locales, desechables y sintéticas.

## 3. Alcance técnico e impacto

| Área | Impacto del spike |
| --- | --- |
| Interfaz | Ninguna pantalla ni ruta en el estado final. La ruta técnica local usada para medir el runtime fue eliminada tras recoger la evidencia. |
| Dominio funcional | Ningún cambio. No se añaden estados, permisos ni flujos de producto. |
| Autenticación | Las sondas y el prototipo en memoria se usaron de forma transitoria y fueron eliminados; no queda autenticación nueva. |
| Autorización | Se reutiliza sin cambios `SessionIdentityProvider` y el servicio existente. Los claims/cookies inyectados no confieren autoridad. |
| Persistencia | Sin cambios en `db/`, `0001` o `wrangler.jsonc`. Better Auth usa una D1 local separada creada por Miniflare durante una prueba. |
| Arquitectura | La evidencia favorece IdP gestionado → sesión Connect revocable → `externalSubject` → autorización D1. No se añade backend independiente. |
| Dependencias | Cinco paquetes se fijaron temporalmente como `devDependencies` para medirlos y después se retiraron de manifiesto y lockfile. |
| Datos y secretos | Solo sujetos, correos, nombres, contraseñas y claves marcados como sintéticos. Sin cuentas, tenants, correos enviados ni secretos reales. |

### 3.1. Propuesta arquitectónica no implementada

La evidencia respalda como hipótesis para la siguiente fase, no como código aprobado, la cadena:

```text
IdP gestionado → sesión stateful revocable de Connect → SessionIdentityProvider
→ resolución de cuenta y perfil activo en D1 → autorización RBAC/ABAC server-side
```

`SessionIdentityProvider` ya es el puerto neutral existente y debe seguir entregando únicamente un sujeto externo verificado. No debe transportar roles, permisos, centro, unidad, residente ni perfil activo. D1 seguiría resolviendo esos atributos en cada petición, aplicando aislamiento multitenant y denegación por defecto. La persistencia, rotación y revocación de la sesión Connect requieren un diseño posterior explícito; este spike no deja ninguna implementación de ese diseño.

## 4. Versiones evaluadas

Versiones publicadas instaladas transitoriamente el 10 de septiembre de 2026 y consultadas con `pnpm view`. Se registraron durante las pruebas para hacerlas reproducibles, pero ya no permanecen en `package.json` ni en `pnpm-lock.yaml`:

| Candidato | Paquete | Versión | Compatibilidad declarada relevante |
| --- | --- | ---: | --- |
| Auth0 | `@auth0/nextjs-auth0` | 4.29.0 | Next `^16.0.10`; React/React DOM `^19.2.1`; Node 20+ en documentación. |
| Clerk | `@clerk/nextjs` | 7.9.2 | Next `^16.0.10`; React/React DOM `~19.2.3`; Node `>=20.9.0`. |
| WorkOS AuthKit | `@workos-inc/authkit-nextjs` | 4.3.1 | Next `^16`; React/React DOM `^19`; Node `>=22.11.0`. |
| WorkOS Workers | `@workos-inc/node` | 10.13.0 | Node `>=22.11.0`; export específico para Workers. |
| Better Auth | `better-auth` | 1.7.4 | Next `^16`; React/React DOM `^19`; peer exacto compatible con Drizzle 0.45.2. |

Todas admiten las versiones fijadas de Connect: Next.js 16.3.4, React 19.2.8, TypeScript 5.9.3 y Node 24.20.0. La compatibilidad declarada no sustituye las pruebas de bundle/runtime ni una autenticación real.

## 5. Matriz comparativa

Leyenda: **P** probado en este spike; **D** documentado oficialmente y contrastado con el ADR; **C** viable con condición; **N** no demostrado o no encaja con el contrato actual.

| Criterio | Auth0 | Clerk | WorkOS AuthKit | Better Auth |
| --- | --- | --- | --- | --- |
| Next 16 + React 19 + TS 5.9 | **P:** typecheck y build Next | **P:** typecheck y build Next | **P:** typecheck y build Next | **P:** typecheck y build Next |
| vinext + Workers | **P parcial:** módulo, constructor y store stateful en ruta Workers; sin callback real | **P parcial:** módulo/cliente en Workers; vinext upstream mantiene `auth()` RSC parcial | **P parcial:** AuthKit y cliente Workers cargan; sin callback real | **P:** módulo/handler y flujo local D1 ejecutados; vinext upstream lo marca compatible |
| D1 | **C:** `SessionDataStore` permite adaptador; no se implementó | **C:** sesión Connect adicional posible, sin integración probada | **C:** registro/sesión Connect adicional posible, sin integración probada | **P:** binding D1 directo, esquema generado y flujo real en base local separada |
| Frontera `SessionIdentityProvider` | **P conceptual:** se reduce a sujeto; no se consumen roles Auth0 | **P conceptual:** puede reducirse a sujeto; la cookie estándar no encaja | **P conceptual:** se reduce a usuario/sesión verificados | **P:** sesión neutral entra por el puerto existente y D1 autoriza |
| Usuario o correo + contraseña | **D:** conexión de base de datos y Universal Login; usuario configurable | **D:** email/usuario y contraseña configurables | **D parcial:** email y contraseña; usuario distinto del email no comprobado | **P:** alta por email y entrada por usuario/contraseña en D1 local |
| Segundo factor antes de datos reales | **D:** TOTP y WebAuthn/passkeys; política y plan pendientes | **D:** TOTP/passkeys; política pendiente | **D:** TOTP/passkeys; SSO requiere comprobar nivel del IdP | **D + bundle:** plugin TOTP; el enrolamiento/recuperación no se ejercitó |
| Invitación, activación y desactivación | **D:** alta controlada e invitaciones; cuenta Connect sigue siendo autoridad de ámbitos | **D:** invitaciones y gestión de usuarios | **D:** modo invite-only y API; cuidado con aceptación por dominio | **C:** provisión, email y flujo de invitación deben operarse en Connect; roles del plugin no serían autoridad |
| Recuperación de contraseña | **D:** flujo alojado; falta ensayar recuperación con MFA | **D:** flujo gestionado; falta ensayar cierre global | **D:** flujo gestionado/eventos; falta ensayar factores perdidos | **C:** API disponible, pero correo, soporte, antiabuso y recuperación MFA son responsabilidad propia |
| Cookie, expiración y renovación | **P parcial:** SDK carga con store stateful; HttpOnly permanente y tiempos configurables; flujo no ejecutado | **N para diseño estándar:** `__session` no es HttpOnly | **P parcial:** SDK carga; cookie sellada/refresh documentados; flujo no ejecutado | **P:** cookie HttpOnly/Secure/SameSite Lax observada; sesión D1 verificada |
| Cierre y revocación | **C fuerte:** store stateful permite revocación local; revocación IdP/API y propagación no probadas | **C débil:** revocación gestionada, pero JWT emitido vive hasta expirar | **C:** API de sesión y refresh rotatorio; JWT ya emitido exige control local | **P:** cookie antigua rechazada después de `sign-out`; prototipo neutral prueba revocación individual/global |
| Fuerza bruta y enumeración | **D:** protección de ataque gestionada y errores neutros a configurar | **D:** protección gestionada; verificar umbrales/planes | **D:** detección automática de bots/abuso; verificar métricas y respuesta | **C:** rate limiter disponible, pero memoria local no sirve como autoridad distribuida; operación propia |
| RBAC/ABAC solo servidor | **P por arquitectura:** ninguna metadata del IdP entra en la política | **P por arquitectura:** ignorar Organizations/claims | **P por arquitectura:** ignorar organizaciones/roles externos | **P:** sesión solo entrega sujeto y D1 conserva cuenta/perfil/centro/unidad |
| Aislamiento multitenant y deny-by-default | **P:** prueba común confirma que la cookie no puede inyectar centro o rol | **P:** misma frontera común, condicionado a sesión adicional | **P:** misma frontera común | **P:** integración local completa con la frontera existente |
| Auditoría de autenticación | **D:** logs/stream según plan; retención y exportación pendientes | **D:** Application/Admin Logs según plan | **D:** Events API; Audit Logs es producto distinto | **C:** Connect debe instrumentar, proteger, exportar y retener todo |
| RGPD, salud, residencia y DPA | **C preferida:** identidad primaria UE disponible; tratamientos auxiliares/transferencias siguen abiertos | **C débil:** infraestructura primaria estadounidense según evidencia del ADR | **C débil:** servicio cloud estándar estadounidense | **C:** datos bajo control en D1, pero Workers/correo/soporte no garantizan procesamiento íntegro UE |
| Coste operativo | **C:** precio público parcial; API de sesiones/Enterprise y B2B/B2C requieren oferta | **C:** entrada previsible; sesión adicional aumenta coste técnico | **C fuerte:** entrada pública baja; SSO/dominio/logs se añaden | **C débil:** licencia barata, coste total de seguridad/operación alto y no estimado |
| Mantenimiento y soporte | **D fuerte:** proveedor maduro; responsabilidades compartidas | **D:** servicio gestionado | **D:** servicio gestionado y buen encaje Workers | **N como primera opción:** Connect custodiaría credenciales y toda la operación |
| Bloqueo tecnológico/salida | **C:** exportar hashes/factores exige confirmar soporte y contrato | **C:** componentes/organizaciones aumentan acoplamiento | **C:** herramientas de migración no prueban salida de factores | **D fuerte:** datos/licencia controlados; factores/passkeys tampoco son portables automáticamente |

### 5.1. Puntuación ponderada actualizada

Se conserva la ponderación del ADR para que el cambio sea trazable. Escala 1–5; `total = Σ(peso × nota / 5)`. Es una ayuda de decisión, no una garantía.

| Criterio | Peso | Auth0 | Clerk | WorkOS | Better Auth |
| --- | ---: | ---: | ---: | ---: | ---: |
| Seguridad y ciclo de vida de sesión | 25 | 4 | 2 | 4 | 3 |
| Privacidad, residencia y contrato | 20 | 4 | 2 | 2 | 3 |
| Compatibilidad con stack y puerto | 20 | **4** | **3** | 4 | **5** |
| Identidad multiorganización y futuro SSO | 10 | 5 | 4 | 5 | 3 |
| Coste y previsibilidad del conjunto | 10 | 2 | 4 | 5 | 2 |
| Operación, soporte y madurez | 10 | 5 | 4 | 4 | 1 |
| Exportabilidad y salida | 5 | 3 | 3 | 3 | 5 |
| **Total / 100** | **100** | **79** | **57** | **75** | **64** |

Frente al ADR, solo cambia la nota de compatibilidad a partir de evidencia local: Auth0 3→4, Clerk 2→3 y Better Auth 4→5. WorkOS ya tenía 4 y se mantiene. Auth0 deja de empatar con WorkOS, pero cuatro puntos no eliminan ninguna puerta contractual o de seguridad.

## 6. Evidencia técnica realizada

### 6.1. Sonda de SDK en el stack real

La ruta local temporal `GET /api/spikes/authentication/runtime?run=1` importó los cuatro SDK desde módulos server-only. Solo respondía si el host era `localhost`, `127.0.0.1` o `::1`; en cualquier otro host devolvía 404. No llamó a ningún proveedor y fue eliminada antes de cerrar el PR documental.

Resultados:

- `pnpm typecheck`: correcto.
- `pnpm lint`: correcto.
- `pnpm build:next`: correcto; Next.js reconoce la ruta como dinámica.
- `pnpm build`: correcto; los cuatro SDK quedaron empaquetados en el Worker vinext durante la prueba.
- `pnpm preview --host 127.0.0.1 --port 5187` y petición local: HTTP 200 dentro del runtime Cloudflare.
- Cabecera observada: `Set-Cookie: __Host-connect-session-spike=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`.
- Respuesta observada:

```json
{
  "probes": [
    { "candidate": "AUTH0", "moduleLoaded": true, "serverEntryPoint": true, "instantiatedWithoutNetwork": true },
    { "candidate": "CLERK", "moduleLoaded": true, "serverEntryPoint": true, "instantiatedWithoutNetwork": true },
    { "candidate": "WORKOS_AUTHKIT", "moduleLoaded": true, "serverEntryPoint": true, "instantiatedWithoutNetwork": true },
    { "candidate": "BETTER_AUTH", "moduleLoaded": true, "serverEntryPoint": true, "instantiatedWithoutNetwork": true }
  ]
}
```

Esto prueba imports, resolución de exports, construcción sin red, Web Crypto/cookies básicos y ejecución del Route Handler. No prueba protocolo OIDC, callback, renovación externa, MFA ni API de gestión.

### 6.2. Contrato de sesión y frontera D1

`pnpm test:auth-spike` ejecuta siete pruebas:

| ID | Evidencia | Resultado |
| --- | --- | --- |
| `AUTH-SPIKE-T01` | Cookie opaca con `__Host-`, Path `/`, HttpOnly, Secure y SameSite Lax; sin perfil/centro/residente | OK |
| `AUTH-SPIKE-T02` | La identidad entra por `SessionIdentityProvider`; rol/centro inyectados en cookie no afectan D1 | OK |
| `AUTH-SPIKE-T03` | Revocación individual deniega la petición siguiente aunque se reutilice la cookie | OK |
| `AUTH-SPIKE-T04` | Rotación invalida el token anterior y no amplía la expiración absoluta | OK |
| `AUTH-SPIKE-T05` | Inactividad y cierre global fallan cerrados | OK |
| `AUTH-SPIKE-T06` | Cuenta `SUSPENDED` en D1 se deniega aunque la sesión sea válida | OK |
| `AUTH-SPIKE-T07` | Better Auth genera su esquema en D1 separada, da de alta por email, inicia por usuario/contraseña, emite cookie segura y revoca la sesión | OK |

Resultado medido: **7/7 correctas**. Las seis primeras usan un store en memoria exclusivamente para demostrar el contrato; no es válido entre Workers ni reinicios. T07 utiliza Miniflare y una base D1 temporal `AUTH_DB`, no el binding `DB` de Connect. El SQL generado se comprueba para asegurar que no crea ni menciona `residents`, `baseline_versions` o `profile_scopes`.

### 6.3. Seguridad de dependencias

`pnpm audit --audit-level high` terminó con código 1 por dos avisos, uno alto y uno moderado, en `sharp` 0.35.2 a través de Miniflare/Wrangler. `pnpm why sharp` atribuye la versión vulnerable exclusivamente a dependencias Cloudflare ya presentes; los SDK evaluados resuelven a la versión corregida 0.35.4 a través de Next. `origin/main` ya contiene `miniflare@5.20260831.0-alpha` y `sharp@0.35.2`, por lo que no es una regresión del spike. No se actualiza Cloudflare por quedar fuera de alcance; debe abrirse una tarea separada.

### 6.4. Incidencias durante la prueba

- El primer `pnpm build:next` restringido recibió `Acceso denegado`; el mismo comando con permisos para procesos hijos terminó correctamente.
- El primer arranque `pnpm dev` quedó en optimización de dependencias y no abrió el puerto durante la ventana observada. No se declaró fallo del runtime: se usó el bundle de producción mediante `pnpm preview`, que sí respondió.
- Miniflare rechazó inicialmente `compatibilityDate: 2026-09-10` porque el binario fijado soporta como máximo 2026-09-07. La prueba se alineó con 2026-09-07, sin actualizar dependencias.
- Better Auth rechazó correctamente un usuario sintético con guion conforme a su validador por defecto. Se usó `synthetic_admin_spike`, sin relajar la validación.

## 7. Riesgos, limitaciones y aspectos no comprobados

### 7.1. Bloqueos deliberados

No se crearon cuentas externas ni se introdujeron credenciales. Por ello no se probaron en Auth0, Clerk o WorkOS:

- login alojado, callback y logout reales;
- MFA TOTP/WebAuthn, recuperación de factor y códigos de respaldo;
- invitaciones, verificación de correo y recuperación de contraseña;
- revocación individual/global mediante API, eventos o back-channel logout;
- protección real frente a fuerza bruta, bots y enumeración;
- exportación de logs, usuarios, hashes o factores;
- límites, SLA, soporte, facturación o comportamiento de planes concretos.

Realizar cualquiera de esas pruebas exige un tenant sandbox, credenciales sintéticas o correo externo y requiere autorización separada. No se ha sustituido esa evidencia por una afirmación.

### 7.2. Seguridad y concurrencia pendientes

- La revocación local y la revocación del IdP no forman una transacción distribuida. Debe definirse el orden, reintentos, idempotencia y ventana máxima de propagación.
- Un `SessionDataStore` Auth0 sobre D1 necesitaría diseño físico, índices, TTL, cifrado de tokens, retención, borrado, auditoría y pruebas de carrera. Si comparte esquema será `0002` o posterior; se prefiere evaluar un binding `AUTH_DB` separado.
- El store en memoria del spike no sirve para producción. No ofrece consistencia, persistencia, rate limit distribuido ni recuperación.
- No se han probado CSRF completo, fijación durante callback, replay, rotación concurrente, cabeceras proxy, Server Actions, RSC autenticados ni caché privada con una sesión real.
- No se midió CPU de hash de contraseñas en Workers, carga, latencia D1 o coste por petición.
- No se probó passkey; RP ID, dominio, accesibilidad, recuperación y equivalencia con la política MFA siguen abiertos.
- Las respuestas externas deben ser neutras, pero no se midió indistinguibilidad temporal.

### 7.3. RGPD y datos de salud

Este spike no es una EIPD, evaluación jurídica ni certificación de seguridad. Antes de contratar deben validarse:

- relación responsable/encargado/subencargado por finalidad;
- DPA aplicable y medidas del artículo 28 y 32 RGPD;
- países por categoría: identidad, factores, IP/dispositivo, logs, soporte, correo, backups y recuperación;
- SCC u otro mecanismo de transferencia y medidas complementarias;
- plazos de retención/borrado, restauración de copias y respuesta a incidentes;
- ausencia de perfiles, centro, unidad, residente, vínculo familiar o contenido de salud en el proveedor, claims, URLs, logs, soporte o correo;
- topología D1 y procesamiento de Workers para datos reales, todavía no aprobados.

## 8. Propuesta reutilizable y limpieza final

### Reutilizable como evidencia o criterio de aceptación

- `SessionIdentityProvider` y toda la frontera server-side preexistente se conservan sin cambios; este informe propone usarlos como puerto neutral en una fase posterior.
- Los casos `AUTH-SPIKE-T02`, T03, T04, T05 y T06 son criterios de aceptación reutilizables para cualquier adaptador definitivo, aunque deberán reescribirse contra almacenamiento real.
- La separación propuesta IdP → sesión Connect → sujeto estable → D1 es reutilizable.
- La configuración conceptual de cookie `__Host-`, HttpOnly, Secure, SameSite explícito, expiración absoluta/inactividad y rotación es reutilizable tras revisión de amenazas.
- La idea de un `AUTH_DB` separado para identidad/sesiones merece diseño posterior; la D1 temporal de T07 no es ese diseño.

### Limpieza ejecutada antes de cerrar el spike

- Se eliminó `app/api/spikes/authentication/runtime/route.ts`; no queda una ruta del spike accesible.
- Se eliminaron `spikes/authentication/candidate-runtime-probe.ts` y `spikes/authentication/revocable-session-provider.ts`; no queda store en memoria ni prototipo experimental.
- Se eliminó `tests/authentication-spike.test.ts` y el script `test:auth-spike`; los resultados medidos permanecen en la sección 6.
- Se retiraron Auth0, Clerk, WorkOS y Better Auth de `package.json` y `pnpm-lock.yaml`; ambos archivos vuelven a coincidir con `origin/main`.
- Si una decisión posterior autoriza un proveedor, deberá añadirse únicamente su versión aprobada como dependencia de producto y mediante una tarea nueva.

El diff final contra `main` contiene exclusivamente este informe. No queda código, configuración, persistencia, ruta, prueba específica, secreto ni dependencia del spike.

## 9. Propuesta de cambios posteriores en ADR 0005

Sin cambiar todavía el estado **Propuesto**, un PR posterior debería:

1. Actualizar fecha/base/versiones examinadas y enlazar este informe.
2. Registrar la evidencia de build Next, bundle vinext, ejecución Workers y Better Auth+D1 local.
3. Cambiar solo la fila de compatibilidad de la matriz: Auth0 3→4, Clerk 2→3 y Better Auth 4→5; total 79/57/75/64.
4. Registrar Auth0 como **candidato preferente pendiente de validación**, WorkOS como respaldo condicionado y Better Auth como salida técnica si fallan contrato/residencia/precio y existe operación financiada.
5. Incorporar `SessionDataStore` de Auth0 como vía candidata para sesión stateful de Connect y exigir un diseño independiente de `AUTH_DB`, revocación, TTL, retención, cifrado e idempotencia.
6. Separar explícitamente revocación local inmediata de revocación Auth0 eventual y no prometer atomicidad distribuida.
7. Añadir una puerta I1 obligatoria con tenant sandbox: login/callback/logout, TOTP, recuperación, invitación, cookie, renovación, revocación individual/global, proveedor caído, proxy, RSC, Route Handlers y Server Actions.
8. Añadir como bloqueantes la oferta Auth0 que incluya las API necesarias, DPA y mapa de países/subencargados, y la aprobación Seguridad/DPO.
9. Mantener que Auth0 no recibe roles, permisos, centros, unidades, residentes ni relaciones familiares; D1 sigue siendo la única autoridad.
10. Cambiar a **Aceptado** únicamente en una decisión posterior expresa, después de cerrar las puertas técnicas, contractuales, de privacidad y presupuesto.

## 10. Puertas antes de implementar definitivamente

1. Autorizar un tenant Auth0 sandbox sin datos reales y fijar configuración mínima.
2. Implementar en un bloque separado el adaptador real `SessionIdentityProvider` y un store stateful revisado; no reutilizar el store en memoria.
3. Definir entidades, campos, restricciones, índices, retención y migración `0002` o un `AUTH_DB` independiente antes de persistir sesiones.
4. Demostrar MFA y recuperación accesibles sin rebajar el factor, incluidas cuentas familiares.
5. Demostrar revocación en nuevas peticiones con carreras y fallos de red.
6. Añadir antiabuso por IP+identificador sin permitir enumeración y con auditoría minimizada.
7. Completar EIPD/DPO, contrato, transferencias, operación, observabilidad, backups, RPO/RTO y respuesta a incidentes.
8. Registrar que todo el código y las dependencias desechables ya fueron retirados; cualquier implementación requerirá una tarea posterior y una decisión expresa.

## 11. Fuentes externas contrastadas

Consulta puntual del 10 de septiembre de 2026; el ADR conserva el catálogo contractual y económico completo consultado el 7–8 de septiembre.

- [Auth0 Next.js SDK](https://github.com/auth0/nextjs-auth0): Next 16, cookies, sesión stateful y `SessionDataStore`.
- [Auth0 Brute-force Protection](https://auth0.com/docs/secure/attack-protection/brute-force-protection) y [revocación de sesión](https://auth0.com/docs/api/management/v2/sessions/revoke-session).
- [Clerk: funcionamiento de cookies](https://clerk.com/docs/guides/how-clerk-works/overview) y [Application Logs](https://clerk.com/docs/guides/dashboard/logs/application-logs).
- [WorkOS AuthKit Next.js](https://workos.com/docs/sdks/authkit-nextjs), [sesiones](https://workos.com/docs/authkit/sessions) e [invitaciones](https://workos.com/docs/authkit/invitations).
- [Better Auth: Next.js](https://better-auth.com/docs/integrations/next), [D1](https://better-auth.com/blog/1-5), [sesiones](https://better-auth.com/docs/concepts/session-management), [2FA](https://better-auth.com/docs/plugins/2fa) y [rate limiting](https://better-auth.com/docs/concepts/rate-limit).
- [Mapa de compatibilidad oficial de vinext](https://raw.githubusercontent.com/cloudflare/vinext/main/packages/vinext/src/check.ts).

## 12. Validación final

- Evidencia experimental previa a la limpieza: `pnpm test:auth-spike`, **7/7 correctas**; `pnpm check`, **218/218 pruebas**; y bundle Workers local, **HTTP 200** con los cuatro candidatos cargados sin red externa.
- Validación posterior a la limpieza: `pnpm check`, **correcto**; la instalación reproducible con lockfile podó los cinco SDK, typecheck y lint correctos, **211/211 pruebas**, build Next.js 16 y build vinext/Vite correctos.
- Suite completa posterior a la limpieza ejecutada también por separado con `pnpm test`: **211/211 correctas**.
- Inventario del build Next posterior a la limpieza: solo `/` y `/_not-found`; no queda la ruta del spike.
- Diff final contra `origin/main`: exclusivamente este informe Markdown; `package.json` y `pnpm-lock.yaml` coinciden con la base.
- `git diff --check`: **correcto**, sin errores de espacios en blanco.
- D1 remota: **no creada ni utilizada**.
- Datos reales: **ninguno**.
- Secretos reales: **ninguno**.
- Despliegue: **ninguno**.
- ADR 0005: **permanece Propuesto**.
