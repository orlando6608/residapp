# Entorno de producción de ResidApp (ADR 0014, propuesta): lectura de privacidad y seguridad

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-08. Segunda lectura experta del agente `dpo-residapp`, no un dictamen. Es una lectura previa al ADR 0014: no modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL); **[Suposición]** mía; **[Decidir]** lo fija Orlando, CJ, el DPO real o un abogado; **[A verificar]** no he podido confirmarlo en fuente oficial. No incluyo secretos, claves ni identificadores de cuentas de Azure.

## Conclusión

**Admisible con condiciones.** Crear un entorno de producción **vacío** (sin datos reales) en una suscripción propia, con el dominio `app.residapp.es`, es una mejora clara: cierra B9 (suscripción compartida) y A4 (sin registros técnicos) y da el sitio donde cumplir el resto. **Cargar datos reales** no lo es todavía: faltan las condiciones de abajo y todo lo de `docs/privacidad/antes-de-datos-reales.md`. Propongo que el ADR 0014 distinga las dos puertas (apartado 11).

Los cinco puntos que cambiaría de la propuesta, por gravedad:

1. **Copias de seguridad: quince años de copias o de exportaciones no son la forma de cumplir el plazo de quince años.** Los quince años los cumple la base viva (y el centro). Una exportación anual inmutable durante 15 años guardaría durante años datos que el centro ya habría expurgado o suprimido, de **todos los centros a la vez**, y no habría forma de borrar los de uno solo cuando termine su contrato. Hay que acortar y dejar el horizonte máximo escrito (apartado 3).
2. **La telemetría tal como se describe puede registrar credenciales y datos de salud sin querer:** la URL de la invitación lleva el token, las rutas llevan identificadores de residente y de vínculo, y el mensaje de una excepción de SQL puede traer el valor que chocó. Application Insights y los registros de la Web App guardan la URL por defecto (apartado 1).
3. **Front Door queda fuera** salvo decisión expresa de la persona experta: ya consta que está excluido del EU Data Boundary (ADR 0007, líneas 33 a 35) y por él pasaría todo el tráfico, incluidas las cookies de sesión y las passkeys (apartado 5).
4. **El acceso del personal de ResidApp a producción es hoy el de una sola persona, con rol de propietario.** Sin segunda persona ni PIM no hay «cuatro ojos» (apartado 4).
5. **La separación dev/prod hoy depende de que nadie copie el trabajo de despliegue:** el único `deploy` aplica los seeds siempre, el entorno de GitHub que despliega **dev** se llama `production`, y la carpeta `/pendientes-cj` va dentro del paquete publicado con una clave pública (apartado 9).

## Riesgos por orden

| N.º | Riesgo | Gravedad | Apartado |
|---|---|---|---|
| 1 | Copias y exportaciones de larga duración (y bloqueadas) con datos de todos los centros; restaurar deshace expurgos, supresiones y revocaciones | Alto | 3 |
| 2 | Telemetría con tokens, identificadores y mensajes de excepción; auditoría de SQL con valores | Alto | 1 |
| 3 | Pipeline que puede aplicar seeds o desplegar con credenciales equivocadas; `/pendientes-cj` en producción | Alto | 9 |
| 4 | Acceso de operador sin segregación (propietario de suscripción puede llegar a los datos) | Alto | 4 |
| 5 | Front Door / WAF global como encargado de todo el tráfico | Medio-alto | 5 |
| 6 | Región de la copia geo: la región de dev (Spain Central) parece no tener par | Medio | 3 |
| 7 | Repositorio privado: lo ya expuesto no se retira; momento de privatizar | Medio | 6 |
| 8 | Correo de producción: dominio compartido con dev, informes DMARC forenses | Medio | 7 |
| 9 | Defender: qué analiza y qué contienen sus alertas | Bajo-medio | 8 |
| 10 | Key Vault y Blob de claves: servicios nuevos dentro de Microsoft, no subencargados nuevos | Bajo | 2 |

## 1. Observabilidad: Application Insights, Log Analytics, diagnóstico y alertas

**Qué es** (una vez): *Application Insights* mide el funcionamiento de la aplicación (peticiones, errores, tiempos); *Log Analytics* es el almacén donde se guardan esos registros y los de Azure; un *diagnostic setting* envía los registros de un recurso a ese almacén.

**Hechos.**
- [Hecho] Hoy no hay registros técnicos ni App Insights (`docs/seguridad/hallazgos.md`, líneas 311 a 328, A4). La app registra poco y con cuidado: los trabajos de correo solo anotan el tipo de excepción (`ChangeNoticeWorker.cs:61`, `EmailLogPurgeWorker.cs:51`, `UnresolvedNoticeWorker.cs:54`) y `MistralClient.cs:109-144` nunca el contenido (ADR 0011, línea 33).
- [Hecho] `InvalidIdentifierFilter.cs:20` escribe la **ruta completa** en el registro.
- [Hecho] El token de una invitación viaja **en la ruta** (`/Acceso/Invitacion/{token}`, `AccesoController.cs:117` y `134-144`; ADR 0007, incremento I2): quien lo vea durante sus 72 horas puede entrar en esa cuenta. Las rutas del Portal Familiar llevan el `linkId` (`PortalFamiliarController.cs:46-255`) y las pantallas del personal llevan el id del residente o del episodio en la ruta (86 usos de `asp-route-id…` en 37 vistas).
- [Hecho] Application Insights **no guarda la IP por defecto** (la usa para ubicar el país y la descarta; solo se guarda si se activa `DisableIpMasking`). Fuente: Microsoft Learn, «Geolocation and IP address handling», consultada el 2026-11-08 por resumen. Los registros HTTP de la Web App, de Front Door y de SQL **sí** llevan IP.
- [Hecho] `appsettings.json:2-7`: nivel `Information` para la aplicación y `Warning` para `Microsoft.AspNetCore`. `AllowedHosts` es `*` (línea 8).

**Qué se puede registrar y qué no.**

