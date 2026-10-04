# ADR 0007 — Proveedor de identidad productivo

Estado: **propuesta** (2026-10-04). Pendiente de validación de CJ, Seguridad y el responsable de protección de datos. No se ha
creado ningún tenant, cuenta ni recurso de Azure, ni se ha añadido ninguna dependencia.

## Contexto

La sesión actual es `DevSessionIdentityProvider`: una cookie con el `sujeto_externo` que se escribe en `/DevAuth/Login`, sin
contraseña. No es autenticación. Bloquea el Portal Familiar, «Mi cuenta» (ADM-30), las invitaciones de cuentas que ya existen en
otro centro y cualquier uso con datos reales (AUTH-04).

El legado eligió provisionalmente Auth0 con tenant en la UE, con WorkOS AuthKit como respaldo
(`docs/legado-cloudflare/docs/architecture/0005-seleccion-proveedor-autenticacion.md` y el spike del 2026-09-10). Esa evaluación
pesaba la compatibilidad con Next.js/vinext en Cloudflare Workers. La pila actual es otra (ASP.NET Core .NET 10 MVC, App Service y
Azure SQL), así que se reabre la elección. Se conservan los requisitos de ese ADR (apartado 3), que no dependen de la pila.

Lo que fija la aplicación actual:

- **El puerto ya existe:** `ISessionIdentityProvider` solo devuelve `VerifiedIdentity(ExternalSubject)`. Las cuentas, los perfiles,
  el centro, la unidad, el residente y los permisos los resuelve siempre SQL (`SqlAuthorizationEvidenceProvider`). El proveedor no
  recibe roles, ámbitos ni datos clínicos, y sus claims no autorizan nada.
- `dbo.cuentas.sujeto_externo` es único y no se puede modificar (trigger de `0023`). Administración da de alta la cuenta (ADM-13)
  antes de que la persona entre por primera vez.
- Hay dos poblaciones: profesionales en tablets de planta compartidas y familiares en su móvil. Es una PWA sin modo offline.
- Las cuentas de Plataforma son las más sensibles y necesitan una autenticación reforzada (ADR 0006).
- Los datos son de salud y están sujetos al RGPD, con preferencia por tratarlos en la UE. Hay un solo desarrollador, sin equipo de
  operación, y el piloto tiene entre 100 y 2.500 usuarios activos al mes.

## Propuesta

**Microsoft Entra External ID, con un tenant externo creado con ubicación en un país de la UE.**

1. **Integración nativa:** OIDC con `Microsoft.Identity.Web` y la autenticación por cookie de ASP.NET Core, sin SDK de terceros. El
   inicio de sesión lo aloja Microsoft, que custodia contraseñas y factores y se encarga del bloqueo y el antiabuso.
2. **Mismo proveedor y contrato que el resto del despliegue:** un solo DPA de Microsoft para App Service, Azure SQL y la identidad. Si
   el tenant se crea con ubicación en la UE, Entra almacena y trata la mayoría de los datos dentro de la EU Data Boundary, con
   excepciones documentadas: IP y teléfonos fraudulentos publicados globalmente y parte de la telemetría de seguridad.
3. **Coste:** gratis hasta 50.000 usuarios activos al mes. Los SMS y el dominio personalizado se pagan aparte.
4. **Alta sin vinculación posterior:** al dar de alta una cuenta (ADM-13 y Plataforma), la app crea el usuario en el tenant externo
   con Microsoft Graph y recibe su identificador inmutable en ese momento. `sujeto_externo` guarda emisor e identificador
   (`<tenant>|<oid>`, dentro de los 200 caracteres actuales). No hay autorregistro abierto ni vinculación por correo, y no cambia el
   esquema de `cuentas`.
5. **Revocación:** una cuenta `SUSPENDED` ya se deniega en SQL en cada petición, con independencia de la sesión. Cerrar la sesión de
   un dispositivo concreto exige un almacén de sesiones en Azure SQL (`ITicketStore` o tabla propia), que es un incremento aparte.

### Segundo factor

Según Microsoft Learn (actualizado el 2026-05-21), los tenants externos admiten como segundo factor el **código por correo**, el
**SMS** (de pago) y la **passkey FIDO2**, impuestos con Conditional Access. **No admiten TOTP** (apps autenticadoras). La passkey
exige un dominio personalizado (Azure Front Door) y solo vale para cuentas de correo o usuario con contraseña.

