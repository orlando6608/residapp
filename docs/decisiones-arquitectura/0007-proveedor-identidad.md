# ADR 0007 — Proveedor de identidad productivo

Estado: **aceptada en lo técnico** el 2026-10-21 (decisión de Orlando, que hace de responsable de Seguridad). Las puertas del responsable de
protección de datos (DPO) quedan abiertas hasta tener residencia piloto: la residencia es la responsable del tratamiento y ResidApp, la
encargada. Sustituye a la propuesta del 2026-10-04, que elegía Microsoft Entra External ID.

## Contexto

Hasta el 2026-10-21 la sesión era `DevSessionIdentityProvider`: una cookie con el `sujeto_externo` en claro que se escribía en
`/DevAuth/Login`, sin contraseña. No era autenticación, y en Azure dev cualquiera podía fabricar esa cookie y entrar como cualquier cuenta,
incluida Plataforma (hallazgo C1 de `docs/seguridad/hallazgos.md`). Bloquea el Portal Familiar real, «Mi cuenta» (ADM-30), las invitaciones,
la importación de usuarios (ADR 0010), una API de integración y cualquier uso con datos reales (AUTH-04).

Lo que fija la aplicación:

- **El puerto ya existe:** `ISessionIdentityProvider` solo devuelve `VerifiedIdentity(ExternalSubject)`. Las cuentas, los perfiles, el
  centro, la unidad, el residente y los permisos los resuelve siempre SQL. El proveedor no recibe roles, ámbitos ni datos clínicos.
- `dbo.cuentas.sujeto_externo` es único y no se puede modificar (trigger de `0023`). Administración da de alta la cuenta (ADM-13) antes del
  primer acceso.
- Dos poblaciones: profesionales en tablets de planta compartidas y familiares en su móvil. PWA sin modo offline. Plataforma necesita
  autenticación reforzada (ADR 0006).
- Datos de salud bajo el RGPD, preferencia por tratarlos en la UE, un solo desarrollador sin equipo de operación, y un piloto de 100 a
  2.500 usuarios activos al mes.
- Se conservan los requisitos del ADR legado (`docs/legado-cloudflare/docs/architecture/0005-seleccion-proveedor-autenticacion.md`,
  apartado 3), entre ellos: TOTP y passkeys evaluables, y el correo o el SMS no se consideran equivalentes a un factor resistente al phishing.

## Lo que cambió al revisarlo (2026-10-21, fuentes oficiales)

