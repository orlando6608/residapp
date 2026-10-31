# Identidad propia en base de datos para la tarea programada del aviso de cambio de cita: lectura de seguridad y privacidad

**BORRADOR del agente `dpo-residapp` — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha: 2026-10-31. Segunda lectura experta, no un dictamen. Revisa una propuesta **antes de escribirla**: no hay script `0083` todavía.

Etiquetas: **[Hecho]** verificado en el repositorio o en una fuente oficial (con archivo y línea o URL), **[Suposición]** mía, **[Decidir]** lo fija Orlando, el DPO real o un abogado.

Complementa `docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md` (el «borrador del aviso de cambio») y el ADR 0012. **No modifico ninguno.**

Términos, en una frase cada uno: **seguridad por filas (RLS)** es una regla de la base de datos que esconde a cada usuario las filas que no le tocan (aquí, las de otros centros); **rol** es un grupo de permisos al que se añaden usuarios; **GRANT por columnas** es dar permiso de leer o escribir solo algunas columnas de una tabla; **DENY** es una prohibición expresa que gana siempre a cualquier permiso; **SCHEMABINDING** es una atadura que impide cambiar una función o tabla mientras otra pieza depende de ella; **usuario contenido** es un usuario que vive dentro de la base de datos, sin cuenta en el servidor; **mínimo privilegio** es dar a cada pieza solo lo que necesita para su trabajo.

## Conclusión

**Admisible con condiciones.** Darle a la tarea una identidad propia es mejor que prestarle el ámbito de una cuenta de Administración, y el rol con permisos por columnas es una medida razonable de mínimo privilegio (arts. 25 y 32 RGPD). **Pero la propuesta, tal como está escrita, no funcionará en un punto y tiene un riesgo operativo grave en el cambio de la política:**

1. **`ALTER FUNCTION` no se puede hacer** mientras la política `seg.pol_centro` dependa de la función. Microsoft lo dice expresamente: «Attempts to modify a function that is used as a predicate on a table within a schema bound security policy results in an error» (apartado «Description» de la documentación de Row-Level Security, consultada el 2026-10-31). [Hecho en fuente oficial; **a verificar ejecutándolo en una base local**, porque no lo he ejecutado.] Hay que quitar los predicados (o la política), cambiar la función y volver a ponerlos.
2. **Mientras la política no esté, `residapp_app` ve todos los centros.** `residapp_app` es `db_datareader` y `db_datawriter` (`database/seguridad/crear_usuario_aplicacion_azure.sql`, líneas 16 y 17): sin política, nada filtra por centro. Por eso el cambio debe ser **una sola transacción**, con comprobación al final, y el script de esquema no puede quedar a medias (`database/aplicar-scripts.sh`, líneas 45 a 54, no es idempotente: un script fallido no se registra y no se puede relanzar sin más).

## Hallazgos y respuestas, por orden de gravedad

### H1 (alta). El cambio de la función (pregunta 4)

- **[Hecho]** La política cubre hoy muchas tablas: se amplió con `ALTER SECURITY POLICY` en al menos 25 scripts (`0030` a `0081`; por ejemplo `0081_envios_correo.sql`, líneas 59 a 62). No existe ningún `ALTER FUNCTION` previo en `database/`. El propio `0030_rls_centro.sql`, líneas 21 a 24, ya avisa de que lo enlazado exige «quitar antes la política y volver a crearla».
- **[Hecho en fuente oficial]** Desactivar la política (`STATE = OFF`) **no** rompe la atadura: para eso hay que quitar los predicados (la propia documentación de Microsoft habla de «remove the predicate in order to break the schema binding»).
- **Acción propuesta:** el `0083` hace, en **un solo lote** con `SET XACT_ABORT ON`, `BEGIN TRY` y `BEGIN TRAN`: (1) guarda el número de filas de `sys.security_predicates`; (2) quita todos los predicados de `seg.pol_centro` (generados desde `sys.security_predicates`, **no** copiados a mano de 25 scripts: se olvidaría alguno); (3) cambia la función con `EXEC sp_executesql N'ALTER FUNCTION ...'` (la sentencia necesita su propio lote, y así se evita partir la transacción con `GO`); (4) vuelve a añadir todos los predicados y deja `STATE = ON`; (5) **comprueba que el número de predicados y el estado son los de antes y, si no, `THROW` y `ROLLBACK`**. Después del despliegue, `database/seguridad/comprobar_aislamiento.sql` y `RlsCentroTests` deben pasar.
- **Coste:** el cambio toma bloqueos de esquema sobre todas las tablas de la política mientras dura. En Azure dev no importa; **en producción, en ventana de mantenimiento** [Decidir Orlando].
- **Alternativa que no toca la política (la más segura, pero con coste):** que la tarea no tenga permisos sobre las tablas, sino solo `EXECUTE` sobre 3 o 4 procedimientos almacenados propiedad de `dbo` y con `EXECUTE AS OWNER` (el dueño pasa la comprobación de `db_owner`). Ventajas: la función, núcleo de la protección de todos los datos clínicos, no se toca; y «el motivo nunca se lee» queda garantizado por construcción. Inconvenientes: la consulta de acceso del portal (`SqlFamilyPortalReader.AccessibleLinks`, líneas 23 a 38) habría que **copiarla** en SQL, y el borrador del aviso de cambio exige «la misma consulta, no una copia» (condición N2). **[Suposición] que `IS_MEMBER('db_owner')` valga 1 dentro de `EXECUTE AS OWNER`: a probar.** Mi recomendación: seguir con la propuesta con las condiciones de abajo; la alternativa solo si Orlando prefiere no tocar la función.