| Población | Propuesta |
| --- | --- |
| Profesionales | Contraseña y código por correo. Passkey opcional en el móvil personal, nunca en la tablet compartida. |
| Familiares | Contraseña y código por correo. SMS como alternativa si CJ lo considera necesario. |
| Plataforma | Passkey obligatoria. |

El código por correo no resiste el phishing. Si Seguridad o el DPO no lo aceptan como segundo factor para AUTH-04, la propuesta pasa
al respaldo.

## Alternativas

| Opción | Valoración |
| --- | --- |
| **Auth0 con tenant en la UE (respaldo)** | Ofrece TOTP, WebAuthn y push, y tiene SDK para ASP.NET Core. Pierde por coste: la API de sesiones y la revocación global son de Enterprise, sin precio público. Además añade un contrato y una cadena de subencargados ajenos a Azure. Pasa a ser la opción principal si se exige TOTP o se rechaza el código por correo. |
| ASP.NET Core Identity (.NET 10, con passkeys y TOTP) | Los datos quedan en nuestra Azure SQL, pero nosotros custodiaríamos credenciales y operaríamos correo, recuperación y antiabuso. Es la objeción que el legado hizo a Better Auth: no hay equipo de operación. |
| Keycloak, Duende IdentityServer | Exigen operar otro servidor o pagar otra licencia. Es demasiada carga para el piloto. |
| Azure AD B2C | Ya no se vende a clientes nuevos; External ID es su sucesor. |
| WorkOS AuthKit, Clerk | Tratan la identidad en EE. UU. y su encaje con .NET es peor. |

## Puertas antes de aceptar

1. Confirmación por escrito de que los **tenants externos** (no solo Entra ID en general) quedan dentro de la EU Data Boundary, y el
   mapa de excepciones por categoría de datos.
2. Aceptación del segundo factor por población (tabla anterior) por parte de CJ, Seguridad y el DPO.
3. Decidir si se usan passkeys en el piloto (coste de Front Door y dominio propio, que debe fijarse antes de que nadie registre una
   passkey, porque cambiarlo después las invalida).
4. Recuperación de cuenta que no rebaje el segundo factor, y cierre por inactividad en las tablets compartidas.
5. EIPD: el proveedor solo recibe correo verificado, credenciales, factores y datos técnicos mínimos. Nunca recibe perfil, centro,
   residente, vínculo familiar ni texto clínico, tampoco en claims, URLs, logs ni correos.

## Incrementos (cada uno con su encargo)

| Incremento | Entrega |
| --- | --- |
| I1 | Tenant externo de pruebas con datos sintéticos: login, callback, logout, cookie (HttpOnly, Secure, SameSite Lax) y denegación de una cuenta suspendida. |
| I2 | `EntraSessionIdentityProvider` sustituye entero a `DevSessionIdentityProvider` (el registro está en `Program.cs`). `DevAuth` queda solo en Development. |
| I3 | Alta de cuentas con Graph desde ADM-13 y Plataforma, e invitación por correo. Solo después se pueden vincular cuentas que ya existen en otro centro. |
| I4 | Conditional Access: segundo factor para todos y passkey para Plataforma. |
| I5 | Sesiones revocables en Azure SQL y «Mi cuenta» (ADM-30). |

Criterios de aceptación de I1–I5: los casos AUTH-SPIKE-T02 a T06 del spike legado, reescritos contra el almacenamiento real. La
identidad entra solo por el puerto; un rol o centro inyectado en una cookie no afecta a SQL; una sesión revocada se deniega en la
petición siguiente; la rotación no amplía la expiración absoluta; la inactividad y el cierre global fallan cerrados; y una cuenta
suspendida se deniega aunque la sesión sea válida.

## Fuentes

- [MFA en tenants externos](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-multifactor-authentication-customers) (consultado el 2026-10-04).
- [Almacenamiento y tratamiento de datos de clientes europeos en Entra ID](https://learn.microsoft.com/en-us/entra/fundamentals/data-storage-eu) (consultado el 2026-10-04).
- [Precios de Microsoft Entra External ID](https://azure.microsoft.com/pricing/details/microsoft-entra-external-id/) (consultado el 2026-10-04).