| Se puede | No se debe |
|---|---|
| Hora UTC, método, **plantilla de ruta** (`/Residente/{linkId}/Citas`, no el valor), código de estado, duración | La URL con valores: GUID de residente, `linkId`, `citaId`, **token de invitación**; la cadena de consulta |
| Tipo de excepción (`GetType().Name`) y código de error de dominio (`ACCOUNT_OTHER_CENTERS`…) | `ex.Message`, `ToString()` de excepciones de SQL o de la librería de passkeys (`CuentaController.cs:228` registra `attestation.Failure?.Message`: **[A verificar]** que no lleva datos) |
| Identificador aleatorio de correlación; versión de la aplicación; perfil como categoría | Cuerpos de petición o respuesta; cabeceras `Cookie`, `Authorization` |
| Recuentos (fallos de inicio de sesión por ventana, 403 y 500 por minuto) | Nombre de residente o familiar, correo, teléfono, usuario tecleado (`0062:10-11` ya lo evita en BD) |
| Resultado del dictado: modelo, tokens, segundos, milisegundos (ya existe) | Contenido de audio, transcripción o sugerencia |
| IP **solo** donde ya se guarda con plazo decidido (`eventos_acceso`) | IP en telemetría de uso; no activar `DisableIpMasking` |

Razones: un mensaje de SQL por clave duplicada incluye el valor que choca **[A verificar el formato]**, y el identificador de acceso de una cuenta puede ser un nombre o un correo (`expurgo-y-supresion.md`, apartado 3.7). Una ruta con id de residente unida a la cuenta que la pidió es un **registro de quién miró a quién**: dato de salud por inferencia (RGPD, art. 9) y registro de actividad del trabajador. Si el registro funcional (`eventos_auditoria`) ya cumple esa finalidad, la telemetría técnica no debe duplicarla con más detalle y sin sus garantías de inmutabilidad.

**Cómo conseguirlo** (para el ADR, sin escribir código aquí):
- **Telemetría solo del servidor.** Sin el SDK de JavaScript de App Insights en el navegador: pone cookies propias y entraría en el art. 22 de la LSSI (**[A verificar]** con la persona experta; las landings ya se rigen por «sin terceros ni cookies», `landings-y-passkeys-por-subdominio.md`, apartado 2).
- Un filtro de telemetría que sustituya la ruta por su plantilla, quite la cadena de consulta, elimine el mensaje de las excepciones de base de datos y descarte las dependencias SQL con texto del comando (**[A verificar]** el valor por defecto del SDK elegido para el texto de comandos SQL).
- Corregir `InvalidIdentifierFilter` para registrar la plantilla (ya lo dice B10, `hallazgos.md:461-463`, y vale también para el token).
- Para la auditoría de Azure SQL: grupos de **autenticación (éxito y fallo), cambios de permisos y de esquema**. **No** el grupo que registra todas las sentencias de la cuenta de la aplicación: con consultas parametrizadas el texto puede incluir los valores (**[A verificar]**) y pasarían a un almacén aparte, sin seguridad por filas, con todos los centros mezclados. La auditoría de la cuenta de tareas que pide ADR 0012 (línea 83) debe limitarse igual.
- Alertas con **recuentos y tipos**, nunca con datos; las notificaciones (correo, SMS) las recibe una persona identificada y no reenvían texto de consultas.
- Un almacén de registros **propio de producción** (no compartido con dev ni con otros proyectos: ya lo pedía `aviso-cita-por-correo.md:153`), con acceso solo a quien administre, sin exportación a otros destinos.

**Retención.** [Hecho] Por defecto 30 días en las tablas de Log Analytics, ampliable hasta 730 de forma interactiva y más con archivo (Microsoft Learn, «Data retention and archive in Azure Monitor Logs», consultada el 2026-11-08). El propio documento avisa de que 30 puede ser 31 y de una orden de purga inmediata si se necesita un tope estricto. **Propuesta [Decidir, sin fuente normativa]:** dos clases. Telemetría de funcionamiento: la de por defecto (30 días). Registros de **seguridad** (fallos de inicio de sesión, actividad de la suscripción, auditoría de autenticación de SQL): más larga, con la cifra fijada por el DPO junto con el plazo de `eventos_acceso` y `sesiones` (`expurgo-y-supresion.md`, apartado 2.8: sigue sin decidir). Misma regla para ambos: un solo plazo por tipo de dato personal (la IP), no uno por cada sitio donde aparece.

**Relación con las 72 horas (art. 33).** [Hecho, texto del BOE leído el 2026-11-08] El **responsable** (la residencia) notifica a la autoridad «sin dilación indebida y, de ser posible, a más tardar 72 horas después de que haya tenido constancia» (art. 33.1); el **encargado** (ResidApp) notifica al responsable «sin dilación indebida» (art. 33.2). Así que el reloj de la residencia corre cuando ResidApp se lo comunica; ResidApp debe poder detectar y alcanzar a cada centro **rápido**. Consecuencias:
- El contrato de encargado debe fijar un plazo máximo propio de aviso, más corto que 72 horas (**[Decidir]** la cifra y el canal por centro, con persona de contacto y suplente).
- Los registros de plataforma son la **prueba independiente** si la base se compromete o se restaura: la auditoría inmutable vive dentro de la base y una restauración la acorta (apartado 3). Por eso los registros de seguridad no deben ser tan cortos que un incidente descubierto tarde ya no tenga rastro.
- Una conservación ampliada **de un incidente concreto** (copia aparte con acceso restringido) es una decisión puntual del responsable, no la retención general.
- Hace falta un protocolo de brechas y un simulacro antes de producción (ver lista del apartado 11). Con una sola persona de operación, **[Decidir]** quién cubre vacaciones y noches.

## 2. Key Vault y Blob para las claves de Data Protection