### H2 (alta). Qué añade el rol y frente a qué (pregunta 1)

- **Es aceptable que el rol salte el filtro de centro**, porque la tarea es transversal por naturaleza y la alternativa es peor.
- **Frente a `residapp_app`: menos, no más.** [Hecho] `cuentas` y `ambitos_perfil` quedan **fuera** de la política (`0030_rls_centro.sql`, línea 19) y `residapp_app` las lee. Quien robase la contraseña de `residapp_app` podría leerlas, fijar él mismo un sujeto y un ámbito válidos en `SESSION_CONTEXT` y ver los datos de ese centro, clínicos incluidos: la RLS protege de errores de la aplicación, **no** de quien tiene la credencial (lo dice `0030`, líneas 16 a 18). La credencial de la tarea, en cambio, solo abre unas tablas y columnas, sin contenido clínico.
- **Lo que sí añade:** (a) otra credencial que guardar y rotar; (b) una rama permanente en la función central: **toda tabla que se añada en el futuro a la política queda sin filtro para este rol**, y solo los permisos lo frenan; (c) un lector transversal de **correos de familiares de todos los centros**; (d) **la tarea corre en el mismo proceso que la web**: si alguien consiguiera ejecutar código en la aplicación, tendría las dos credenciales. La separación protege de errores de código y de que las peticiones web usen la credencial transversal; **no protege de un compromiso de la Web App**. Un proceso aparte (otra instancia, WebJob o Function) daría separación real; Orlando ya decidió dentro de la aplicación (ADR 0012, línea 66) y es defendible en un piloto.
- **Frente a prestar el ámbito de una cuenta de Administración (descartado):** habría que dar a la aplicación el ámbito de una persona real por cada centro, con todo lo que ese perfil puede hacer; las acciones de la tarea saldrían en la auditoría **a nombre de esa persona**; y habría que mantener un ámbito por centro. La identidad propia es claramente mejor.
- **Condición:** `residapp_app` **no** puede ser miembro de `residapp_tareas` y el usuario de la tarea **no** puede ser miembro de `db_datareader`, `db_datawriter` ni `db_owner`. Probarlo.

### H3 (media). Permisos mínimos (pregunta 2)

Derivados de las consultas reales (`SqlChangeNoticeStore.cs`, líneas 46 a 110, y `SqlFamilyPortalReader.AccessibleLinks`):

| Tabla | SELECT | UPDATE | Nota |
|---|---|---|---|
| `envios_correo` | tabla entera (no guarda dirección ni contenido: `0081`, líneas 4 a 7) | solo `estado_codigo`, `motivo_codigo`, `acs_operacion_id`, `intentos`, `resuelto_en` | Sin INSERT ni DELETE |
| `citas` | `id`, `centro_id`, `estado_codigo`, `inicio_en`, `cuenta_id`, `vinculo_id` | ninguno | |
| `citas_cambios` | `cita_id`, `centro_id`, `numero`, `accion_codigo`, `actor_perfil_codigo`, `causa_codigo`, `inicio_anterior_en`, `inicio_nuevo_en` | ninguno | **Sin `motivo`** |
| `residentes_familiares` | `id`, `centro_id`, `familiar_id`, `residente_id`, `desvinculado_en` | ninguno | |
| `familiares` | `id`, `centro_id`, `cuenta_id`, `correo` | ninguno | **Sin `nombre_visible` ni `telefono`** (`0022_familiares_autorizaciones.sql`, líneas 23 a 25) |
| `residentes` | `id`, `centro_id`, `estado` | ninguno | **Sin `nombre_visible`**: la tarea no necesita saber quién es el residente |
| `suspensiones_residente` | `residente_id`, `centro_id`, `finalizada_en` | ninguno | |
| `familiares_autorizaciones_cambios` | `vinculo_id`, `centro_id`, `estado_codigo`, `valida_hasta`, `numero` | ninguno | |