- **Microsoft Entra External ID** (tenant externo):
  - sigue **sin TOTP**: los segundos factores son código por correo, SMS (de pago) y passkey
    ([MFA en tenants externos](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-multifactor-authentication-customers),
    actualizado el 2026-06-17);
  - las passkeys exigen un **dominio propio servido por Azure Front Door** (unos 35 $ al mes), y Front Door está **excluido del EU Data
    Boundary** ([servicios excluidos](https://learn.microsoft.com/en-us/privacy/eudb/eu-data-boundary-excluded-services)). Además, la
    pantalla para registrarlas la construye la aplicación con permisos altos de Graph, y Conditional Access **no puede exigirlas** (no hay
    *authentication strengths* en tenants externos)
    ([passkeys](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-sign-in-with-passkey), 2026-10-06);
  - **no hay invitaciones para usuarios**: la aplicación crea la cuenta con Graph y una contraseña temporal que alguien tiene que entregar
    ([funcionalidades de los tenants externos](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-supported-features-customers));
  - ninguna página oficial confirma que el EU Data Boundary cubra los tenants externos
    ([residencia de datos en la UE](https://learn.microsoft.com/en-us/entra/fundamentals/data-storage-eu) habla de Entra ID en general);
  - todo el mundo necesitaría correo para entrar y para el código.
- **Auth0 con tenant en la UE:** el TOTP pide el plan Essentials (unos 175 $ al mes con 2.500 usuarios; el reparto de factores por plan
  solo lo confirma un foro) y la revocación de sesiones es solo de Enterprise ([precios](https://auth0.com/pricing)).
- **ASP.NET Core Identity (.NET 10)** trae de serie TOTP, códigos de recuperación, bloqueo por intentos y passkeys (en MVC, las passkeys se
  montan a mano y cuentan como primer factor)
  ([passkeys en ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys), 2026-10-13).
- La pulsera NFC con PIN del ADR 0009 obliga, con cualquier proveedor, a que ResidApp custodie los PIN y verifique la pulsera.

## Decisión

**ASP.NET Core Identity propio, sobre nuestra Azure SQL.**

- TOTP, códigos de recuperación, bloqueo por intentos y passkeys sin coste ni tercero nuevo; los datos de identidad quedan en la misma base
  (UE) que el resto.
- Alta por **invitación de un solo uso** que entrega Administración (enlace o QR), sin depender de que cada auxiliar tenga correo.
  Recuperación por Administración, que no rebaja el segundo factor.
- Sesiones revocables en SQL, que ya existen (incremento I0).
- **A cambio**, ResidApp custodia las credenciales: hashes de contraseña (los de Identity), secretos TOTP y claves de passkeys. Exige
  configurar bien el bloqueo, el límite de intentos y los registros de seguridad (hallazgo A4), y está entre lo que revisará el DPO.
- `sujeto_externo` sigue siendo la clave de la identidad. **Decidido en I1:** el usuario de Identity es la cuenta (`Id = cuentas.id`,
  `UserName = sujeto_externo`); Identity solo aporta `UserManager` (contraseña, bloqueo, TOTP, códigos de recuperación) sobre
  `dbo.credenciales`, sin `SignInManager` ni su cookie: la sesión es la de I0.

## Incrementos

| Incremento | Entrega | Estado |
| --- | --- | --- |
| **I0** | **Capa común, independiente del proveedor**: sesión con cookie cifrada por Data Protection y validada contra `dbo.sesiones` (script `0061`) en cada petición, con inactividad (30 min) y caducidad absoluta (12 h) que fallan cerradas; cierre de sesión que la invalida en el servidor; política global que exige sesión salvo en las pantallas públicas; acceso de desarrollo solo si se habilita y, fuera de Development, con una clave que no está en el repositorio; límite de intentos; HTTPS obligatorio, HSTS y cookies `Secure`. | Hecho el 2026-10-21 |
| **I1** | **ASP.NET Core Identity** (script `0062`): `/Acceso/Entrar` con contraseña (12 caracteres o más, sin reglas de composición); TOTP obligatorio para el personal y Plataforma, con alta en el primer acceso (QR) y 10 códigos de recuperación de un solo uso guardados como resúmenes SHA-256; familiares solo con contraseña hasta que CJ responda la 3.2; bloqueo tras 5 fallos (contraseña o código) durante 15 minutos, contados en SQL; mismo mensaje para todo fallo; límite de intentos por IP; registro de accesos `dbo.eventos_acceso`, inmutable y separado de la auditoría clínica, más avisos en el registro técnico. El acceso de desarrollo sigue (con clave en Azure dev) hasta I2 y pone la contraseña de prueba (`DevAuth/Credenciales`). | Hecho el 2026-10-22 |
| **I2** | **Invitaciones de un solo uso** (script `0063`): Administración entrega desde la ficha de la cuenta o la autorización del familiar una hoja con QR y enlace (`/Acceso/Invitacion/{token}`), y Plataforma, la de una cuenta de Administración; solo se guarda el resumen SHA-256 del token, vale 72 horas, una nueva anula la anterior y se usa una vez. Con ella la persona elige su contraseña y, si es del personal, activa el TOTP, **que ya solo se da de alta desde una invitación**. Si la cuenta ya tenía contraseña es de **recuperación**: sustituye contraseña y segundo factor y cierra todas sus sesiones (motivo `RECUPERACION`). Las mismas reglas que suspender: no la propia cuenta ni cuentas con perfiles en otros centros. Auditoría administrativa y registro de accesos. Las cuentas de demostración reciben su invitación desde la app (sin secretos en los seeds); `DevAuth/Credenciales` crea una para la cuenta de la sesión. El acceso de desarrollo se mantiene en Azure dev hasta los datos reales (decisión de Orlando). | Hecho el 2026-10-22 |
| I2b | Vincular cuentas que ya existen en otro centro (con verificación explícita, ADR legado). | Pendiente |
| I3 | «Mi cuenta» (ADM-30 y FAM-16): contraseña, segundo factor y sesiones abiertas propias. | Pendiente |
| I4 | Passkeys; obligatorias para Plataforma. | Pendiente |
| I5 | Pulsera NFC con PIN (ADR 0009) como entrada rápida en tablet compartida. | Pendiente |

Criterios de aceptación: los casos AUTH-SPIKE-T02 a T06 del spike legado contra el almacenamiento real. Desde I0 los cubre
`tests/FunctionalTests/SesionTests.cs`: la identidad entra solo por el puerto (una cookie fabricada no da acceso); una sesión cerrada se
deniega en la petición siguiente; la renovación no amplía la caducidad absoluta; la inactividad y la caducidad fallan cerradas; y una cuenta
suspendida se deniega aunque la sesión sea válida.

Desde I1 los cubren además `tests/FunctionalTests/AccesoTests.cs` y `tests/IntegrationTests/SqlCredentialStoreTests.cs`; desde I2,
`tests/FunctionalTests/AccesoTests.Invitaciones.cs` y `tests/IntegrationTests/InvitacionesAccesoTests.cs`.

**Lo que I1 e I2 dejan conscientemente para después (revisar con el DPO):**

- La clave TOTP se guarda en claro en `dbo.credenciales`, como en el almacén por defecto de Identity: quien lea la base puede calcular
  códigos. Cifrarla con Data Protection obligaría a no perder nunca sus claves.
- ~~Quien conozca la contraseña de una cuenta del personal sin TOTP puede dar de alta su propio segundo factor.~~ Cerrado en I2: el
  segundo factor solo se da de alta desde una invitación.
- Quien tenga la hoja de una invitación sin usar entra en esa cuenta: es el secreto que se entrega en mano, por eso vale 72 horas, una
  vez, y se puede anular. Una recuperación no cambia nada hasta que se usa (si hay sospecha, se suspende la cuenta).
- Un mismo código TOTP vale durante su ventana (unos 90 segundos a cada lado); Identity no impide reutilizarlo.
- `eventos_acceso` guarda la IP del intento: el plazo de conservación lo decide el DPO.
- Las cuentas de Plataforma no se recuperan desde la app: por SQL, como su alta (ADR 0006).

## Puertas abiertas

1. **DPO de la residencia piloto:** EIPD; qué datos de identidad se guardan y cuánto tiempo; aceptación del segundo factor por población.
2. **Segundo factor por población** (lo propone Seguridad y lo valida el DPO): TOTP o passkey para el personal; para familiares, a decidir
   con CJ (pregunta 3.2 de `docs/pendientes-cj/portal-familiar-confirmaciones-cj.html`); passkey obligatoria para Plataforma.
3. **Tablets compartidas:** tiempo de inactividad definitivo y entrada rápida (I5).
4. **Contradicciones (I2 no las necesita: la invitación se entrega en mano, sin correo):** el correo de los familiares es opcional en el alta; FAM-10 dice «sin correo ni SMS en el
   piloto». La auditoría de autenticación separada de la clínica que pide el ADR legado existe desde I1 (`dbo.eventos_acceso`;
   antes solo había entradas y salidas en `dbo.sesiones`).

## Alternativas descartadas

| Opción | Por qué no |
| --- | --- |
| Microsoft Entra External ID | La propuesta anterior. Sin TOTP, passkeys caras y fuera del EU Data Boundary por Front Door, sin invitaciones y sin confirmación por escrito de la residencia UE de los tenants externos. Sería la opción si se prefiriera no custodiar credenciales y se aceptara el código por correo. |
| Auth0 con tenant en la UE | Coste (175–545 $ al mes para TOTP o WebAuthn), revocación de sesiones solo en Enterprise y otra cadena de subencargados fuera de Azure. |
| Keycloak, Duende IdentityServer | Otro servidor que operar u otra licencia. |
| Azure AD B2C | Ya no se vende a clientes nuevos. |
| WorkOS AuthKit, Clerk | Tratan la identidad en EE. UU. y encajan peor con .NET. |