- [Hecho] Hoy las claves de Data Protection están en el almacenamiento de App Service y solo vale con una instancia (B8, `hallazgos.md:455-457`). Esas claves cifran la cookie de sesión y las cookies de estado (`Program.cs:104`, `PasskeyStateCookie.cs`, `PendingAccessCookie.cs`): quien las tenga puede fabricar sesiones.
- **¿Subencargados nuevos?** [Suposición] No: Key Vault y Blob son servicios de Microsoft, el mismo proveedor que Azure SQL y la Web App. Lo que cambia es la **lista de servicios** y su ubicación en el anexo del contrato de encargado y en el registro de actividades. **[A verificar]** que el contrato de Microsoft (condiciones de servicios en línea) cubre cada servicio nuevo en la suscripción nueva.
- **Ubicación.** Misma región que la web, o dentro de la UE. **[A verificar]** si Key Vault y Blob de la región elegida replican a otra región por defecto (si la región no tiene par, no debería, pero hay que mirarlo, apartado 3).
- No contienen datos personales de residentes. Sí son claves de seguridad: **acceso por identidad gestionada y por roles**, nunca por usuario con copia del valor; borrado suave y protección contra purga activados; registros de acceso al almacén de claves al almacén de registros de producción (llevan identidad e IP de quien consulta, no el secreto).
- **Relación con el ADR 0007** (líneas 97 y 98): si algún día la clave TOTP se cifra con Data Protection, perder esas claves **sería perder datos**. Entonces las claves pasan a necesitar copia y recuperación como un dato más. Lo anoto como condición del ADR 0014 para que quien lo escriba no lo trate como un dato de configuración sin importancia.
- **Mistral y ACS.** Meter sus claves en Key Vault no cambia el papel de ninguno. Producción debe arrancar con **IA y correo apagados** hasta cerrar sus puertas: sin `IA:Mistral:ApiKey` el dictado queda desactivado (ADR 0011, línea 6) y el aviso por correo está apagado por defecto (ADR 0012, líneas 81 y 82). Condiciones del dictado: `docs/seguridad/dictado-por-voz-dossier-dpo.md`, líneas 70 a 84 (el anexo del contrato dice «categorías especiales: ninguna»; retención cero; región; entrenamiento).

## 3. Base de datos de producción: copias, retención, exportación y restauración

**Fuentes** (Microsoft Learn, «Automatic, Geo-Redundant Backups – Azure SQL Database», página actualizada el 2026-10-19, leída el 2026-11-08): recuperación a un punto en el tiempo de 1 a 35 días (7 por defecto); retención a largo plazo (LTR) **hasta 10 años**; las copias LTR **admiten inmutabilidad**, las de recuperación a un punto en el tiempo no; una copia no se puede descargar ni leer: solo sirve para restaurar; al borrar un servidor lógico se borran sus bases pero **no** las copias LTR; las copias se borran solas al vencer; la redundancia por defecto es **geográfica hacia la región par**, y la restauración geográfica «crea una base nueva en cualquier servidor de cualquier región de Azure». También hay un aviso sobre RGPD: la vía que ofrece Microsoft para suprimir datos personales de las copias es **acortar la retención**, no borrar filas dentro de ellas.

### 3.1 Respuesta corta: ¿es aceptable?

- **Recuperación a un punto en el tiempo de 35 días y copia redundante en zona: sí.** Es medida del art. 32 (disponibilidad, resiliencia) y es corta.
- **LTR y exportación anual inmutable de 15 años: no tal como está redactada.** Razones:
  1. **Minimización y limitación del plazo (art. 5.1.e).** El plazo de 15 años desde la baja es de la **historia clínica** (`expurgo-y-supresion.md`, apartado 1) y se cumple con la base viva. Una copia anual a 15 años guardaría **además** el dato ya vencido o suprimido durante años: una copia hecha el último año de un residente lo conservaría hasta 15 años después de su plazo.
  2. **Supresión y fin de contrato.** Al terminar con un centro, ResidApp debe devolver y borrar su copia (art. 28.3.g del RGPD, citado en `expurgo-y-supresion.md:40`). Una exportación mezcla todos los centros: no se puede borrar solo uno. Cuanto más larga la retención, más tarde queda limpio.
  3. **Una política de retención bloqueada no se puede acortar ni quitar** (Microsoft Learn, «Immutable storage», resumen de búsqueda de hoy: una política con tiempo bloqueada no se puede borrar y solo se puede alargar). Ante una orden de supresión o un error en la configuración no habría vuelta atrás. El *legal hold* (retención por asunto) sí se levanta, pero entonces quien tenga permiso puede levantarlo: la inmutabilidad deja de ser tal.
  4. **El dato extra no sirve para lo que se pretende.** La exportación a Blob (un fichero con toda la base) escapa a la seguridad por filas, a TDE y a los permisos de la aplicación: quien lea el fichero ve todos los centros. Es una **copia con menos controles que la base**.
- **Lo que sí admito:** una exportación periódica a un contenedor inmutable **de retención corta** (ransomware, borrado malicioso por una cuenta con permisos, error de despliegue: riesgos reales, art. 32.1.b y c). La duración, la frecuencia y si la política va bloqueada o con *legal hold* las **decide el DPO con Orlando**; mi criterio es que el máximo sea un horizonte pequeño y escrito, y no el plazo clínico. **[Decidir]** cifra.
- Hecho: `expurgo-y-supresion.md:434` (Parte B, punto 10) ya pedía verificar «que no hay retención a largo plazo». La propuesta lo contradice; o se cambia la propuesta o se cambia ese documento, pero no las dos cosas a medias. Y `hallazgos.md:441` (B6) dice «definir la retención y las copias según el plazo de conservación (15 años…)»: debería leer «la retención de las copias **no** sigue al plazo clínico».

### 3.2 Restaurar deshace expurgos, supresiones y revocaciones