- **Sobra / falta:** la lista es la mínima que sale de las consultas **de hoy** [Suposición: no he comprobado nombres de columna uno a uno; la prueba de abajo lo hará]. Si alguien añade una columna a `AccessibleLinks` (por ejemplo `resident.nombre_visible`), la tarea fallará con «permiso denegado»: **falla cerrado, y es lo deseable**, pero hay que saberlo para no «arreglarlo» ampliando el GRANT sin pensar.
- **¿Debe leer `familiares.correo`?** Sí: sin ella no se puede enviar, y la aplicación ya la maneja en memoria para ese fin (`SqlChangeNoticeStore.cs`, líneas 13 y 14). Mitigaciones: solo esa columna de datos personales, sin nombre ni teléfono, sin `residentes.nombre_visible` (así la dirección no queda unida en la base al nombre del residente); la dirección no se escribe en ningún sitio.
- **GRANT por columnas para `citas_cambios.motivo`: sí, y además `DENY SELECT ON dbo.citas_cambios(motivo) TO residapp_tareas`.** Con el GRANT por columnas, la garantía «el motivo nunca se lee» pasa de ser del código a ser de la base. El `DENY` explícito la protege de que mañana alguien conceda un permiso más amplio (por ejemplo `GRANT SELECT ON SCHEMA::dbo` o a `public`); un `DENY` gana a cualquier `GRANT`. Es la garantía que el borrador del aviso de cambio pedía en N4 («el motivo nunca se lee»), reforzada. Lo mismo para el `detalle` de las solicitudes de cita: sin permiso sobre `citas_solicitudes`, no se lee.
- **[Hecho]** El motivo es texto libre de hasta 500 caracteres y puede contener datos de salud (`aviso-cita-cambio-cancelacion-por-correo.md`, línea 22): por eso merece una barrera en la propia base.

### H4 (media). Predicados de bloqueo y disparador (pregunta 3)

- Con la rama nueva, la función devuelve «permitido» para el rol, así que los predicados `AFTER INSERT` y `AFTER UPDATE` **ya no impiden** a este rol escribir una fila con otro `centro_id`. Lo que lo impide es: (a) el rol **no tiene INSERT ni DELETE**; (b) el `UPDATE` por columnas **no incluye `centro_id`** ni ninguna columna de identidad; (c) `TR_envios_correo_guard` (`0081_envios_correo.sql`, líneas 48 a 56) lanza `EMAIL_LOG_IDENTITY_IMMUTABLE` (50590) si en el `SET` aparece una columna de identidad, aunque sea con el mismo valor, y `EMAIL_LOG_ALREADY_RESOLVED` (50591) si la fila ya no está `PENDIENTE`.
- **Consecuencias para las pruebas:** si el rol intenta cambiar `centro_id`, el error esperado es el de **permiso denegado** (falla antes que el disparador), no el 50590; un intento sobre una fila ya resuelta da 50591. El código de la tarea ya filtra por `estado_codigo = 'PENDIENTE'` (`SqlChangeNoticeStore.cs`, líneas 62 y 108), así que no debería disparar el 50591.
- **Riesgo residual de integridad (bajo):** una credencial robada podría resolver como `OMITIDO` o `ENVIADO` avisos pendientes de **cualquier centro** (suprimirlos), y también de la plantilla de propuesta, porque los permisos no distinguen plantilla. No filtra datos y el disparador impide reabrirlos. Aceptable en el piloto [Decidir DPO/Orlando].
- **Se anticipa un problema:** la purga de `envios_correo` (imprescindible antes de producción, borrador del aviso de cambio, 2.5) necesitará `DELETE`. **No concederlo ahora ni «por si acaso»:** decidir entonces si la hace esta misma identidad (con ADR actualizado) u otra.

### H5 (media). Contraseña y cadena de conexión (pregunta 5)

- **[Hecho]** El modelo actual es contraseña SQL en la cadena `ConnectionStrings:ResidApp` (`Program.cs`, línea 48) y el script del usuario de la aplicación no guarda la contraseña en el repositorio (`crear_usuario_aplicacion_azure.sql`, línea 8). Se debe hacer igual con `residapp_tarea`.
- **Dónde:** ajuste de la Web App (Configuración, cadenas de conexión) o, mejor, referencia a Azure Key Vault. Nunca en `appsettings.json`, ni en el CI, ni en el chat, ni en un `.respuestas.json`. Contraseña larga y aleatoria, **distinta** de la de `residapp_app` y de la del administrador.
- **Rotación:** cambiar con `ALTER USER residapp_tarea WITH PASSWORD = '...'` y actualizar el ajuste a la vez (reiniciar la Web App para soltar conexiones antiguas); periodicidad **[Decidir DPO/Orlando]**, y siempre al cambiar la persona que la maneja o ante sospecha de exposición. No cito un plazo concreto.
- **Mejora a considerar [Suposición, a verificar en la suscripción de Azure]:** identidad administrada de Azure (Microsoft Entra) para la Web App, sin contraseña. Elimina la rotación manual. Requiere administrador Entra en el servidor SQL.
- **El fallo de configuración no puede ser silencioso:** con `residapp_app` la tarea ve **cero filas** sin error (la RLS esconde, no rechaza), así que los avisos quedarían `PENDIENTE` para siempre sin que nadie lo note. Que la ausencia de la cadena sea un **error** (no un aviso) al arrancar, y que la tarea compruebe `IS_MEMBER(N'residapp_tareas')` o `db_owner` y lo registre como error si no se cumple. Y que los avisos pendientes antiguos no se envíen tarde si se arregla más tarde: ya lo cubren N3 y N12 del borrador del aviso de cambio (la hora relevante pasada da `CITA_PASADA`).
- **Que la petición web nunca use la cadena de la tarea:** `RegisterAsync` (reserva, dentro de la petición, con ámbito) y el resto (tarea) deben usar fábricas de conexión distintas. Hoy son el mismo `SqlChangeNoticeStore` (`SqlChangeNoticeStore.cs`, líneas 16 a 63). Separar el puerto en dos, o dos instancias con su cadena, y una prueba que lo compruebe.

### H6 (media). Registro de actividades y medidas de seguridad (pregunta 5)

- **Registro de actividades (art. 30 RGPD):** la entrada del aviso de cambio (apartado 5 del borrador) añade: «tratamiento automático por un proceso del sistema, sin persona conectada, para todos los centros que lo tengan activo; lectura limitada a los datos mínimos para el envío».
- **Medidas de seguridad (arts. 25, 30.1.g y 32 RGPD; wording a contrastar con EUR-Lex):** rol de base de datos de mínimo privilegio con permisos por columnas; el motivo del cambio bloqueado por permiso; credencial propia fuera del repositorio; rotación; sin contenido ni dirección en los registros; interruptores de apagado (`Correo__Activo`, `Correo__AvisoCambioCita`).
- **Si el centro es público o concertado** (Esquema Nacional de Seguridad, RD 311/2022): el mínimo privilegio y la gestión de credenciales son medidas exigibles; las medidas exactas, **a verificar** con el responsable de seguridad del centro.
- **Auditoría del servidor:** activar la auditoría de Azure SQL para el usuario `residapp_tarea` si no lo está **[a verificar]**: es la única traza de lo que hace una identidad sin persona.
- **Contrato de encargado (art. 28):** la tarea es un tratamiento de ResidApp por cuenta de la residencia; nada que cambiar si la instrucción de la residencia incluye los tres avisos (borrador del aviso de cambio, parte B).

## Condiciones antes de fusionar