Ya lo anotaba `expurgo-y-supresion.md` (líneas 168 y 169). Lo amplío con lo que una restauración revierte:
- expurgos y anonimizaciones de familiares (el plan del registro externo con identificadores y fechas es correcto);
- **revocaciones de acceso**: cuentas suspendidas que vuelven a activas, autorizaciones de familiares revocadas que reaparecen, passkeys borradas, invitaciones anuladas, sesiones abiertas. Es un riesgo de **seguridad**, no solo de privacidad;
- el tramo final de la **auditoría inmutable**: lo ocurrido entre el punto restaurado y el incidente deja de constar en la base. Los registros de plataforma (apartado 1) son la única huella de ese tramo.

**Redacción propuesta para la política de copias** (para el contrato y para la información a los centros):

> ResidApp conserva copias de seguridad de la base de datos para poder recuperar el servicio si algo falla. Las copias se guardan en la Unión Europea, cifradas, y se borran solas al cumplirse [N] días. Mientras una copia exista, puede contener datos que el centro haya suprimido o expurgado después de hacerla; esos datos no se usan y desaparecen con la copia. Si hay que restaurar una copia, ResidApp vuelve a aplicar las supresiones y revocaciones registradas y cierra todas las sesiones antes de reabrir el servicio. Al terminar el contrato, los datos del centro se devuelven y se borran de la base; los de las copias desaparecen en como mucho [N] días.

**Procedimiento tras cualquier restauración** (escrito antes de producción): restaurar siempre en una base **nueva**, no sobre la viva; comprobar; reaplicar el registro externo de supresiones y revocaciones; **cerrar todas las sesiones**; avisar al centro afectado y anotar el incidente; borrar la copia de comprobación al terminar.

**El registro externo de supresiones y revocaciones.**
- Contenido: identificador (no nombre), tipo de acción, fecha, quién pidió, quién ejecutó. Nada de nombres, motivos en texto libre ni contenido.
- [Suposición] Un identificador sigue siendo dato personal mientras exista el vínculo (considerando 26 del RGPD): va al registro de actividades, con acceso restringido y **plazo = el de la copia más larga que lo necesite** más un margen. No en el repositorio (público o privado), ni en el almacén de claves, ni dentro de la base que se restaura.
- Hay que decir en la política que **mientras exista una copia el dato no se ha destruido del todo** (idea del bloqueo, `expurgo-y-supresion.md:214`).

**Simulacro de restauración.** Sí, antes de producción y después cada [N] meses **[Decidir]**. Reglas: se hace **dentro de la suscripción de producción**, en un servidor de pruebas aislado con los mismos controles que producción; **nunca** se restaura en dev ni en un portátil; se borra al terminar y queda un acta sin datos (fecha, copia usada, tiempo, comprobaciones). La comprobación de datos se hace por recuentos y claves, no leyendo historias.

### 3.3 Ubicación de la copia geográfica (UE)

- [Hecho] Dev está en Spain Central (dato del encargo).
- [Hecho, resumen de búsqueda de hoy; **A verificar** en «Azure region pairs»] Spain Central figura como **región sin par**, y sin par no hay almacenamiento geo-redundante estándar. Si se confirma, la redundancia «por defecto» de la base **no vale** para esa región y la copia geográfica exige una decisión expresa: una segunda región de la UE/EEE con replicación geográfica activa o grupo de conmutación, o exportaciones a otra región. Cuesta más y añade una **ubicación** al contrato y al registro de actividades.
- Cualquiera que sea la elección: región dentro de la UE/EEE, que esté en el **EU Data Boundary** (**[A verificar]** en la lista vigente de Microsoft), con SQL y Blob disponibles, y escrita en el ADR 0014 y en el anexo de subencargados.
- [Hecho] La restauración geográfica puede crear la base «en cualquier región de Azure». Para que nadie lo haga fuera de la UE, una **política de Azure de regiones permitidas** en la suscripción de producción (y, para la redundancia, la política «evitar GRS» si se decide no usarla; ojo: no se aplica a bases creadas por T-SQL).
- [Decidir] Si se prefiere redundancia entre zonas (si la región tiene tres zonas: **[A verificar]**) y aceptar que un desastre de toda la región supone horas o días de corte, o pagar la copia en otra región. Es una decisión de disponibilidad y presupuesto de la que debe quedar constancia escrita para el centro.

### 3.4 Otras medidas de esta zona

- Bloqueo contra borrado (`CanNotDelete`) en el grupo de recursos y en la base (B6). [Hecho] La suscripción nueva permite quitarlo con un solo clic a quien sea propietario: va junto con el apartado 4.
- Cifrado en reposo con claves de Microsoft (TDE, ya activo en dev, `hallazgos.md:300`). Claves propias en Key Vault: **[Decidir]**, no lo veo necesario para el piloto y aumenta el riesgo de perder la clave.

## 4. Acceso del personal de ResidApp a producción

**Hechos.**
- [Hecho] El administrador de SQL es `db_owner` y se salta la seguridad por filas (`hallazgos.md:129` y `302`). El administrador de Entra de SQL es la cuenta de Orlando (A3, línea 292).
- [Hecho] Un **propietario de la suscripción** puede cambiar la contraseña del administrador de SQL, abrir el firewall o ponerse como administrador de Entra: del plano de control al plano de datos hay un camino corto. **[Suposición]** estándar; **[A verificar]** en la configuración final.
- [Hecho] En la aplicación, Plataforma ve la estructura de los centros, no lo clínico (`antes-de-datos-reales.md`, apartado 9); entra solo con passkey (ADR 0007, I4). La política de privacidad del personal debe decirlo (apartado 9, última viñeta).
- [Hecho] Hoy hay una sola persona que opera ResidApp (ADR 0007, línea 22). El entorno `production` de GitHub exige revisor (Orlando), pero con una sola persona el control es de él sobre él mismo (A1, línea 229).