1. **Script `0083`:** `CREATE ROLE residapp_tareas` (el rol nace **sin miembros**); GRANT por columnas como en H3; `DENY SELECT` sobre `citas_cambios(motivo)`; ningún permiso sobre tablas clínicas, `cuentas`, `ambitos_perfil`, `credenciales`, `eventos_acceso`, auditoría ni `citas_solicitudes`; cambio de la función según H1, en una sola transacción, con comprobación del número de predicados y `THROW` si cambia. La rama nueva se escribe `OR IS_MEMBER(N'residapp_tareas') = 1` (si el rol no existiera, `IS_MEMBER` da `NULL` y no abre nada: falla cerrado).
2. **Probado en una base local con datos de prueba antes del CI:** que el script funciona y que `ALTER FUNCTION` directo falla, como dice la documentación (ver «A verificar»).
3. **`database/seguridad/crear_usuario_tarea_azure.sql`:** usuario contenido, `ALTER ROLE residapp_tareas ADD MEMBER`, **sin** `db_datareader`, `db_datawriter` ni `db_owner`; contraseña por variable, nunca en el repositorio; comprobación al final (`IS_MEMBER('db_owner')` = 0 e `IS_MEMBER('residapp_tareas')` = 1).
4. **Pruebas (nuevas, en el estilo de `RlsCentroTests`: usuario sin login, creado y añadido al rol una sola vez por proceso, sin pool ni MARS):**
   - ve `envios_correo` de **varios centros** y las tablas necesarias; actualiza las cinco columnas de resolución;
   - **no puede** leer `citas_cambios.motivo` (ni con `SELECT *`), `residentes.nombre_visible`, `familiares.nombre_visible` ni `telefono`, ni ninguna tabla clínica, `cuentas` o `ambitos_perfil`; no puede insertar ni borrar en `envios_correo`; no puede cambiar `centro_id`;
   - **ejecuta las consultas reales de producción** (constantes SQL compartidas, incluida `AccessibleLinks`) con el rol, no copias, para que un cambio de columna rompa la prueba y no el entorno; lo deseable es exponerlas como constantes internas;
   - un usuario **sin** el rol y sin ámbito sigue sin ver nada (regresión); `residapp_app` y los usuarios de prueba normales no son miembros del rol;
   - `comprobar_aislamiento.sql` y `RlsCentroTests` siguen pasando.
5. **Código:** segunda cadena `ConnectionStrings:ResidAppTareas` solo para la tarea; reserva por la petición con `residapp_app`; falta de cadena = error visible, no aviso; sin dirección ni excepción completa en el registro (condición 14 del borrador del aviso de cambio).

## Condiciones antes de encender el aviso en Azure

1. Script operativo ejecutado **por Orlando** con el administrador; contraseña en la Web App (o Key Vault), no en el chat ni en el repositorio.
2. Comprobación en Azure dev con datos de prueba: la tarea, con ese usuario, resuelve un aviso de una cita de un centro de prueba **y no ve el motivo**.
3. Auditoría de Azure SQL activa para el usuario **[a verificar]**.
4. Todo lo ya exigido por el borrador del aviso de cambio: textos, información a la familia, FAM-10 ampliado por CJ, contrato de Azure, B10.
5. En producción, el cambio de la política con aviso previo y en ventana de mantenimiento.

## Qué debe validar una persona

- **DPO real o abogado:** que el rol transversal y su alcance son proporcionados; el texto del apartado de medidas de seguridad; la rotación; la mención en el registro de actividades; si hay obligaciones del Esquema Nacional de Seguridad en el centro piloto.
- **Orlando:** ejecutar el script operativo; ventana de mantenimiento para el cambio de la política en producción; decidir si prefiere la alternativa de procedimientos almacenados; que la separación en proceso aparte queda para después.

## A verificar o decidir

1. **A verificar (técnico):** que `ALTER FUNCTION` sobre `seg.fn_centro_del_ambito` falla con la política activa en Azure SQL y en el contenedor del CI (la documentación lo dice; no lo he ejecutado), y que el procedimiento de quitar y volver a poner los predicados deja `sys.security_predicates` idéntico.
2. **A verificar:** `IS_MEMBER` dentro de `EXECUTE AS` (pruebas) y, si se elige la alternativa, dentro de `EXECUTE AS OWNER`.
3. **A verificar:** nombres exactos de las columnas de H3 contra el esquema real; las consultas de `ChangeNoticeService` que no he leído por completo.
4. **A verificar:** auditoría de Azure SQL, administrador Entra y plan de Azure para una identidad administrada.
5. **[Decidir Orlando]** periodicidad de rotación de la contraseña; ventana de mantenimiento; proceso aparte en el futuro; si la purga futura de `envios_correo` la hace esta identidad.
6. **[Decidir DPO]** proporcionalidad del acceso transversal; conservación de la auditoría del servidor.
7. **Fuera de este documento:** `eventos_acceso` (IP), TOTP en claro y otras puertas abiertas de los ADR; no se tocan aquí.

Fuentes consultadas el 2026-10-31: repositorio (archivos y líneas citados); documentación de Microsoft, «Row-Level Security» (`https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security`), apartados «Description», «Permissions» y «Security note»; los arts. 25, 28, 30 y 32 del RGPD se citan por su materia y su redacción exacta está **a contrastar con EUR-Lex o el BOE**.