**Principios que propongo para el ADR.**
1. **Nadie ve datos clínicos de producción sin motivo escrito:** una instrucción del centro o un incidente. Se anota fecha, motivo y quién (registro de intervenciones, sin contenido).
2. **Identidades separadas:** una cuenta de uso diario sin permisos sobre producción y otra de administración con autenticación resistente al *phishing* (passkey o llave física), usada solo para esa tarea. Una cuenta de emergencia, guardada fuera de línea, con alerta si se usa.
3. **Sin permisos permanentes de propietario:** acceso temporal con aprobación y caducidad (PIM o equivalente). **[A verificar]** qué nivel de licencia de Entra exige PIM; si es caro, el equivalente es asignar el rol solo mientras dura la tarea, con alerta en cada cambio de rol.
4. **Administrador de SQL:** solo Entra (sin usuario y contraseña de SQL) cuando sea posible, y la cuenta de tareas (`ResidAppTareas`, hoy con contraseña: `hallazgos.md:394`) con identidad gestionada propia. **[A verificar]** compatibilidad con el despliegue y con el cierre de avisos.
5. **Alertas** de: cambio de asignaciones de roles, cambio de administrador de SQL, apertura de reglas de firewall o de acceso público, uso de la cuenta de emergencia, consulta de secretos. Con el registro de actividad de la suscripción enviado al almacén de producción.
6. **Cuatro ojos para lo irreversible** (expurgo, borrado de cuentas, restauración, levantar un bloqueo): `expurgo-y-supresion.md:144` ya lo pide. **Hoy es imposible con una persona.** **[Decidir]** una segunda persona de confianza con rol limitado (puede ser externa y bajo contrato), o aceptar por escrito que hasta entonces esas operaciones no se hacen en producción.
7. **Acceso de Microsoft:** **[A verificar]** si procede activar *Customer Lockbox* (aprobación del cliente antes de que un técnico de Microsoft acceda). Microsoft dice que su personal no tiene acceso permanente a las copias y solo por JIT bajo auditoría (misma página de copias).
8. **Sin datos de producción en estaciones de trabajo ni en dev**, ni en capturas, ni en correos.
9. **Inquilino (tenant) de Entra.** **[Decidir]** si la suscripción nueva cuelga del mismo inquilino que dev y otros proyectos: los administradores globales de ese inquilino alcanzarían producción (B9 cambia de lugar, no desaparece).

## 5. Red: Private Endpoint, VNet y Front Door / WAF

- **Private Endpoint e integración con VNet: favorables.** Sacan SQL, Key Vault y Blob de Internet, quitan las reglas de firewall por IP (A3, B5) y reducen la superficie. Consecuencia para el trabajo de despliegue [Hecho: `ci-cd.yml:148-166` abre una regla con la IP del runner para aplicar scripts]: con SQL privado, un runner de GitHub no llega. Hace falta otro camino (ejecutor dentro de la red, o aplicar el esquema desde un paso controlado). **Un ejecutor propio dentro de la red es un componente nuevo con alcance sobre la base de producción y hay que tratarlo como acceso privilegiado** (apartado 4).
- **Front Door / WAF: no recomendado para ResidApp** sin decisión expresa.
  - [Hecho] ADR 0007 (líneas 33 a 35, fuente Microsoft Learn, 2026-10-21): Front Door está **excluido del EU Data Boundary**. **[A verificar]** que sigue vigente.
  - Termina TLS en la red de borde: ve todas las cabeceras, las cookies de sesión, los códigos TOTP y las aserciones de passkey, y lleva los datos clínicos. Sus registros guardan IP. Sería un **servicio nuevo con ubicación posiblemente fuera de la UE** en el anexo de subencargados.
  - [Hecho] La app deduce la IP del cliente con `ForwardedHeaders` y redes de confianza fijas (`Program.cs:201-207`, `421`) y la usa en `eventos_acceso` y en el límite de fallos por IP. Con un proxy delante, hay que revisar esa cadena de confianza o la IP registrada será falsa o falsificable (art. 5.1.d, exactitud, y eficacia del control).
  - Alternativas dentro de la región: restricciones de acceso de App Service y límites en la propia aplicación (ya hay límite por IP y de peticiones), o un WAF regional (Application Gateway) si la persona experta quiere WAF. **[A verificar]** precio y disponibilidad en la región.
- `AllowedHosts: "*"` (`appsettings.json:8`): en producción debe ser `app.residapp.es`.
- [Hecho] `AccessFailureThrottle` guarda el contador en memoria (ADR 0007, I2, línea 71): con varias instancias se multiplica el tope. Si producción tendrá más de una instancia, cuenta como cambio de seguridad.

## 6. Repositorio privado en GitHub

**Estado de partida.** [Hecho] `hallazgos.md:452-454`: el repositorio se queda público por ahora; se privatiza ante el primer cliente real o la primera demo a un tercero; el plan de privatización (plan Pro, sustituir el escaneo de secretos y CodeQL, credenciales OIDC que no se rompen) está medido. El documento `hallazgos.md` **no está en el repositorio** (`.git/info/exclude`, línea 18), pero los ADR y `docs/privacidad/` **sí**, y ya cuentan debilidades abiertas (clave TOTP en claro: ADR 0007 líneas 97 y 98; límites de los permisos mínimos: `antes-de-datos-reales.md`, apartado 10).

**Mi lectura.**
1. **El momento.** El disparador «primer cliente real o primera demo a un tercero» llega tarde para un detalle: el ADR 0014 y su flujo de despliegue pondrían en un repositorio público los **nombres y la estructura de producción** meses antes. Propongo privatizar **antes de fusionar a `main` el trabajo de despliegue de producción o de firmar con un centro, lo que ocurra primero**. La ley no obliga a que el código sea privado (`hallazgos.md:452`), pero la seguridad que describe el contrato de encargado no debe hacer pública la forma exacta de defender producción. **[Decidir]** Orlando; el coste orientativo es bajo (`hallazgos.md:454`).
2. **GitHub como proveedor.** Aunque la regla sea «ningún dato personal en el repositorio, PR, issues ni registros de CI», GitHub trata datos de las personas que trabajan en el proyecto (cuenta, correo, actividad). [A verificar] dónde los guarda en el plan Pro (la residencia de datos en la UE solo la he visto para Enterprise Cloud, GA el 2024-10-29: GitHub Blog) y su acuerdo de tratamiento de datos. **[Suposición]** No es subencargado de datos de residentes **mientras la regla se cumpla**; si se rompe, se convierte en incidente. Debe estar en el registro de actividades de ResidApp como proveedor de desarrollo, y la regla escrita en el contrato.
3. **Controles que sustituyen a los de GitHub público** (B7, requisito 2): escaneo de secretos en el *pre-commit* y en el CI; análisis estático alternativo; **ningún paso de CI imprime resultados de consultas** (`ci-cd.yml:100-101` imprime interbloqueos de la base de **pruebas**; en el despliegue de producción, nada de eso y con las contraseñas enmascaradas); registros de ejecución con retención corta **[A verificar]** el plazo por defecto de GitHub.
4. **Lo ya expuesto mientras fue público** (a decir a la persona experta, sin esconderlo):
   - Correo de Orlando en los commits (`hallazgos.md:452`; se puede ocultar hacia adelante con la dirección `noreply`, no hacia atrás sin reescribir historia).
   - Cabeceras completas de un correo de prueba en `docs/pendientes-cj/orlando/dominio-y-correo.respuestas.json` (correo personal de Orlando, IP de salida de Microsoft, firmas DKIM): dato personal de Orlando, ya copiado por quien haya clonado el repositorio.
   - El diseño: ADR, `docs/privacidad/` y las debilidades que reconocen (TOTP en claro, límites de M2, contadores en memoria). Privatizar no lo retira de clones, cachés o archivos web **[Suposición]**.
   - Escaneo de secretos: 416 commits y todas las ramas, 0 secretos, 3 falsos positivos (gitleaks 8, 2026-11-05; `hallazgos.md:452`). **No se detectaron datos reales.**
   - **[A verificar]** que los seeds (`database/seed/centros_prueba.sql`) no usan nombres reales de CJ o de Orlando u otras personas; si los usan, es dato personal publicado.
   - Conclusión que propongo para la persona experta: **«todo lo que fue público se trata como conocido»**. La seguridad de producción no puede depender de ocultarlo (art. 32): ya está bien planteado en la condición 3 de `hallazgos.md:452`.
5. **Credenciales de despliegue de producción.** Credencial federada nueva para la suscripción nueva, con el **entorno de GitHub propio**. Si el repositorio cambia de propietario (organización), hay que actualizarla (B7, requisito 4).
6. El entorno de GitHub llamado `production` hoy despliega **dev** (apartado 9). Hay que renombrarlo antes de crear el de producción real.

## 7. Correo de producción (ACS)

- [Hecho] Decisiones vigentes: Azure Communication Services con datos en la UE; remitente fijo `no-responder@avisos.residapp.es`; DMARC a cuarentena antes de producción tras unos días de informes; seguimiento de aperturas apagado; estado de entrega solo en un almacén propio de la UE a 30 días (ADR 0012, líneas 3 a 7; `antes-de-datos-reales.md:104-106`; `aviso-cita-por-correo.md:155-160`).
- **Recurso propio de producción** en la suscripción nueva: ACS, dominio de correo, clave en Key Vault (apartado 2), almacén de registros de producción (no el de dev). **Datos en la UE confirmados al crearlo** (ADR 0012 lo dejaba «a comprobar», línea 93; el correo de prueba salió por una región de Europa occidental: `aviso-cita-por-correo.md:146`).
- **Dominio.** Recomiendo que **dev y producción no compartan remitente.** Dev puede probar con una copia de un correo y no debe poder enviar «como ResidApp» a familias reales. Propuesta: `avisos.residapp.es` para producción (es el que el ADR 0012 y la información a la familia citan) y para dev otro subdominio o ningún envío (`Correo__Activo` apagado, que ya es el valor por defecto). **[A verificar]** si el mismo dominio puede estar verificado a la vez en dos recursos de ACS. **[Decidir]** Orlando. Un dominio nuevo arrastra poca reputación (ya pasó en Outlook: `antes-de-datos-reales.md:106`); un dominio que mezcla pruebas y producción la daña.
- **DMARC.** `quarantine` antes de producción, como ya decidió Orlando. Pedir solo **informes agregados**; **no** pedir los de fallo con copia del mensaje: pueden incluir cabeceras y trozos del correo. [Hecho] El correo no lleva contenido sanitario, pero los informes agregados llevan IP y volúmenes. Dirigirlos a un buzón controlado.
- **SPF y DKIM** en el dominio de envío y SPF restrictivo en los dominios que no envían (`landings-y-passkeys-por-subdominio.md`, apartado 2).
- **Contrato.** Microsoft como subencargado de ACS en el anexo del contrato (art. 28.2 y 28.4: autorización previa por escrito y mismas obligaciones; texto literal del BOE leído el 2026-11-08). Pendiente de Microsoft por escrito: retención de direcciones de rebote y si se guarda el contenido (`aviso-cita-por-correo.md:151-152`).
- Los avisos al personal o a Orlando por correo de alertas (apartado 1) no deben salir por el mismo dominio ni el mismo recurso que los avisos a familias.
- **Lo que no cambia:** familiares con dirección confirmada por Administración (`0084`), texto genérico, sin `Reply-To`, y todo apagado hasta que la persona experta valide (`antes-de-datos-reales.md`, apartado 7).

## 8. Defender para SQL y para la nube

- [Hecho] En dev no está activado por coste (A3, línea 292: «reconsiderar antes de datos reales»).
- [Hecho, Microsoft Learn, «Advanced Threat Protection», página del 2026-10-06, leída el 2026-11-08] Detecta accesos y consultas anómalos, inyección SQL y abusos; los avisos por correo incluyen **nombre de la base y del servidor, nombre de la aplicación y hora**, y en los avisos de inyección SQL el detalle incluye **la sentencia SQL vulnerable**. Para investigar a fondo conviene la auditoría activada.
- **[A verificar]** qué telemetría analiza exactamente y dónde se procesa y guarda; la página citada no lo detalla.
- **Lectura.** Es una medida razonable del art. 32 (detección) para una base con datos de salud. No añade un tercero fuera de Microsoft. Cautelas:
  1. Los avisos pueden llevar **texto de sentencias**, que puede contener valores: destinatarios limitados (Orlando y la persona que cubra), que no se reenvíen ni peguen en canales de equipo o tickets.
  2. El análisis de vulnerabilidades guarda sus resultados en una cuenta de almacenamiento de la suscripción: debe ser del grupo de recursos de producción, privada y sin acceso público.
  3. La clasificación de datos sensibles opera sobre columnas (**[A verificar]** si lee valores). Útil para la EIPD, pero no es sustituto de ella.
  4. Cualquier plan de Defender que **escanee el contenido** de Blob (p. ej., malware) no debería activarse sobre el contenedor de exportaciones sin haberlo revisado (**[A verificar]**).
  5. Defender para App Service, Key Vault y Storage: valorar uno por uno; cada uno es un tratamiento de metadatos por Microsoft.
- Debe aparecer en el registro de actividades (parte B de ResidApp) como medida de seguridad y en la EIPD, sin cambiar finalidades.

## 9. Separación entre dev y producción

**Hechos que condicionan.**
- [Hecho] `ci-cd.yml:109-232` es **un solo** trabajo de despliegue, con los nombres de dev escritos en el fichero (`rg-residapp-dev`, `sql-residapp-dev`, `app-residapp-dev`: líneas 150, 178, 209, 218, 229) y `APLICAR_SEED=1` siempre (línea 193). Lo único que impide aplicar semillas a producción es el comentario de la línea 169 y la cabecera de `database/aplicar-scripts.sh` (líneas 4 y 5). `docs/tareas/alta-prioridad/pipeline-no-provisiona-bd-azure.md:51-54` repite «nunca».
- [Hecho] El entorno de GitHub que usa ese trabajo de despliegue se llama **`production`** (línea 115) y despliega **dev**.
- [Hecho] La carpeta `docs/pendientes-cj/` (HTML y `.respuestas.json`, también `orlando/`) se copia al paquete publicado (`ResidApp.Web.csproj:10-13`) y se sirve en `/pendientes-cj` (`Program.cs:435-465`) tras una clave que está **en `appsettings.json:12-13`**, es pública y no la sobrescribe Azure (B3, `hallazgos.md:430-433`). Sin clave configurada, nadie entra (`PendientesCjAccess.cs:8` y `31`); con la de `appsettings.json`, cualquiera.
- [Hecho] Passkeys por subdominio exacto y sin migración entre entornos (ADR 0007, líneas 118 a 127).

**Reglas que propongo para el ADR 0014** (cada una con su prueba o control, no solo con un comentario):

| Regla | Cómo se hace cumplir |
|---|---|
| Nunca datos reales en dev | Procedimiento escrito; rótulo «entorno de pruebas, no introduzca datos reales» y `noindex` en `dev.` (**no consta hoy**, `landings-y-passkeys-por-subdominio.md`, apartado 3); sin restaurar copias de producción en dev; la clave de Mistral de dev solo con voces de prueba |
| Nunca datos de dev en producción | Las cuentas de CJ y de Orlando de dev **no** se crean en producción; cada persona recibe su invitación y registra sus passkeys en `app.residapp.es` |
| `APLICAR_SEED` prohibido en producción | Trabajo de despliegue de producción **aparte** (otro fichero o un trabajo con parámetros y sin la variable). **Control técnico recomendado [Decidir]**: que `aplicar-scripts.sh` se niegue a aplicar semillas si el servidor o la base no son de desarrollo, o si falta una marca de entorno en la base |
| Credenciales no mezclables | Entornos de GitHub y credenciales federadas **distintos** (renombrar el actual `production`→`dev`); secretos propios por entorno; ninguna credencial de producción accesible desde ramas o PR |
| `/pendientes-cj` fuera de producción | Excluir la carpeta del paquete de producción, no mapear la ruta y una **prueba que falle** si `GET /pendientes-cj/…` no da 404 en la configuración de producción. Además, quitar la clave de `appsettings.json` o no confiar en ella. Los `.respuestas.json` incluyen las cabeceras de correo de Orlando (apartado 6) |
| Configuración propia por entorno | `Acceso__PasskeyDominio=app.residapp.es`, `Correo__UrlBase`, `AllowedHosts`, `ServerTimeZone`, IA y correo apagados hasta cerrar sus puertas; ninguna copia de los ajustes de dev |
| Almacenes de registros, de claves y de identidades separados | Un almacén de registros, un Key Vault y una identidad gestionada por entorno |
| Demo a un tercero | Siempre sobre dev, con datos ficticios y sin acceso a `/pendientes-cj` |

## 10. Choques con documentos ya escritos

- `expurgo-y-supresion.md:434` (Parte B, punto 10) espera «que no hay retención a largo plazo»: choca con LTR de 10 años y con la exportación de 15 (apartado 3).
- `hallazgos.md:441` (B6) liga las copias al plazo clínico (apartado 3.1).
- `hallazgos.md:452-454` (B7) y la propuesta sobre el momento de privatizar (apartado 6).
- ADR 0012, línea 7 y `aviso-cita-por-correo.md:160`: cuando se cree el ACS de producción, comprobar las cabeceras del primer envío real (sin `Reply-To`).
- `antes-de-datos-reales.md`: no recoge nada de infraestructura de producción. Propuesta: añadir una sección con la lista del apartado 11 y recordar al abrir producción. Lo hace Orlando o la sesión principal, no este agente.

## 11. Previos para abrir producción

**Puerta 1: crear el entorno vacío (sin datos reales).** Puede hacerse con la propuesta ajustada:
1. Suscripción propia, región elegida (y su segunda región o la alternativa), política de regiones permitidas, bloqueo contra borrado.
2. Identidades de administración separadas, cuenta de emergencia y alertas de cambios de rol (apartado 4).
3. Almacén de registros propio de producción y telemetría filtrada (apartado 1), antes de que haya tráfico.
4. Trabajo de despliegue de producción separado, sin semillas, con entornos y credenciales propios; `/pendientes-cj` fuera del paquete (apartado 9).
5. IA y correo apagados.
6. `app.residapp.es` con su registro `TXT` de verificación y el DNS creado **después** del recurso (ADR 0007, punto 5.c y d).

**Puerta 2: cargar datos reales.** Además de la lista de `antes-de-datos-reales.md`:
1. Persona experta (DPO real o abogado) contratada; ha leído también el estado del repositorio público (apartado 6).
2. Política de copias aprobada, con la cifra de retención y la redacción del apartado 3; procedimiento de restauración y primer simulacro hechos.
3. Registro externo de supresiones y revocaciones creado.
4. Plazos de `eventos_acceso`, `sesiones`, auditoría y registros de plataforma decididos (`expurgo-y-supresion.md`, apartado 2.10).
5. Protocolo de brechas con plazo de aviso a cada centro y contacto de guardia; simulacro hecho.
6. Contrato de encargado con la lista de servicios y ubicaciones (región, segunda región, Key Vault, Blob, ACS, Mistral si procede), el plazo de aviso y la política de copias.
7. EIPD con las partes de este documento (observabilidad, copias, acceso del operador).
8. Segunda persona para operaciones irreversibles, o decisión escrita de que esas operaciones no se hacen en producción.
9. Repositorio privado, o decisión escrita de Orlando de lo contrario (apartado 6).
10. Antes de activar el dictado: lo del dossier de Mistral (`dictado-por-voz-dossier-dpo.md`, líneas 70 a 84). Antes de activar el correo: lo del `antes-de-datos-reales.md`, apartado 7.

## Fuentes consultadas el 2026-11-08

Repositorio: archivos y líneas citados. Externas (leídas por una herramienta que devuelve **resúmenes automáticos**, salvo que se indique):
- Microsoft Learn, «Automatic, Geo-Redundant Backups – Azure SQL Database»: `https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview?view=azuresql` (página del 2026-10-19).
- Microsoft Learn, «Advanced Threat Protection – Azure SQL Database»: `https://learn.microsoft.com/azure/azure-sql/database/threat-detection-overview` (2026-10-06).
- Microsoft Learn, «Geolocation and IP address handling» (Application Insights): `https://learn.microsoft.com/en-us/azure/azure-monitor/app/ip-collection` (resumen de búsqueda).
- Microsoft Learn, «Data retention and archive in Azure Monitor Logs»: `https://learn.microsoft.com/azure/azure-monitor/logs/data-retention-archive` (resumen de búsqueda).
- Microsoft Learn, «Immutable storage overview»: `https://learn.microsoft.com/azure/storage/blobs/immutable-storage-overview` (resumen de búsqueda; lo de la cuenta de almacenamiento sin borrar procede de una entrada de la comunidad de Microsoft, **no lo uso**).
- Microsoft Learn, «Cross-region replication in Azure» (regiones sin par): `https://learn.microsoft.com/azure/reliability/cross-region-replication-azure` (resumen de búsqueda; **A verificar**).
- GitHub Blog, «GitHub Enterprise Cloud Data Residency in the EU is generally available»: `https://github.blog/changelog/2024-10-29-github-enterprise-cloud-data-residency-in-the-eu-is-generally-available/` (resumen de búsqueda).
- BOE, Reglamento (UE) 2016/679: `https://www.boe.es/buscar/doc.php?id=DOUE-L-2016-80807` (arts. 28 y 33, lectura parcial por resumen y considerando 85, 2026-11-08).

## A verificar o decidir

**Decidir (Orlando y el DPO real):**
1. Retención de las copias (corta recuperación y LTR) y de la exportación inmutable: cifra, bloqueo o *legal hold*, y si se mantiene.
2. Segunda región o alternativa a la copia geográfica; zona o geo.
3. Plazos de los registros de seguridad de plataforma, alineados con la IP de `eventos_acceso` y `sesiones`.
4. Plazo máximo de aviso de brecha al centro y quién cubre la guardia.
5. Segunda persona para operaciones irreversibles; PIM o equivalente; inquilino de Entra.
6. Momento de privatizar el repositorio.
7. Dominio de envío de producción y de dev.
8. Si se usa WAF (y cuál) o ninguno.
9. Cuándo se hace el simulacro de restauración y cada cuánto.

**A verificar (abogado o fuente oficial):**
10. Spain Central sin región par (documento de pares de regiones vigente) y qué redundancia ofrece la base allí.
11. EU Data Boundary vigente de Front Door y de las regiones elegidas.
12. Si Key Vault y Blob de la región elegida replican fuera de ella.
13. Formato del mensaje de excepción de SQL por clave duplicada; valor por defecto del texto de comandos SQL en el SDK de telemetría; si la auditoría de SQL registra los valores de parámetros.
14. Qué telemetría analiza Defender para SQL y dónde se procesa.
15. Residencia de los datos de GitHub en el plan Pro y su acuerdo de tratamiento; plazo por defecto de los registros de ejecución.
16. Si un mismo dominio puede estar verificado a la vez en dos recursos de ACS.
17. Nivel de licencia necesario para PIM; Customer Lockbox.
18. LSSI art. 22 y cookies del SDK de JavaScript de App Insights (si se usara).
19. Si los seeds contienen nombres reales de personas.
20. Texto literal de los arts. 5.1.e, 28.3.g y 32 del RGPD (leídos en resumen) y del art. 12 de la Llei 21/2000 aplicado a residencias (`expurgo-y-supresion.md`, apartado 1.1).

**Lo que valida una persona antes de usarlo:** todo lo anterior. Yo no soy DPO designado ni abogado.
