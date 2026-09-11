# 0002. Contrato de datos mínimo de Residente y Basal

- **Estado:** contrato alineado con `LBF-CONNECT-2026-09-06-V1.1`; `0001` autorizada para validación local ficticia
- **Fecha:** 6 de septiembre de 2026
- **Ámbito:** puerta previa a D1/Drizzle para Residente, ubicación, Basal y los permisos estrictamente relacionados
- **Datos:** exclusivamente ficticios durante el prototipo

Este documento no crea una decisión clínica nueva. Traduce el contrato semántico consolidado por `LBF-CONNECT-2026-09-06-V1.1` para diseñar y validar localmente tablas, claves foráneas, índices y restricciones D1/Drizzle. Los nombres técnicos son propuestas revisables; las reglas funcionales proceden de la línea base vigente. No se modifican ni sustituyen el PRD, la matriz, los wireframes ni `AGENTS.md`.

## Fuentes y criterio de lectura

- Línea base `LBF-CONNECT-2026-09-06-V1.1` y su declaración v1.1.
- PRD v0.5 consolidado: requisitos de Residente, Basal, autorización, ubicación y correcciones.
- Matriz de permisos de seis perfiles v0.2.
- Wireframes canónicos: Auxiliar v0.2, Enfermería v0.2, Medicina v0.2, Portal Familiar v0.2, Administración v0.2 y Dirección/Coordinación Clínica v0.1.
- Matriz de trazabilidad funcional v0.1.
- `AGENTS.md` v1.3.
- Registro técnico `0001-modelo-minimo-residente-basal.md`, únicamente como antecedente de las decisiones técnicas propuestas.

Se aplica la jerarquía de la línea base v1 y de `AGENTS.md`. `Obligatorio al firmar` permite que un borrador esté incompleto, pero impide activarlo como vigente. Toda capacidad indicada en las columnas de creación o modificación presupone cuenta activa, perfil activo explícito, ámbito autorizado y permiso específico cuando corresponda. El servidor debe obtener identidad, perfil, ámbito, autoría y fecha de una sesión y de datos confiables; nunca de campos libres enviados por el cliente.
Los tipos `ID opaco` se representan actualmente como UUID v4 no secuencial. Las fechas son calendarias ISO 8601 y las fechas/hora son instantes ISO 8601 en UTC. La edad no se persiste: se calcula desde la fecha de nacimiento y la fecha de consulta.

## Centro y estructura física

Edificio y planta son niveles opcionales. Centro y unidad son obligatorios. Habitación y plaza/cama forman parte de la ubicación administrativa cuando el centro las utiliza. Los centros son provisionados por la plataforma; Administración del centro gestiona la estructura subordinada, pero no crea centros. Crear una estructura no concede por sí mismo acceso a sus residentes.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador del centro | `center.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Plataforma, fuera de los seis perfiles del centro | Nadie edita el ID | Provisionamiento de plataforma autorizado | No; inmutable | Sí, creación | Decisión de producto 2026-09-05; PRD 7.1; `ADM-05`; AGENTS 11 | Identidad del actor de plataforma y procedimiento de provisionamiento antes del esquema |
| Nombre del centro | `center.display_name` | Texto | Obligatorio | Sin valor por defecto | Texto de provisionamiento | Plataforma, fuera de los seis perfiles del centro | Plataforma mediante cambio trazable; Administración no lo provisiona | Centro provisionado | No; conserva historial en auditoría | Sí | Decisión de producto 2026-09-05; PRD 7.1; `ADM-05` | Longitud, normalización, unicidad y alcance de edición administrativa posterior |
| Identificador del edificio | `building.id` | ID opaco | Condicional: solo si se usa este nivel | Generado por servidor | No aplica | Administración | Nadie edita el ID | Centro propietario | No; inmutable | Sí, creación | PRD `ORG-03`; `ADM-05` | Si un centro necesita códigos externos de edificio |
| Centro del edificio | `building.center_id` | ID opaco, referencia a centro | Obligatorio si existe edificio | Centro del contexto server-side | Centros autorizados para Administración | Administración | No se cambia; mover exige una operación trazable aún no definida | Centro propietario | No; inmutable | Sí | PRD `ORG-03`; `ADM-05` | Política para reestructurar un edificio ya utilizado |
| Nombre del edificio | `building.display_name` | Texto | Obligatorio si existe edificio | Sin valor por defecto | Texto administrativo | Administración | Administración | Centro propietario | No; cambios en auditoría | Sí | `ADM-05` | Longitud y unicidad dentro del centro |
| Identificador de planta | `floor.id` | ID opaco | Condicional: solo si se usa este nivel | Generado por servidor | No aplica | Administración | Nadie edita el ID | Centro y edificio propietarios | No; inmutable | Sí, creación | PRD `ORG-03`; `ADM-05` | Ninguna funcional |
| Edificio de la planta | `floor.building_id` | ID opaco, referencia a edificio | Obligatorio si existe planta | Edificio seleccionado en ámbito | Edificios del mismo centro | Administración | No se cambia; mover exige operación trazable | Centro y edificio propietarios | No; inmutable | Sí | `ADM-05` | Cómo representar plantas en centros sin edificio explícito |
| Nombre o código de planta | `floor.display_name` | Texto | Obligatorio si existe planta | Sin valor por defecto | Texto administrativo | Administración | Administración | Centro y edificio propietarios | No; cambios en auditoría | Sí | `ADM-05` | Longitud y unicidad dentro del edificio |
| Identificador de unidad | `unit.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Administración | Nadie edita el ID | Centro propietario | No; inmutable | Sí, creación | PRD 7.1 y `ORG-03`; `ADM-05` | Ninguna funcional |
| Centro de la unidad | `unit.center_id` | ID opaco, referencia a centro | Obligatorio | Centro del contexto server-side | Centros autorizados | Administración | No se cambia; mover exige operación trazable | Centro propietario | No; inmutable | Sí | PRD `ORG-03`; `ADM-05`; Matriz §9 | Política para reorganizar una unidad con residentes |
| Edificio de la unidad | `unit.building_id` | ID opaco, referencia a edificio | Opcional | `null` | Edificios del mismo centro | Administración | Administración | Centro propietario | No; cambios en auditoría | Sí | PRD `ORG-03`; `ADM-05` | Confirmar si se deriva siempre de planta cuando hay planta |
| Planta de la unidad | `unit.floor_id` | ID opaco, referencia a planta | Opcional | `null` | Plantas del mismo centro y edificio | Administración | Administración | Centro propietario | No; cambios en auditoría | Sí | PRD `ORG-03`; `ADM-05` | Confirmar si una unidad puede abarcar varias plantas |
| Nombre de la unidad | `unit.display_name` | Texto | Obligatorio | Sin valor por defecto | Texto administrativo | Administración | Administración | Centro propietario | No; cambios en auditoría | Sí | PRD 7.1 y `ORG-03`; `ADM-05` | Longitud y unicidad dentro del centro |
| Identificador de habitación | `room.id` | ID opaco | Condicional: solo si el centro usa habitaciones | Generado por servidor | No aplica | Administración | Nadie edita el ID | Centro y unidad propietarios | No; inmutable | Sí, creación | PRD `ORG-03`; `ADM-05` y `ADM-06` | Ninguna funcional |
| Unidad de la habitación | `room.unit_id` | ID opaco, referencia a unidad | Obligatorio si existe habitación | Unidad seleccionada en ámbito | Unidades del mismo centro | Administración | No se cambia; mover exige operación trazable | Centro y unidad propietarios | No; inmutable | Sí | `ADM-05` y `ADM-06` | Política para mover habitaciones ya ocupadas |
| Nombre o código de habitación | `room.display_name` | Texto | Obligatorio si existe habitación | Sin valor por defecto | Texto administrativo | Administración | Administración | Centro y unidad propietarios | No; cambios en auditoría | Sí | PRD `ORG-03`; `ADM-06` | Longitud y unicidad dentro de la unidad |
| Identificador de plaza/cama | `place.id` | ID opaco | Condicional: solo si el centro usa plazas/camas | Generado por servidor | No aplica | Administración | Nadie edita el ID | Centro, unidad y habitación propietarios | No; inmutable | Sí, creación | PRD `ORG-03`; `ADM-05` y `ADM-06` | Terminología visible por centro: plaza, cama u otra |
| Habitación de la plaza/cama | `place.room_id` | ID opaco, referencia a habitación | Obligatorio si existe plaza/cama | Habitación seleccionada en ámbito | Habitaciones de la misma unidad | Administración | No se cambia; mover exige operación trazable | Centro, unidad y habitación propietarios | No; inmutable | Sí | `ADM-06` | Confirmar si se permiten plazas sin habitación |
| Nombre o código de plaza/cama | `place.display_name` | Texto | Obligatorio si existe plaza/cama | Sin valor por defecto | Texto administrativo | Administración | Administración | Centro, unidad y habitación propietarios | No; cambios en auditoría | Sí | PRD `ORG-03`; `ADM-06` | Longitud y unicidad dentro de la habitación |

## Identidad administrativa y estado del residente

El alta administrativa y el basal profesional son operaciones diferentes. El alta crea la identidad en estado activo y deja el basal pendiente. La edad, el estado basal visible y toda ubicación actual son proyecciones; el historial de ubicación es su única fuente de verdad. Ninguna de esas proyecciones debe aceptarse como valor de autoridad enviado por el navegador ni persistirse como una segunda fuente mutable.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador del residente | `resident.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Administración; Enfermería solo con permiso de alta | Nadie edita el ID | Centro y unidad autorizados | No; inmutable | Sí, alta | PRD `RES-01` y `RES-02`; Matriz §3; `ADM-04`; `ENF-19` | Ninguna funcional |
| Centro actual | `resident.current_center_id` | Proyección de ID opaco desde ubicación vigente | Obligatorio en lectura | Primer intervalo del alta | Centro del intervalo vigente | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro autorizado | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `RES-01`; Matriz §§3 y 9; `ADM-03` y `ADM-04` | En el primer bloque no existe traslado entre centros: se tramita baja en origen y alta independiente en destino |
| Unidad actual | `resident.current_unit_id` | Proyección de ID opaco desde ubicación vigente | Obligatorio en lectura | Primer intervalo del alta | Unidad del intervalo vigente y del mismo centro | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro y unidad autorizados | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `RES-01`; Matriz §§3 y 9; `ADM-03` y `ADM-04` | La pantalla y el procedimiento de traslado interno pueden aplazarse; el historial debe admitirlos |
| Edificio actual | `resident.current_building_id` | Proyección de ID opaco desde ubicación vigente | Opcional | `null` si el intervalo no usa edificio | Edificio del intervalo vigente | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro y unidad autorizados | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `ORG-03` y `RES-01`; `ADM-03` y `ADM-04` | Coherencia con planta y unidad |
| Planta actual | `resident.current_floor_id` | Proyección de ID opaco desde ubicación vigente | Opcional | `null` si el intervalo no usa planta | Planta del intervalo vigente | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro y unidad autorizados | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `ORG-03` y `RES-01`; `ADM-03` y `ADM-04` | Coherencia con edificio y unidad |
| Habitación actual | `resident.current_room_id` | Proyección de ID opaco desde ubicación vigente | Opcional | `null` | Habitación del intervalo vigente | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro y unidad autorizados | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `ORG-03` y `RES-01`; `ADM-03` y `ADM-04` | Si habitación es obligatoria en algún centro |
| Plaza/cama actual | `resident.current_place_id` | Proyección de ID opaco desde ubicación vigente | Opcional | `null` | Plaza/cama del intervalo vigente | Servidor a partir de `resident_location` | Solo cambia al crear otro intervalo | Centro y unidad autorizados | No se versiona aparte | Sí en la transición fuente | Decisión de producto 2026-09-05; PRD `ORG-03` y `RES-01`; `ADM-03` y `ADM-04` | Si plaza/cama es obligatoria y si admite ocupación simultánea |
| Nombre | `resident.display_name` | Texto | Obligatorio | Sin valor por defecto | Texto administrativo | Administración; Enfermería con permiso de alta | Administración | Centro, unidad y residente autorizados | No; cambios quedan en auditoría | Sí | PRD `RES-01`; `ADM-03`, `ADM-04`; `ENF-19` | Partes del nombre, longitud y normalización |
| Fecha de nacimiento | `resident.birth_date` | Fecha | Obligatorio | Sin valor por defecto | Fecha válida; no futura | Administración; Enfermería con permiso de alta | Administración | Centro, unidad y residente autorizados | No; cambios quedan en auditoría | Sí | PRD `RES-01`; `ADM-04`; `ENF-19` | Si se admiten fechas incompletas o estimadas |
| Sexo documentado | `resident.documented_sex_code` | Código | Obligatorio | Sin valor por defecto | `male`, `female`, `other`, `unknown`; etiquetas Hombre, Mujer, Otra categoría documentada y No consta; sin texto libre ni inferencia | Administración; Enfermería con permiso de alta | Administración | Centro, unidad y residente autorizados | No; cambios quedan en auditoría | Sí | PRD v0.5 `RES-01`; `ADM-04`; `ENF-19`; `DEC-CONNECT-2026-09-06-D1-P01-P08` | Ninguna para `0001` |
| Identificador interno del centro | `resident.internal_reference` | Texto | Opcional en los wireframes | Generado o validado por el centro | Formato definido por el centro; nunca se usa como autorización | Administración; Enfermería con permiso de alta | Administración | Centro propietario | No; cambios quedan en auditoría | Sí | `ADM-03`, `ADM-04`; `ENF-19` | Quién lo genera, formato, obligatoriedad y unicidad dentro del centro |
| Estado administrativo | `resident.status` | Código | Obligatorio | `ACTIVO` | `ACTIVO`, `INACTIVO` | Servidor al completar un alta autorizada | Administración puede inactivar; reactivación queda fuera del primer bloque | Centro, unidad y residente autorizados | Sí, mediante auditoría o historial de estado | Sí | Decisión de producto 2026-09-05; `ADM-02`; AGENTS 5.1 y 7.5 | La reactivación y sus efectos pueden definirse después |
| Motivo de inactivación | `resident.inactivation_reason` | Texto | Obligatorio al pasar a `INACTIVO` | `null` | Texto administrativo obligatorio y minimizado | Administración | Nadie sobre el registro original; una aclaración es trazable | Centro y residente autorizados | Sí; inmutable tras la transición | Sí | Decisión de producto 2026-09-05; AGENTS 10 | Longitud definitiva aplazable |
| Fecha/hora de inactivación | `resident.inactivated_at` | Fecha/hora | Obligatorio al pasar a `INACTIVO` | `null` | Hora del servidor | Servidor | Nadie | Centro y residente autorizados | Sí; inmutable | Sí | Decisión de producto 2026-09-05; AGENTS 10 | Ninguna funcional |
| Cuenta que inactiva | `resident.inactivated_by_account_id` | ID opaco, referencia a cuenta | Obligatorio al pasar a `INACTIVO` | `null` | Cuenta de Administración activa y autorizada | Servidor | Nadie | Centro y residente autorizados | Sí; inmutable | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 y 10 | Ninguna funcional |
| Perfil que inactiva | `resident.inactivated_by_profile` | Código de perfil | Obligatorio al pasar a `INACTIVO` | `null` | `ADMINISTRACION` | Servidor | Nadie | Perfil activo del mismo centro | Sí; inmutable | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 y 7.5 | Ninguna funcional |
| Fecha/hora del alta | `resident.created_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor | Nadie | Centro, unidad y residente | No; inmutable | Sí | AGENTS 5.2, 10 y 11 | Retención y precisión temporal |
| Cuenta autora del alta | `resident.created_by_account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta autenticada resuelta por servidor | Cuentas activas autorizadas | Servidor | Nadie | Centro y unidad del alta | No; inmutable | Sí | Matriz §§9 y 10; AGENTS 5.2 y 10 | Ninguna funcional |
| Perfil autor del alta | `resident.created_by_profile` | Código de perfil | Obligatorio | Perfil activo resuelto por servidor | `ADMINISTRACION`; `ENFERMERIA` solo con permiso de alta | Servidor | Nadie | Mismo grant de centro/unidad que autorizó el alta | No; inmutable | Sí | PRD `RES-02`; Matriz §§3, 9 y 11; AGENTS 5.2 | Qué profesionales concretos de Enfermería reciben ese permiso en cada centro |
| Estado basal visible | `resident.baseline_state` | Proyección, no entrada de cliente | Obligatorio en lectura | `PENDIENTE` tras el alta | `PENDIENTE`, `BORRADOR`, `FIRMADO_VIGENTE` | Servidor a partir de las versiones | Servidor a partir de transiciones válidas | Centro, unidad y residente autorizados | Sí, derivado del historial de versiones | Sí en las transiciones fuente | PRD 7.2, `RES-02`, `BAS-05`; `ADM-04`; `ENF-23` | Persistir el estado agregado o derivarlo sin duplicar fuentes de verdad |

## Historial de ubicación

El historial de ubicación es la fuente de verdad de la ubicación actual. La ubicación vigente es el único intervalo sin fin. El alta crea el primer intervalo y cada cambio dentro del mismo centro cierra el anterior y abre el siguiente en una única operación. El esquema debe conservar esta capacidad aunque la pantalla y el procedimiento de traslado interno se implementen después. En el primer bloque no existe traslado entre centros: se registra la baja/inactivación en origen y un alta independiente en destino, sin trasladar automáticamente identidad, basal, grants ni historial. No se admiten campos mutables paralelos en Residente que puedan divergir. Ningún cambio de URL o de identificador amplía el ámbito autorizado.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador del intervalo de ubicación | `resident_location.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Administración; el primer intervalo también se crea en un alta de Enfermería autorizada | Nadie | Centro, unidad y residente | Sí; cada cambio crea otro intervalo | Sí | `ADM-03` a `ADM-06`; AGENTS 10 | Ninguna funcional |
| Residente ubicado | `resident_location.resident_id` | ID opaco, referencia a residente | Obligatorio | Residente del recurso autorizado | Residentes del ámbito | Administración; Enfermería solo al crear el primer intervalo dentro del alta autorizada | Nadie | Centro, unidad y residente | Sí; inmutable por intervalo | Sí | PRD `RES-01` y `RES-02`; Matriz §9 | Ninguna funcional |
| Centro durante el intervalo | `resident_location.center_id` | ID opaco, referencia a centro | Obligatorio | Centro confirmado | Centro del alta; los intervalos posteriores del primer bloque conservan el mismo centro | Administración; Enfermería solo en alta autorizada | Nadie | Debe concordar con unidad y residente | Sí; inmutable por intervalo | Sí | Decisión de producto 2026-09-05; `ADM-03` a `ADM-06`; Matriz §9 | Traslados entre centros fuera del primer bloque |
| Unidad durante el intervalo | `resident_location.unit_id` | ID opaco, referencia a unidad | Obligatorio | Unidad confirmada | Unidades del mismo centro | Administración; Enfermería solo en alta autorizada | Nadie | Centro y unidad autorizados | Sí; inmutable por intervalo | Sí | Decisión de producto 2026-09-05; `ADM-03` a `ADM-06`; Matriz §9 | Interfaz/procedimiento aplazables; la relación histórica no se aplaza |
| Edificio durante el intervalo | `resident_location.building_id` | ID opaco, referencia a edificio | Opcional | `null` | Edificios compatibles con centro/unidad | Administración; Enfermería solo en alta autorizada | Nadie; se abre otro intervalo | Centro y unidad autorizados | Sí; inmutable por intervalo | Sí | PRD `ORG-03`; `ADM-03` a `ADM-06` | Si se deriva de unidad/planta |
| Planta durante el intervalo | `resident_location.floor_id` | ID opaco, referencia a planta | Opcional | `null` | Plantas compatibles | Administración; Enfermería solo en alta autorizada | Nadie; se abre otro intervalo | Centro y unidad autorizados | Sí; inmutable por intervalo | Sí | PRD `ORG-03`; `ADM-03` a `ADM-06` | Si se deriva de unidad |
| Habitación durante el intervalo | `resident_location.room_id` | ID opaco, referencia a habitación | Opcional | `null` | Habitaciones de la unidad | Administración; Enfermería solo en alta autorizada | Nadie; se abre otro intervalo | Centro, unidad y residente | Sí; inmutable por intervalo | Sí | `ADM-03` a `ADM-06` | Obligatoriedad por centro |
| Plaza/cama durante el intervalo | `resident_location.place_id` | ID opaco, referencia a plaza/cama | Opcional | `null` | Plazas/camas compatibles | Administración; Enfermería solo en alta autorizada | Nadie; se abre otro intervalo | Centro, unidad y residente | Sí; inmutable por intervalo | Sí | `ADM-03` a `ADM-06` | Obligatoriedad y restricción de ocupación |
| Inicio de vigencia de la ubicación | `resident_location.valid_from` | Fecha/hora | Obligatorio | Hora efectiva confirmada por servidor | No aplica | Servidor en alta/cambio autorizado | Nadie | Centro, unidad y residente | Sí; inmutable por intervalo | Sí | `ADM-05` y `ADM-06`; AGENTS 10 | Si Administración puede registrar cambios efectivos en pasado/futuro |
| Fin de vigencia de la ubicación | `resident_location.valid_until` | Fecha/hora | Opcional; `null` significa vigente | `null` | Posterior a `valid_from` | Servidor | Administración solo al efectuar el siguiente cambio; no edición libre | Centro, unidad y residente | Sí; cierre inmutable | Sí | `ADM-05` y `ADM-06`; AGENTS 10 | Tratamiento de correcciones de una fecha equivocada |
| Cuenta autora del cambio | `resident_location.changed_by_account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta autenticada resuelta por servidor | Cuentas activas autorizadas | Servidor | Nadie | Mismo ámbito del cambio | Sí; inmutable por intervalo | Sí | `ADM-06`; Matriz §10; AGENTS 5.2 y 10 | Ninguna funcional |
| Perfil autor del cambio | `resident_location.changed_by_profile` | Código de perfil | Obligatorio | Perfil activo resuelto por servidor | `ADMINISTRACION`; `ENFERMERIA` solo en el intervalo inicial de un alta autorizada | Servidor | Nadie | Mismo ámbito del cambio | Sí; inmutable por intervalo | Sí | `ADM-04` a `ADM-06`; Matriz §3 | Ninguna funcional |
| Fecha/hora de registro del cambio | `resident_location.changed_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor | Nadie | Mismo ámbito del cambio | Sí; inmutable por intervalo | Sí | `ADM-06`; AGENTS 10 | Ninguna funcional |
| Motivo del cambio de ubicación | `resident_location.change_reason` | Texto o código | Pendiente | Sin valor por defecto | **No definido por las fuentes** | Administración | Administración solo mediante rectificación trazable | Centro, unidad y residente | Sí; inmutable en el intervalo original | Sí | `ADM-06` exige auditoría del cambio | Obligatoriedad, catálogo y longitud |

## Ciclo de vida y versión basal

`PENDIENTE` pertenece al agregado del residente antes de que exista un basal firmado; no es una versión clínica firmada. Un residente puede tener como máximo un borrador basal activo. El ciclo firmado es `BORRADOR -> FIRMADO_VIGENTE -> HISTORICO`; un borrador también puede pasar a `CANCELADO` con motivo y auditoría. Una versión firmada es inmutable. Toda rectificación exige `BASELINE_REEVALUATE`, motivo y una nueva versión vinculada a la vigente; no edita la firmada. Las versiones ya históricas no originan otra vigente por rectificación: una futura anotación aclaratoria será inmutable y queda fuera del primer bloque. Activar una nueva versión debe crear la vigente y archivar la anterior atómicamente, dejando como máximo una vigente por residente.

La inactivación administrativa del residente cancela el borrador abierto como consecuencia server-side de esa transición, bloquea nuevas escrituras y conserva la lectura autorizada y todo el historial. Administración no recibe por ello permiso clínico para cancelar borradores por separado.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador de versión basal | `baseline_version.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Enfermería o Medicina con el permiso correspondiente | Nadie edita el ID | Centro, unidad y residente del mismo grant | Sí; identifica una versión inmutable al firmar | Sí | PRD `BAS-05` a `BAS-07`; `ENF-23` y `ENF-25`; AGENTS 12 | Ninguna funcional |
| Residente del basal | `baseline_version.resident_id` | ID opaco, referencia a residente | Obligatorio | Recurso autorizado | Residentes del ámbito del perfil activo | Enfermería o Medicina con permiso | Nadie lo cambia | Centro, unidad y residente | Sí; inmutable por versión | Sí | PRD `BAS-05` a `BAS-07`; Matriz §§3 y 9 | Ninguna funcional |
| Número de versión | `baseline_version.version_number` | Entero positivo seguro | Obligatorio | `1` o anterior + 1, calculado por servidor | Enteros positivos sin reutilización por residente | Servidor | Nadie | Residente | Sí; inmutable | Sí | `ENF-23` y `ENF-25`; AGENTS 12 | Mecanismo D1 de asignación concurrente |
| Estado de versión | `baseline_version.status` | Código | Obligatorio | `BORRADOR` | `BORRADOR`, `CANCELADO`, `FIRMADO_VIGENTE`, `HISTORICO` | Servidor al crear borrador | Servidor solo mediante transición válida | Centro, unidad y residente | Sí; conserva todos los estados relevantes | Sí | Decisión de producto 2026-09-05; PRD 7.2 y `BAS-05`; `ENF-23`; AGENTS 6, 10 y 12 | Mecanismo físico para una sola fila `BORRADOR` activa y una sola `FIRMADO_VIGENTE` por residente |
| Motivo funcional de evaluación | `baseline_version.reason_code` | Código | Obligatorio al firmar | `ALTA` para el primer basal; sin valor automático en reevaluación | `ALTA`, `REVISION_PROGRAMADA`, `CAMBIO_FUNCIONAL_CONSOLIDADO` | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad y residente | Sí; inmutable al firmar | Sí | PRD `BAS-06`; `ENF-20` y `ENF-23` | Si `ALTA` puede utilizarse después de un alta administrativa reabierta |
| Fuente común de información | `baseline_version.common_information_source_code` | Selección única | Obligatorio al firmar | `null` en borrador | `VALORACION_DIRECTA`, `HISTORIA_O_INFORME_CLINICO`, `PERSONAL_DEL_CENTRO`, `FAMILIAR_O_CUIDADOR`, `FUENTES_COMBINADAS`, `OTRA`, `NO_DOCUMENTADO` | Profesional creador o profesional autorizado que aporta | Profesional autorizado mientras sea borrador; cada cambio conserva autoría | Centro, unidad y residente | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-20` y `ENF-23` | Ninguna funcional |
| Descripción de otra fuente común | `baseline_version.common_information_source_other_text` | Texto libre condicional | Obligatorio si la fuente común es `OTRA` | `null` | Texto breve sin identificar innecesariamente a terceros | Profesional autorizado | Profesional autorizado mientras sea borrador | Centro, unidad y residente | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; AGENTS 11 | Longitud definitiva aplazable |
| Fecha común de la información | `baseline_version.common_information_date` | Fecha | Obligatorio al firmar | `null` en borrador | Fecha documentada válida | Profesional creador o profesional autorizado que aporta | Profesional autorizado mientras sea borrador; cada cambio conserva autoría | Centro, unidad y residente | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-20` y `ENF-23` | Política de fechas parciales puede aplazarse; el primer bloque usa fecha completa |
| Versión firmada rectificada | `baseline_version.rectifies_version_id` | ID opaco, referencia a versión | Condicional si la nueva versión es una rectificación | `null` | Versión `FIRMADO_VIGENTE` del mismo residente al iniciar la rectificación | Servidor tras validar `BASELINE_REEVALUATE` | Nadie | Mismo residente y ámbito | Sí; inmutable | Sí | Decisión de producto 2026-09-05; `ENF-25`; AGENTS 10 | Ninguna funcional; no apunta a una versión que ya era histórica al iniciar la operación |
| Motivo de rectificación | `baseline_version.rectification_reason` | Texto | Condicional si existe rectificación | Sin valor por defecto | Texto obligatorio | Profesional con `BASELINE_REEVALUATE` que crea la nueva versión | No se modifica tras firma | Mismo residente y permiso basal; sin ventana de seis horas | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-25`; AGENTS 10 | Longitud definitiva aplazable |
| Cuenta creadora del borrador | `baseline_version.created_by_account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta autenticada resuelta por servidor | Cuentas activas autorizadas | Servidor | Nadie | Centro, unidad, residente y perfil del grant | Sí; inmutable | Sí | Matriz §§9 y 10; AGENTS 5.2 y 10 | Ninguna funcional |
| Perfil creador del borrador | `baseline_version.created_by_profile` | Código de perfil | Obligatorio | Perfil activo resuelto por servidor | `ENFERMERIA`, `MEDICINA` | Servidor | Nadie | Mismo grant que autoriza crear/completar | Sí; inmutable | Sí | PRD `BAS-07`; Matriz §3; AGENTS 5.2 | Ninguna funcional |
| Centro de autoría de creación | `baseline_version.created_in_center_id` | ID opaco, referencia a centro | Obligatorio | Centro del grant autorizado | Centro del residente y del perfil activo | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | Matriz §9; AGENTS 5.2 y 11 | Ninguna funcional |
| Unidad de autoría de creación | `baseline_version.created_in_unit_id` | ID opaco, referencia a unidad | Obligatorio | Unidad del grant autorizado | Unidad del residente dentro del centro | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | Matriz §9; AGENTS 5.2 y 11 | Cómo firmar durante un traslado de unidad |
| Fecha/hora de creación | `baseline_version.created_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | `ENF-23`; AGENTS 5.2 y 10 | Ninguna funcional |
| Cuenta de última edición del borrador | `baseline_version.updated_by_account_id` | ID opaco, referencia a cuenta | Obligatorio mientras exista borrador | Igual a creador al inicio | Cuentas profesionales autorizadas | Servidor | Servidor tras cada edición o aportación autorizada | Centro y unidad autorizados para el residente | Sí; cada aportación conserva además autoría individual | Sí | Decisión de producto 2026-09-05; `ENF-23`; Matriz §10; AGENTS 5.2 y 10 | Ninguna funcional; la fila es una proyección de última actividad, no sustituye la auditoría de aportaciones |
| Perfil de última edición del borrador | `baseline_version.updated_by_profile` | Código de perfil | Obligatorio mientras exista borrador | Igual a perfil creador | `ENFERMERIA`, `MEDICINA` con permiso correspondiente | Servidor | Servidor tras cada edición o aportación autorizada | Perfil activo del grant que autoriza la aportación | Sí; cada aportación conserva además el perfil individual | Sí | Decisión de producto 2026-09-05; PRD `BAS-07`; AGENTS 5.2 | Ninguna funcional; nunca transfiere la titularidad de la firma |
| Fecha/hora de última edición | `baseline_version.updated_at` | Fecha/hora | Obligatorio mientras exista borrador | Igual a `created_at` | No aplica | Servidor | Servidor tras cada edición autorizada | Centro, unidad y residente | Sí; se conserva en auditoría | Sí | Matriz §10; AGENTS 10 | Retención detallada de revisiones de borrador |
| Revisión de concurrencia del borrador | `baseline_version.draft_revision` | Entero positivo | Obligatorio en borrador | `1` | Entero incremental | Servidor | Servidor tras cada edición aceptada | Centro, unidad y residente | No es versión clínica; control técnico | Sí si se rechaza o acepta escritura | Wireframe Enfermería, control de edición; AGENTS 12 y 16 | Estrategia D1 exacta de concurrencia optimista |
| Cuenta firmante | `baseline_version.signed_by_account_id` | ID opaco, referencia a cuenta | Obligatorio al firmar | `null` en borrador | Exactamente `created_by_account_id`, si la cuenta sigue activa y autorizada | Servidor al firmar | Nadie | Centro, unidad, residente, perfil y permiso vigentes | Sí; inmutable | Sí | Decisión de producto 2026-09-05; `ENF-23`; Matriz §§3, 9 y 10; AGENTS 6 | Ninguna funcional; la firma no se transfiere |
| Perfil firmante | `baseline_version.signed_by_profile` | Código de perfil | Obligatorio al firmar | `null` en borrador | Exactamente `created_by_profile`, activo y autorizado | Servidor al firmar | Nadie | Perfil activo explícito del mismo grant de creación | Sí; inmutable | Sí | Decisión de producto 2026-09-05; PRD `BAS-07`; `ENF-23`; AGENTS 5.2 | Comportamiento si el creador conserva cuenta activa pero pierde el grant antes de firmar |
| Fecha/hora de firma y activación | `baseline_version.signed_at` | Fecha/hora | Obligatorio al firmar | `null` | Hora del servidor, no anterior a creación | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | `ENF-23`; AGENTS 6, 10 y 12 | Ninguna funcional |
| Inicio del periodo de vigencia | `baseline_version.valid_from` | Fecha/hora | Obligatorio para versión firmada | Igual a activación salvo regla aprobada | No aplica | Servidor | Nadie | Residente | Sí; inmutable | Sí | `ENF-23` | Si se permiten basales con vigencia retroactiva |
| Fin del periodo de vigencia | `baseline_version.valid_until` | Fecha/hora | Opcional; `null` solo para vigente | `null` al activar | Posterior a `valid_from` | Servidor | Servidor al sustituir por nueva versión | Residente | Sí; inmutable después de archivar | Sí | PRD `BAS-05`; `ENF-23` | Ninguna funcional |
| Versión que sustituye | `baseline_version.superseded_by_version_id` | ID opaco, referencia a versión | Obligatorio para histórica | `null` hasta sustitución | Versión firmada posterior del mismo residente | Servidor | Nadie | Mismo residente | Sí; inmutable | Sí | PRD `BAS-05`; `ENF-23`; AGENTS 12 | Ninguna funcional |
| Clave idempotente de firma | `baseline_version.activation_operation_id` | ID opaco de operación | Obligatorio al firmar | Generado o validado en límite server-side | Único por operación autorizada; no es ID de recurso | Servidor | Nadie | Cuenta, perfil, centro, unidad y residente | No; inmutable | Sí | AGENTS 12 y 16 | Retención y representación física sin conservar secretos del cliente |
| Fecha/hora de cancelación | `baseline_version.cancelled_at` | Fecha/hora | Condicional si estado `CANCELADO` | `null` | Hora del servidor | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | Decisión de producto 2026-09-05 | Ninguna funcional |
| Cuenta causal de cancelación | `baseline_version.cancelled_by_account_id` | ID opaco, referencia a cuenta | Condicional si estado `CANCELADO` | `null` | Creador; responsable clínico autorizado; o cuenta administrativa que inactiva al residente | Servidor | Nadie | Centro, unidad y residente | Sí; inmutable | Sí | Decisión de producto 2026-09-05 | Ninguna funcional |
| Perfil causal de cancelación | `baseline_version.cancelled_by_profile` | Código de perfil | Condicional si estado `CANCELADO` | `null` | `ENFERMERIA`, `MEDICINA`; `ADMINISTRACION` solo como causa de la inactivación, no como permiso clínico de cancelación directa | Servidor | Nadie | Perfil activo y operación autorizada | Sí; inmutable | Sí | Decisión de producto 2026-09-05 | Ninguna funcional |
| Motivo de cancelación | `baseline_version.cancellation_reason` | Texto | Condicional si estado `CANCELADO` | Sin valor por defecto | Texto obligatorio y minimizado; la inactivación referencia además su transición administrativa | Creador; servidor por inactivación autorizada; actor excepcional todavía bloqueado | Nadie tras cancelar | Centro, unidad y residente | Sí; inmutable | Sí | Decisión de producto 2026-09-05 | Longitud definitiva aplazable |

### Aportaciones al borrador basal

Otros profesionales de Enfermería o Medicina autorizados por centro y unidad pueden aportar información mientras el borrador está activo si poseen el permiso basal correspondiente: `BASELINE_INITIAL_COMPLETE` para un basal inicial o `BASELINE_REEVALUATE` para una reevaluación. Cada aportación es trazable e individual; no cambia `created_by_account_id`, no concede la firma y no permite combinar permisos de perfiles distintos.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador de aportación | `baseline_draft_contribution.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Servidor tras aportación autorizada | Nadie | Centro, unidad, residente y borrador activo | Append-only | Es parte de la auditoría del borrador | Decisión de producto 2026-09-05; AGENTS 5.2 y 10 | Ninguna funcional |
| Borrador basal | `baseline_draft_contribution.baseline_version_id` | ID opaco, referencia a versión | Obligatorio | Borrador autorizado | Único borrador activo del residente | Servidor | Nadie | Mismo residente y ámbito | Append-only | Sí | Decisión de producto 2026-09-05 | Ninguna funcional |
| Área y componente afectados | `baseline_draft_contribution.area_code`, `component_code` | Códigos | Obligatorio | Recurso editado | Catálogo basal v0.1 y sus componentes | Servidor desde la operación autorizada | Nadie | Mismo borrador | Append-only | Sí | Decisión de producto 2026-09-05 | Granularidad de auditoría para selecciones múltiples y texto |
| Cuenta autora de la aportación | `baseline_draft_contribution.account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta autenticada | Cuenta activa autorizada | Servidor | Nadie | Centro y unidad autorizados para el residente | Append-only | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 | Ninguna funcional |
| Perfil autor de la aportación | `baseline_draft_contribution.active_profile` | Código | Obligatorio | Perfil activo validado | `ENFERMERIA`, `MEDICINA` con `BASELINE_INITIAL_COMPLETE` o `BASELINE_REEVALUATE`, según el tipo de borrador | Servidor | Nadie | Un único grant por operación y acceso al residente | Append-only | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 | Ninguna funcional |
| Centro y unidad de autoría | `baseline_draft_contribution.center_id`, `unit_id` | Referencias opacas | Obligatorio | Grant autorizado | Centro y unidad vigentes/autorizados | Servidor | Nadie | Centro, unidad y residente | Append-only | Sí | Decisión de producto 2026-09-05 | Comportamiento durante traslado de unidad |
| Fecha/hora de aportación | `baseline_draft_contribution.contributed_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor | Nadie | Mismo borrador | Append-only | Sí | Decisión de producto 2026-09-05 | Ninguna funcional |
| Cambio aportado | `baseline_draft_contribution.change_payload` | Cambio estructurado o texto minimizado | Obligatorio | Valor nuevo de la operación | Solo campos del catálogo y texto permitido | Profesional autorizado; servidor valida | Nadie; una modificación posterior genera otra aportación | Mismo borrador | Append-only | Sí | Decisión de producto 2026-09-05; AGENTS 10 y 11 | Formato físico, conservación de valores anteriores y política de texto clínico en auditoría |

## Nueve áreas basales

Las áreas se modelan como entradas de una versión, no como columnas mutables del residente. Debe existir como máximo una entrada por combinación de versión y área. El catálogo común es `BASAL_AREAS_V0_1` y debe guardarse con cada entrada. Estas nueve áreas no son las diez categorías del registro cotidiano de cambios y no se permite calcular una puntuación conjunta: no constituyen una escala clínica validada.

Para firmar deben existir las nueve entradas y estar respondidos todos sus componentes obligatorios. `NO_DOCUMENTADO` cuenta como respuesta explícita, nunca como normalidad ni ausencia de alteraciones. Los componentes aplicables no pueden quedar vacíos al firmar: usan `NO_APLICA` solo donde el catálogo lo autoriza. Toda entrada forma parte de la versión completa, admite observación opcional y conserva autoría y motivo. La fuente y fecha comunes de la versión se aplican por defecto; un área puede declarar una fuente o fecha diferente sin repetirlas obligatoriamente nueve veces.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Versión basal de la entrada | `baseline_area_entry.baseline_version_id` | ID opaco, referencia a versión | Obligatorio | Versión borrador autorizada | Versiones del mismo residente | Enfermería o Medicina con permiso | Nadie cambia la referencia | Centro, unidad, residente y versión | Sí; forma parte de la versión | Sí | PRD `BAS-01`, `BAS-06`, `BAS-07`; `ENF-20` | Ninguna funcional |
| Área basal | `baseline_area_entry.area_code` | Código | Obligatorio al firmar; nueve entradas | Sin valor por defecto | `MOVILIDAD`, `ALIMENTACION`, `CONTINENCIA`, `ASEO_HIGIENE`, `COGNICION`, `COMUNICACION`, `CONDUCTA`, `SUENO`, `AYUDAS_HABITUALES` | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | PRD `BAS-01`; `ENF-20` | Ninguna funcional; revisar solo etiquetas visibles y accesibilidad |
| Versión del catálogo basal | `baseline_area_entry.catalog_version_code` | Código | Obligatorio al firmar | `BASAL_AREAS_V0_1` | `BASAL_AREAS_V0_1` para este contrato | Servidor | Servidor al crear la entrada; no se cambia después | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05 | Política de evolución del catálogo sin reescribir históricos |
| Observación del área | `baseline_area_entry.observation` | Texto libre | Opcional | `null` | Texto profesional; sin catálogo | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | `ENF-20`; AGENTS 11 | Longitud, formato y política de minimización |
| Fuente diferente para el área | `baseline_area_entry.information_source_override_code` | Selección única opcional | Opcional; `null` hereda la fuente común | `null` | `VALORACION_DIRECTA`, `HISTORIA_O_INFORME_CLINICO`, `PERSONAL_DEL_CENTRO`, `FAMILIAR_O_CUIDADOR`, `FUENTES_COMBINADAS`, `OTRA`, `NO_DOCUMENTADO` | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-20` y `ENF-23` | Ninguna funcional |
| Descripción de otra fuente del área | `baseline_area_entry.information_source_override_other_text` | Texto libre condicional | Obligatorio si la fuente diferente es `OTRA` | `null` | Texto breve sin identificar innecesariamente a terceros | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; AGENTS 11 | Longitud definitiva aplazable |
| Fecha diferente para el área | `baseline_area_entry.information_date_override` | Fecha opcional | Opcional; `null` hereda la fecha común | `null` | Fecha documentada válida | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-20` y `ENF-23` | Política de fechas parciales aplazable; el primer bloque usa fecha completa |
| Motivo de evaluación del área | `baseline_version.reason_code`, por relación | Código | Obligatorio al firmar para todas las áreas | Motivo de la versión | `ALTA`, `REVISION_PROGRAMADA`, `CAMBIO_FUNCIONAL_CONSOLIDADO` | Profesional creador del borrador | Profesional creador mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; PRD `BAS-06`; `ENF-20` | No se duplica físicamente por área salvo justificación; se hereda de la versión |
| Cuenta autora del valor vigente en el borrador | `baseline_area_entry.recorded_by_account_id` | ID opaco, referencia a cuenta | Obligatorio al firmar | Cuenta autenticada que registra o aporta el valor | Cuentas profesionales autorizadas | Servidor | Servidor tras una aportación autorizada | Centro y unidad autorizados para el residente | Sí; el historial de aportaciones conserva autores previos | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 y 10 | Ninguna funcional |
| Perfil autor del valor vigente en el borrador | `baseline_area_entry.recorded_by_profile` | Código | Obligatorio al firmar | Perfil activo validado | `ENFERMERIA`, `MEDICINA` con permiso basal | Servidor | Servidor tras una aportación autorizada | Un único grant por operación | Sí; el historial conserva perfiles previos | Sí | Decisión de producto 2026-09-05; AGENTS 5.2 | Ninguna funcional |
| Fecha/hora de registro del valor | `baseline_area_entry.recorded_at` | Fecha/hora | Obligatorio al firmar | Hora del servidor | No aplica | Servidor | Servidor tras una aportación autorizada | Centro, unidad, residente y versión | Sí; el historial conserva valores previos | Sí | Decisión de producto 2026-09-05; AGENTS 10 | Ninguna funcional |

Las tablas siguientes completan los componentes estructurados. Todos heredan versión, permisos, autoría, fuente, fecha, motivo, observación, versionado y auditoría de la entrada común.

### Movilidad

Se presenta como un único bloque, pero desplazamiento, ayuda técnica y transferencias se modelan por separado para admitir combinaciones como andador de cuatro ruedas y ayuda física de una persona.

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Modo habitual de desplazamiento | `baseline_mobility.displacement_mode_code` | Selección única | Sí | `DEAMBULA_INDEPENDIENTE_SIN_AYUDA`, `DEAMBULA_CON_AYUDA_TECNICA`, `DEAMBULA_CON_SUPERVISION`, `DEAMBULA_CON_AYUDA_FISICA_1_PERSONA`, `DEAMBULA_CON_AYUDA_FISICA_2_PERSONAS`, `SILLA_RUEDAS_AUTOPROPULSADA`, `SILLA_RUEDAS_IMPULSADA_POR_OTRA_PERSONA`, `SIN_DESPLAZAMIENTO_FUNCIONAL`, `NO_DOCUMENTADO` |
| Ayuda técnica complementaria | `baseline_mobility.technical_aid_code` | Selección única | Sí; puede quedar vacía solo en borrador | `NINGUNA`, `BASTON`, `MULETA_O_MULETAS`, `ANDADOR_4_RUEDAS`, `ANDADOR_2_RUEDAS`, `ANDADOR_FIJO_SIN_RUEDAS`, `OTRA`, `NO_DOCUMENTADO`. `NINGUNA` expresa ausencia conocida; `NO_DOCUMENTADO`, información desconocida. `NO_APLICA` se rechaza. `OTRA` exige descripción. La ayuda seleccionada puede coexistir con supervisión o ayuda física. |
| Descripción de otra ayuda técnica | `baseline_mobility.technical_aid_other_text` | Texto libre condicional | Sí cuando `technical_aid_code = OTRA` | Texto breve obligatorio; no sustituye el código estable. |
| Transferencias | `baseline_mobility.transfer_code` | Selección única | Sí | `INDEPENDIENTE`, `SUPERVISION`, `AYUDA_1_PERSONA`, `AYUDA_2_PERSONAS`, `GRUA`, `NO_DOCUMENTADO` |

### Alimentación

La plataforma registra la textura prescrita o indicada. No prescribe, recomienda ni transforma automáticamente texturas o consistencias. «Triturada» significa alimento fragmentado con partículas perceptibles; «Puré», textura homogénea sin grumos ni separación apreciable de líquido.

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Vía de alimentación | `baseline_feeding.route_code` | Selección única | Sí | `ORAL`, `ENTERAL`, `MIXTA`, `NO_DOCUMENTADO` |
| Textura habitual de alimentos | `baseline_feeding.food_texture_code` | Selección única | Sí; puede quedar vacía solo en borrador | `NORMAL`, `TROCEADA`, `TRITURADA`, `PURE`, `OTRA_TEXTURA_ADAPTADA`, `NO_APLICA`, `NO_DOCUMENTADO`. `NO_APLICA` solo es válido con vía `ENTERAL`; con `ORAL` o `MIXTA` debe registrarse una textura o `NO_DOCUMENTADO`. |
| Detalle de otra textura adaptada | `baseline_feeding.food_texture_other_text` | Texto libre condicional | Obligatorio para `OTRA_TEXTURA_ADAPTADA` | Descripción breve obligatoria; no sustituye el código estable. |
| Consistencia habitual de líquidos | `baseline_feeding.liquid_consistency_code` | Selección única | Sí; puede quedar vacía solo en borrador | `IDDSI_0_FINO_SIN_ESPESAR`, `IDDSI_1_LIGERAMENTE_ESPESO`, `IDDSI_2_POCO_ESPESO`, `IDDSI_3_MODERADAMENTE_ESPESO`, `IDDSI_4_EXTREMADAMENTE_ESPESO`, `NO_APLICA`, `NO_DOCUMENTADO`. `NO_APLICA` solo es válido con vía `ENTERAL`. `NECTAR`, `MIEL` y `PUDIN` no son códigos principales; futuros sinónimos locales serán solo informativos y exigirán correspondencia IDDSI validada por el centro. |
| Ayuda habitual para alimentarse | `baseline_feeding.assistance_code` | Selección única | Sí | `INDEPENDIENTE`, `PREPARAR_O_CORTAR_ALIMENTOS`, `SUPERVISION_O_INDICACIONES`, `AYUDA_FISICA_PARCIAL`, `AYUDA_TOTAL`, `NO_DOCUMENTADO` |
| Precauciones de deglución | `baseline_feeding.swallowing_precautions_code` | Selección única | Sí | `NINGUNA_DOCUMENTADA`, `PRECAUCIONES_DOCUMENTADAS`, `NO_DOCUMENTADO` |
| Descripción de precauciones | `baseline_feeding.swallowing_precautions_text` | Texto libre condicional | Sí cuando se selecciona `PRECAUCIONES_DOCUMENTADAS` | Texto obligatorio y minimizado. |

### Continencia

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Micción | `baseline_continence.urination_code` | Selección única | Sí | `CONTINENTE`, `INCONTINENCIA_OCASIONAL`, `INCONTINENCIA_HABITUAL`, `NO_DOCUMENTADO` |
| Deposición | `baseline_continence.bowel_code` | Selección única | Sí | `CONTINENTE`, `INCONTINENCIA_OCASIONAL`, `INCONTINENCIA_HABITUAL`, `NO_DOCUMENTADO` |
| Manejo o dispositivo | `baseline_continence.management_codes` | Selección múltiple | Sí; al menos una opción | `NINGUNO`, `ABSORBENTE`, `SONDA_URINARIA`, `UROSTOMIA`, `COLOSTOMIA_ILEOSTOMIA`, `OTRO`, `NO_DOCUMENTADO`. `NINGUNO` y `NO_DOCUMENTADO` son mutuamente excluyentes y excluyen todas las demás opciones. |
| Descripción de otro manejo o dispositivo | `baseline_continence.management_other_text` | Texto libre condicional | Sí cuando se selecciona `OTRO` | Texto breve obligatorio. |

### Aseo e higiene

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Ayuda habitual para aseo personal | `baseline_hygiene.personal_care_assistance_code` | Selección única | Sí | `INDEPENDIENTE`, `SUPERVISION_O_INDICACIONES`, `AYUDA_PARCIAL`, `AYUDA_TOTAL`, `NO_DOCUMENTADO` |
| Baño o ducha | `baseline_hygiene.bathing_assistance_code` | Selección única | Sí | `INDEPENDIENTE`, `SUPERVISION`, `AYUDA_PARCIAL`, `AYUDA_TOTAL`, `NO_DOCUMENTADO` |

### Cognición

La categoría registra una situación documentada y no equivale a una evaluación cognitiva formal normal ni genera diagnósticos. En particular, `SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO` no afirma normalidad formal. GDS 1–7 puede registrarse cuando consta documentado, con independencia de la categoría cognitiva; la etiología continúa limitada a `DEMENCIA_DOCUMENTADA`. La publicación original de Reisberg y colaboradores describe GDS como una escala para delimitar estadios de deterioro ([PubMed PMID 7114305](https://pubmed.ncbi.nlm.nih.gov/7114305/)).

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Situación cognitiva | `baseline_cognition.category_code` | Selección única | Sí | `SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO`, `DETERIORO_COGNITIVO_LEVE_DOCUMENTADO`, `DEMENCIA_DOCUMENTADA`, `SITUACION_NO_DETERMINADA` |
| Etiología documentada | `baseline_cognition.etiology_code` | Selección única condicional | No; solo si `category_code = DEMENCIA_DOCUMENTADA` y consta | `ENFERMEDAD_ALZHEIMER`, `DEMENCIA_VASCULAR`, `DEMENCIA_MIXTA`, `DEMENCIA_CON_CUERPOS_DE_LEWY`, `DEMENCIA_FRONTOTEMPORAL`, `DEMENCIA_ASOCIADA_ENFERMEDAD_PARKINSON`, `SINDROME_CORTICOBASAL`, `OTRA`, `ETIOLOGIA_NO_ESPECIFICADA` |
| Otra etiología | `baseline_cognition.etiology_other_text` | Texto libre condicional | Sí cuando `etiology_code = OTRA` | Texto breve obligatorio. |
| GDS documentado | `baseline_cognition.gds_code` | Selección única opcional | Opcional para cualquier categoría cognitiva | `NO_DOCUMENTADO`, `GDS_1`, `GDS_2`, `GDS_3`, `GDS_4`, `GDS_5`, `GDS_6`, `GDS_7`. Los grados 1–7 se admiten cuando constan documentados y no se infieren de la categoría. |
| Fuente clínica de etiología/GDS | `baseline_cognition.clinical_reference_source_code` | Selección única condicional | Sí cuando se documenta etiología o GDS 1–7 | `VALORACION_DIRECTA`, `HISTORIA_O_INFORME_CLINICO`, `PERSONAL_DEL_CENTRO`, `FAMILIAR_O_CUIDADOR`, `FUENTES_COMBINADAS`, `OTRA`, `NO_DOCUMENTADO`. Para un dato clínico documentado no puede usarse `NO_DOCUMENTADO`. |
| Descripción de otra fuente clínica | `baseline_cognition.clinical_reference_source_other_text` | Texto libre condicional | Obligatorio si la fuente clínica es `OTRA` | Texto breve sin identificar innecesariamente a terceros. |
| Fecha de la fuente clínica | `baseline_cognition.clinical_reference_date` | Fecha condicional | Sí cuando se documenta etiología o GDS 1–7 | Fecha documentada válida; el primer bloque usa fecha completa. |

### Comunicación

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Comprensión | `baseline_communication.comprehension_code` | Selección única | Sí | `COMPRENSION_FUNCIONAL`, `NECESITA_FRASES_SENCILLAS_REPETICION_O_APOYO`, `COMPRENSION_MUY_LIMITADA`, `NO_SE_HA_PODIDO_DETERMINAR`, `NO_DOCUMENTADO` |
| Expresión | `baseline_communication.expression_code` | Selección única | Sí | `EXPRESA_NECESIDADES_EFICAZMENTE`, `EXPRESION_VERBAL_LIMITADA_PERO_COMUNICA_NECESIDADES_BASICAS`, `COMUNICACION_PRINCIPALMENTE_NO_VERBAL`, `NO_EXPRESA_NECESIDADES_DE_FORMA_FIABLE`, `NO_DOCUMENTADO` |
| Forma habitual de comunicación | `baseline_communication.usual_forms_codes` | Selección múltiple | Sí; al menos una opción al firmar | `LENGUAJE_ORAL`, `GESTOS`, `ESCRITURA`, `TABLERO_O_DISPOSITIVO`, `OTRA`, `NO_SE_IDENTIFICA_FORMA_EFECTIVA`, `NO_DOCUMENTADO`. Las dos últimas son mutuamente excluyentes y excluyen todas las demás opciones. |
| Otra forma habitual | `baseline_communication.usual_form_other_text` | Texto libre condicional | Sí cuando se selecciona `OTRA` | Texto breve obligatorio. |

### Conducta

No se utilizan etiquetas estigmatizantes ni se derivan diagnósticos automáticamente.

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Situación basal | `baseline_behavior.status_code` | Selección única | Sí | `SIN_CONDUCTAS_RELEVANTES_CONOCIDAS`, `PATRONES_CONDUCTUALES_HABITUALES`, `NO_DOCUMENTADO` |
| Patrones habituales | `baseline_behavior.pattern_codes` | Selección múltiple condicional | Sí, al menos uno, cuando `status_code = PATRONES_CONDUCTUALES_HABITUALES`; vacío en los otros estados | `APATIA_O_RETRAIMIENTO`, `ANIMO_BAJO_HABITUAL`, `ANSIEDAD_O_TEMOR`, `IRRITABILIDAD`, `AGITACION_O_INQUIETUD`, `RESISTENCIA_A_LOS_CUIDADOS`, `CONDUCTAS_O_VOCALIZACIONES_REPETITIVAS`, `DEAMBULACION_ERRATICA_O_INTENTO_DE_SALIDA`, `AGRESIVIDAD_VERBAL`, `AGRESIVIDAD_FISICA`, `DESINHIBICION`, `IDEAS_DELIRANTES_O_ALUCINACIONES_DOCUMENTADAS`, `OTRA` |
| Otro patrón habitual | `baseline_behavior.pattern_other_text` | Texto libre condicional | Sí cuando se selecciona `OTRA` | Texto breve obligatorio. |
| Desencadenantes conocidos | `baseline_behavior.known_triggers_observation` | Texto libre | Opcional | Observación profesional minimizada. |
| Estrategias que habitualmente ayudan | `baseline_behavior.usually_helpful_strategies_observation` | Texto libre | Opcional | Observación descriptiva; no es recomendación automática. |

### Sueño

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Patrón habitual de sueño | `baseline_sleep.pattern_codes` | Selección múltiple | Sí; al menos una opción | `PATRON_HABITUALMENTE_CONSERVADO`, `DIFICULTAD_INICIO_SUENO`, `DESPERTARES_FRECUENTES`, `DESPERTAR_PRECOZ`, `INVERSION_SUENO_VIGILIA`, `SOMNOLENCIA_DIURNA_HABITUAL`, `PATRON_IRREGULAR_VARIABLE`, `NO_DOCUMENTADO`. Las alteraciones pueden combinarse entre sí; `PATRON_HABITUALMENTE_CONSERVADO` y `NO_DOCUMENTADO` son mutuamente excluyentes y excluyen todas las alteraciones. |

### Ayudas habituales

Las ayudas de movilidad se excluyen de esta área porque su fuente de verdad es Movilidad.

| Campo | Nombre técnico propuesto | Tipo de entrada | Obligatorio al firmar | Catálogo y reglas |
| --- | --- | --- | --- | --- |
| Ayudas habituales no relacionadas con movilidad | `baseline_usual_aids.aid_codes` | Selección múltiple | Sí; al menos una opción | Sensoriales/comunicación: `GAFAS`, `AUDIFONO`, `TABLERO_O_DISPOSITIVO_COMUNICACION`. Alimentación/autocuidado: `PROTESIS_DENTAL`, `CUBIERTOS_O_VAJILLA_ADAPTADOS`, `OTRO_PRODUCTO_DE_APOYO`. Otros soportes: `OXIGENOTERAPIA_HABITUAL`, `CPAP_BIPAP`, `OTRO`, `NINGUNO`, `NO_DOCUMENTADO`. `NINGUNO` y `NO_DOCUMENTADO` son mutuamente excluyentes y excluyen el resto. |
| Descripción de otro producto de apoyo | `baseline_usual_aids.other_support_product_text` | Texto libre condicional | Obligatorio cuando se selecciona `OTRO_PRODUCTO_DE_APOYO` | Descripción breve obligatoria. |
| Descripción de otro soporte | `baseline_usual_aids.other_support_text` | Texto libre condicional | Obligatorio cuando se selecciona `OTRO` | Descripción breve obligatoria. |

## Índice de Barthel

Se adopta un único catálogo común para todos los centros: `BARTHEL_COMUN_V0_1`. No se permite guardar únicamente una puntuación total. Deben conservarse los diez ítems, su opción única, la puntuación correspondiente, la versión del catálogo y el total automático sobre 100. El total no es una entrada manual.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Versión basal de Barthel | `barthel_assessment.baseline_version_id` | ID opaco, referencia a versión | Obligatorio | Versión borrador autorizada | Versiones del mismo residente | Enfermería o Medicina con permiso | Nadie cambia la referencia | Centro, unidad, residente y versión | Sí; forma parte de la versión | Sí | PRD `BAS-02`, `BAS-03`, `BAS-06`, `BAS-07`; `ENF-21` | Ninguna funcional |
| Versión del instrumento | `barthel_assessment.instrument_version_code` | Código | Obligatorio al firmar | `BARTHEL_COMUN_V0_1` | `BARTHEL_COMUN_V0_1` para todos los centros | Servidor | Servidor para evaluaciones nuevas si una futura versión es aprobada; no reescribe históricas | Centro y versión basal | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; `ENF-21`; PRD §14 | Gobernanza de futuras versiones |
| Ítem de Barthel | `barthel_item.item_code` | Código | Obligatorio; diez entradas por evaluación firmada | Sin valor por defecto | `COMER`, `LAVARSE`, `VESTIRSE`, `ARREGLARSE`, `DEPOSICION`, `MICCION`, `USO_RETRETE`, `TRASLADO_CAMA_SILLON`, `DEAMBULACION`, `ESCALERAS` | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; PRD `BAS-03`; `ENF-21` | Ninguna funcional |
| Opción seleccionada del ítem | `barthel_item.selected_option_code` | Selección única por ítem | Obligatorio al firmar | `null` en borrador | Catálogo `BARTHEL_COMUN_V0_1` detallado debajo | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; PRD `BAS-03`; `ENF-21` | Ninguna funcional |
| Puntuación del ítem | `barthel_item.awarded_score` | Entero derivado y guardado | Obligatorio al firmar | Derivada de opción + versión | Solo la puntuación asociada a la opción de `BARTHEL_COMUN_V0_1`; no editable libremente | Servidor | Servidor al cambiar la opción en borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | Decisión de producto 2026-09-05; PRD `BAS-03`; `ENF-21` | Ninguna funcional |
| Total de Barthel | `barthel_assessment.total_score` | Entero automático guardado | Obligatorio al firmar | Suma calculada por servidor | `0` a `100`; debe coincidir con la suma de los diez ítems | Servidor | Servidor al cambiar ítems en borrador | Centro, unidad, residente y versión | Sí; reproducible desde los ítems | Sí | Decisión de producto 2026-09-05; PRD `BAS-03`; `ENF-21` | Ninguna funcional |
| Fecha efectiva de la evaluación Barthel | `barthel_assessment.assessment_date` | Fecha | Obligatorio al firmar | Fecha de evaluación confirmada | Fecha válida; no se infiere del cliente sin validación | Enfermería o Medicina con permiso | Enfermería o Medicina con permiso mientras sea borrador | Centro, unidad, residente y versión | Sí; inmutable al firmar | Sí | `ENF-18`, `ENF-20`, `ENF-23`; AGENTS 10 | Si puede diferir de la fecha de firma y cuánto |

### Catálogo `BARTHEL_COMUN_V0_1`

| Ítem | Código de opción | Texto funcional | Puntuación |
| --- | --- | --- | --- |
| Comer | `INDEPENDIENTE` | Independiente | 10 |
| Comer | `AYUDA_PREPARAR_O_CORTAR` | Necesita ayuda para preparar o cortar | 5 |
| Comer | `DEPENDIENTE` | Dependiente | 0 |
| Lavarse | `SOLO_COMPLETO` | Se ducha o baña completamente solo | 5 |
| Lavarse | `NECESITA_AYUDA` | Necesita ayuda | 0 |
| Vestirse | `INDEPENDIENTE` | Independiente | 10 |
| Vestirse | `AYUDA_REALIZA_AL_MENOS_MITAD` | Necesita ayuda, pero realiza al menos la mitad | 5 |
| Vestirse | `DEPENDIENTE` | Dependiente | 0 |
| Arreglarse | `INDEPENDIENTE_HIGIENE_PERSONAL_BASICA` | Independiente en higiene personal básica | 5 |
| Arreglarse | `NECESITA_AYUDA` | Necesita ayuda | 0 |
| Deposición | `CONTINENTE` | Continente | 10 |
| Deposición | `INCONTINENCIA_OCASIONAL_O_AYUDA_ENEMAS_SUPOSITORIOS` | Incontinencia ocasional o ayuda con enemas/supositorios | 5 |
| Deposición | `INCONTINENTE` | Incontinente | 0 |
| Micción | `CONTINENTE` | Continente | 10 |
| Micción | `MAX_UN_EPISODIO_24H_O_AYUDA_SONDA_COLECTOR` | Máximo un episodio en 24 horas o ayuda con sonda/colector | 5 |
| Micción | `INCONTINENTE` | Incontinente | 0 |
| Uso del retrete | `INDEPENDIENTE` | Independiente | 10 |
| Uso del retrete | `PEQUENA_AYUDA_ROPA_O_TRANSFERENCIA_SE_LIMPIA_SOLO` | Pequeña ayuda con ropa o transferencia, pero se limpia solo | 5 |
| Uso del retrete | `DEPENDIENTE` | Dependiente | 0 |
| Traslado cama-sillón | `INDEPENDIENTE` | Independiente | 15 |
| Traslado cama-sillón | `SUPERVISION_O_MINIMA_AYUDA` | Supervisión o mínima ayuda | 10 |
| Traslado cama-sillón | `GRAN_AYUDA_MANTIENE_SEDESTACION` | Gran ayuda, pero mantiene sedestación | 5 |
| Traslado cama-sillón | `DEPENDIENTE_GRUA_O_DOS_PERSONAS` | Dependiente, grúa o dos personas | 0 |
| Deambulación | `CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA` | Camina al menos 50 metros independientemente, con ayuda técnica si precisa | 15 |
| Deambulación | `NECESITA_AYUDA_O_SUPERVISION` | Necesita ayuda o supervisión | 10 |
| Deambulación | `INDEPENDIENTE_EN_SILLA_RUEDAS` | Independiente en silla de ruedas | 5 |
| Deambulación | `DEPENDIENTE` | Dependiente | 0 |
| Escaleras | `SUBE_BAJA_UN_PISO_SOLO` | Sube y baja un piso solo | 10 |
| Escaleras | `NECESITA_AYUDA_O_SUPERVISION` | Necesita ayuda o supervisión | 5 |
| Escaleras | `DEPENDIENTE` | Dependiente | 0 |

## Exclusión de CFS

CFS queda eliminada completamente de este contrato y del futuro producto estructurado por la línea base v1. No existe entidad, campo, opción, permiso, activo, versión, idioma ni referencia de licencia CFS en el contrato objetivo. Sus menciones en documentos anteriores se conservan únicamente como evidencia histórica de una contradicción ya resuelta, nunca como alcance de implementación.

## Tipos de entrada, exclusión y coherencia

| Tipo | Campos |
| --- | --- |
| Selección única | Movilidad: desplazamiento, ayuda técnica complementaria y transferencias. Alimentación: vía, textura, consistencia de líquidos, ayuda y precauciones. Continencia: micción y deposición. Aseo/higiene: aseo personal y baño/ducha. Cognición: situación, etiología cuando proceda y GDS documentado cuando conste. Comunicación: comprensión y expresión. Conducta: situación basal. Fuente común del basal y fuente clínica específica cuando proceda. Barthel: una opción por cada uno de los diez ítems. |
| Selección múltiple | Continencia: manejo/dispositivo. Comunicación: formas habituales. Conducta: patrones habituales. Sueño: patrón habitual y alteraciones. Ayudas habituales: soportes no relacionados con movilidad. |
| Condicional | Textos de `OTRA`/`OTRO`; aplicabilidad de textura y consistencia según vía; descripción de precauciones; patrones de conducta; etiología; fuente clínica y fecha para etiología/GDS; fuente o fecha distinta por área; campos de cancelación y rectificación. |
| Texto libre | Observación opcional de cada área; descripción obligatoria de opciones `OTRA`/`OTRO`; precauciones de deglución; desencadenantes y estrategias habituales; motivos de rectificación y cancelación. |

Reglas de exclusión y coherencia:

- Las nueve áreas deben estar respondidas antes de firmar. `NO_DOCUMENTADO` es una respuesta explícita y nunca equivale a normalidad.
- En borrador, la ayuda técnica, la textura y la consistencia de líquidos pueden permanecer vacías. Para firmar deben contener un valor explícito. Ayuda técnica admite `NINGUNA` o `NO_DOCUMENTADO` pero rechaza `NO_APLICA`. Textura y líquidos admiten `NO_APLICA` únicamente con vía exclusivamente `ENTERAL`; con vía `MIXTA` exigen el valor oral o `NO_DOCUMENTADO`.
- En selecciones múltiples, `NINGUNO` y `NO_DOCUMENTADO` son excluyentes entre sí y respecto al resto cuando el catálogo los contiene.
- En formas de comunicación debe seleccionarse al menos una opción. `NO_SE_IDENTIFICA_FORMA_EFECTIVA` y `NO_DOCUMENTADO` son excluyentes entre sí y respecto a todas las demás formas.
- En Sueño, `PATRON_HABITUALMENTE_CONSERVADO` y `NO_DOCUMENTADO` excluyen todas las alteraciones; las alteraciones sí pueden coexistir entre ellas.
- En Conducta, `PATRONES_CONDUCTUALES_HABITUALES` exige al menos un patrón; los otros estados exigen que la lista quede vacía.
- Toda opción `OTRO` u `OTRA`, incluidas `OTRA_TEXTURA_ADAPTADA` y `OTRO_PRODUCTO_DE_APOYO`, exige una descripción breve que, tras recortar espacios, no quede vacía. Sin la opción correspondiente, el texto debe quedar vacío.
- El basal firmado exige fuente y fecha comunes. Cada área puede sustituir una o ambas por valores propios; los valores ausentes se heredan del basal. La fuente `OTRA` exige descripción breve sin identificar innecesariamente a terceros.
- Etiología solo se admite con `DEMENCIA_DOCUMENTADA`. GDS 1–7 puede registrarse con cualquier categoría cognitiva si está documentado. Fuente clínica específica y fecha son obligatorias cuando se documenta etiología o GDS; `NO_DOCUMENTADO` no es una fuente clínica válida en esos casos.
- Los niveles IDDSI son la codificación principal de líquidos. `NECTAR`, `MIEL` y `PUDIN` no se guardan como códigos clínicos principales.
- Las ayudas de movilidad solo se registran en Movilidad, aunque se proyecten para lectura en otros contextos.
- GDS frente a categoría cognitiva, Barthel y las áreas basales pueden generar advertencias de coherencia para revisión humana. El sistema no corrige valores, no bloquea la firma por una supuesta contradicción clínica y no toma decisiones automáticamente.
- No se calcula una puntuación combinada de las nueve áreas. El único total de este alcance es el Barthel automático, reproducible desde sus diez respuestas.

## Perfiles, ámbitos y permisos configurables

El perfil activo pertenece al contexto autenticado de cada petición y **no** se persiste como permiso global de la cuenta. Los grants se vinculan a un único perfil y centro. El acceso profesional se concede principalmente por centro y unidad; un ámbito por residente solo añade una restricción específica cuando la política lo exige y nunca sustituye la validación de la ubicación vigente. Los permisos deben colgar del mismo grant para impedir productos cartesianos y la combinación de perfiles distintos. No existen comodines implícitos.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador de asignación de perfil | `profile_scope.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Administración | Nadie edita el ID; revocación trazable | Centro administrado | No; inmutable | Sí | PRD `AUTH-05`, `ORG-02`; Matriz §§9 y 10; AGENTS 5.2 | Ninguna funcional |
| Cuenta asignada | `profile_scope.account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta administrada | Cuentas del centro | Administración | No se cambia; se revoca y crea otra asignación | Centro administrado | No; cambios mediante nuevos grants/revocación | Sí | PRD `AUTH-01`, `AUTH-05`; AGENTS 5.2 y 7.5 | Ninguna funcional |
| Perfil asignado | `profile_scope.profile_code` | Código | Obligatorio | Sin valor por defecto | `AUXILIAR`, `ENFERMERIA`, `MEDICINA`, `FAMILIAR`, `ADMINISTRACION`, `DIRECCION_CLINICA` | Administración | No se cambia; se revoca y crea otra asignación | Centro administrado | No; cambios mediante nuevos grants/revocación | Sí | PRD `AUTH-02`, `AUTH-05`; Matriz §9; AGENTS 5.2 | Ninguna funcional |
| Centro del perfil | `profile_scope.center_id` | ID opaco, referencia a centro | Obligatorio | Centro administrado | Centros autorizados para la Administración actuante | Administración | No se cambia | Centro administrado | No; inmutable | Sí | Matriz §9; AGENTS 5.1 y 5.2 | Si algún perfil puede abarcar varios centros, debe hacerlo con grants separados |
| Estado del grant de perfil | `profile_scope.status` | Código | Obligatorio | `ACTIVO` | `ACTIVO`, `REVOCADO` | Administración | Administración | Centro administrado | Sí, mediante auditoría de concesión/revocación | Sí | PRD `AUTH-03`; Matriz §10; AGENTS 5.3 | Si se necesita además suspensión temporal y sus efectos |
| Unidad autorizada del grant | `profile_unit_scope.unit_id` | ID opaco, referencia a unidad | Obligatorio para ámbitos operativos; cardinalidad múltiple | Sin concesión implícita | Unidades del mismo centro del grant | Administración | Administración mediante alta/revocación trazable | Centro administrado | Sí, mediante historial de grants | Sí | PRD `ORG-02`; Matriz §§9 y 11; AGENTS 5.1 | Reglas exactas de asignación por turno/tarea, fuera de este contrato basal |
| Residente autorizado del grant | `profile_resident_scope.resident_id` | ID opaco, referencia a residente | Opcional para profesionales; obligatorio para vínculos o restricciones individualizadas | Sin concesión individual implícita | Residentes cuya ubicación vigente pertenece al centro/unidad del grant; vínculo familiar además exige autorización activa | Administración | Administración mediante alta/revocación trazable | Mismo perfil, centro y unidad del grant | Sí, mediante historial de grants | Sí | Decisión de producto 2026-09-05; Matriz §§3, 9, 10 y 11; AGENTS 5.1 y 5.3 | Casos profesionales que requieren además restricción individual y actualización por traslado |
| Permiso configurable | `profile_permission.permission_code` | Código | Obligatorio por grant de permiso | Sin permiso por defecto | `RESIDENT_IDENTITY_CREATE`, `BASELINE_INITIAL_COMPLETE`, `BASELINE_REEVALUATE`, `BASELINE_DRAFT_CANCEL` reservado y no concedible todavía, `CLINICAL_DETAIL_READ` | Administración según configuración del centro y solo para códigos habilitados | Administración mediante concesión/revocación trazable | Mismo perfil, centro, unidades y residentes del grant | Sí, mediante historial de grants | Sí | PRD `RES-02`, `BAS-07`; Matriz §§3, 9, 10 y 11; AGENTS 5 y 7; decisión de producto 2026-09-05 | Qué profesionales concretos reciben cada permiso dentro de la configuración de cada centro |
| Fecha/hora de concesión | `profile_permission.granted_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor | Nadie | Centro y grant | Sí; inmutable | Sí | Matriz §10; AGENTS 5.2 y 10 | Ninguna funcional |
| Cuenta que concede | `profile_permission.granted_by_account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta administrativa autenticada | Cuentas de Administración autorizadas | Servidor | Nadie | Centro y grant | Sí; inmutable | Sí | AGENTS 5.2, 7.5 y 10 | Ninguna funcional |
| Fecha/hora de revocación | `profile_permission.revoked_at` | Fecha/hora | Opcional | `null` | Hora del servidor; efecto en la siguiente petición | Servidor | Nadie después de revocar | Centro y grant | Sí; inmutable | Sí | PRD `AUTH-03`; Matriz §10 | Ninguna funcional |
| Cuenta que revoca | `profile_permission.revoked_by_account_id` | ID opaco, referencia a cuenta | Condicional si se revoca | `null` | Cuentas de Administración autorizadas | Servidor | Nadie | Centro y grant | Sí; inmutable | Sí | Matriz §10; AGENTS 10 | Ninguna funcional |
| Motivo de revocación | `profile_permission.revocation_reason` | Texto | Pendiente | `null` | **No definido por las fuentes** | Administración | Nadie después de revocar | Centro y grant | Sí; inmutable | Sí | AGENTS 10 y 11 | Obligatoriedad, catálogo y longitud |
| Perfil activo de la petición | `request_context.active_profile` | Código no persistido como grant global | Obligatorio por petición | Selección explícita del usuario validada por servidor | Uno de los perfiles asignados y activos de esa cuenta | Usuario mediante cambio explícito; servidor valida | Usuario mediante cambio explícito; servidor valida | Un único grant por operación | No; se registra en cada escritura/acceso auditado | Sí en operaciones relevantes | PRD `AUTH-05`; Matriz §§9 y 10; AGENTS 5.2 | Mecanismo de sesión productivo, fuera de esta migración |

Reglas específicas de permisos basales:

- Enfermería o Medicina pueden aportar a un borrador ajeno si tienen acceso al residente y el permiso correspondiente al tipo de versión: `BASELINE_INITIAL_COMPLETE` para el basal inicial o `BASELINE_REEVALUATE` para una reevaluación.
- La firma corresponde exclusivamente a la cuenta creadora del borrador. Una aportación no transfiere ni delega ese derecho.
- La cuenta creadora puede cancelar su propio borrador. La cancelación por ausencia del creador permanece bloqueada y sin actor autorizado en esta entrega. Cualquier diseño futuro exigirá permiso explícito, motivo y auditoría; Administración no obtiene acceso clínico por esa operación.
- El inicio de rectificación permanece bloqueado en esta entrega. Una decisión futura deberá exigir permiso específico, acceso al residente y motivo obligatorio; la rectificación seguirá creando una nueva versión vinculada, nunca una edición.
- Una versión histórica no se convierte en objetivo de rectificación para crear otra vigente. Una futura anotación aclaratoria inmutable podrá complementar una versión histórica, pero queda fuera del primer bloque.
- La inactivación administrativa del residente exige motivo. Como consecuencia de dominio, el servidor cancela el borrador abierto, bloquea nuevas escrituras y preserva la lectura autorizada y el historial; esto no concede a Administración acceso al contenido ni permiso clínico de cancelación independiente.

## Auditoría de lectura clínica por Dirección

Dirección operativa no accede al detalle clínico. Cada lectura detallada autorizada desde Dirección Clínica es solo lectura, exige permiso específico y finalidad, y debe generar un registro append-only antes de entregar el contenido. La obligación de auditoría no puede quedar como una indicación opcional para el llamador.

| Nombre funcional | Nombre técnico | Tipo | Obligatorio u opcional | Valor inicial | Catálogo permitido | Perfil que puede crearlo | Perfil que puede modificarlo | Ámbito de autorización | ¿Se versiona? | ¿Exige auditoría? | Fuente funcional | Decisión pendiente |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identificador del acceso auditado | `clinical_access_audit.id` | ID opaco | Obligatorio | Generado por servidor | No aplica | Servidor tras autorizar a Dirección Clínica | Nadie | Centro, unidad, residente y recurso | Append-only | Es el registro de auditoría | PRD `DIR-03` y `DIR-04`; Matriz §§3, 9 y 10; AGENTS 5.1 y 7.6 | Ninguna funcional |
| Cuenta que accede | `clinical_access_audit.account_id` | ID opaco, referencia a cuenta | Obligatorio | Cuenta autenticada resuelta por servidor | Cuentas activas con grant de Dirección Clínica | Servidor | Nadie | Centro y grant de Dirección | Append-only | Sí | PRD `DIR-03`; Matriz §10; AGENTS 5.1 | Ninguna funcional |
| Perfil del acceso | `clinical_access_audit.active_profile` | Código | Obligatorio | `DIRECCION_CLINICA` resuelto por servidor | Solo `DIRECCION_CLINICA` | Servidor | Nadie | Grant específico sin combinar otros perfiles | Append-only | Sí | PRD `AUTH-05`, `DIR-03`; AGENTS 5.2 y 7.6 | Ninguna funcional |
| Centro accedido | `clinical_access_audit.center_id` | ID opaco, referencia a centro | Obligatorio | Recurso autorizado | Centro del grant | Servidor | Nadie | Centro autorizado | Append-only | Sí | Matriz §§9 y 10; AGENTS 5.1 | Ninguna funcional |
| Unidad accedida | `clinical_access_audit.unit_id` | ID opaco, referencia a unidad | Obligatorio | Recurso autorizado | Unidad del mismo centro y grant | Servidor | Nadie | Centro y unidad autorizados | Append-only | Sí | Matriz §§9 y 10; AGENTS 5.1 | Ninguna funcional |
| Residente accedido | `clinical_access_audit.resident_id` | ID opaco, referencia a residente | Obligatorio | Recurso autorizado | Residente del mismo centro/unidad y grant | Servidor | Nadie | Centro, unidad y residente autorizados | Append-only | Sí | Matriz §10; AGENTS 7.6 y 10 | Ninguna funcional |
| Tipo de recurso leído | `clinical_access_audit.resource_type` | Código | Obligatorio | Recurso de la operación | `BASELINE_CURRENT`, `BASELINE_HISTORY` para este contrato | Servidor | Nadie | Recurso autorizado | Append-only | Sí | Matriz §3; AGENTS 7.6 | Extensión futura a otros detalles clínicos en contrato transversal separado |
| Identificador del recurso leído | `clinical_access_audit.resource_id` | ID opaco | Obligatorio | Versión concreta entregada | Versiones basales del residente autorizado | Servidor | Nadie | Mismo residente y versión | Append-only | Sí | Matriz §10; AGENTS 7.6 y 10 | Si una lectura de historial genera un evento por listado y otro por cada versión abierta |
| Finalidad declarada y validada | `clinical_access_audit.purpose_code` | Código | Obligatorio | Sin valor por defecto | Provisionalmente `SUPERVISION_CLINICA`; catálogo final no definido | Dirección Clínica selecciona/declara; servidor valida | Nadie | Centro, unidad, residente y nivel de supervisión | Append-only | Sí | Matriz §§9 y 10; AGENTS 5.1 y 7.6 | Catálogo de finalidades, justificación adicional y controles de abuso |
| Fecha/hora del acceso | `clinical_access_audit.accessed_at` | Fecha/hora | Obligatorio | Hora del servidor | No aplica | Servidor antes de entregar contenido | Nadie | Centro, unidad, residente y recurso | Append-only | Sí | Matriz §10; AGENTS 7.6 y 10 | Retención, consulta y alertas de auditoría |

## Decisiones cerradas para validar 0001

La decisión `DEC-CONNECT-2026-09-06-D1-P01-P08` y la línea base `LBF-CONNECT-2026-09-06-V1.1` cierran los catálogos funcionales que bloqueaban el primer esquema:

1. Sexo documentado: `male`, `female`, `other`, `unknown`; fuente administrativa, sin inferencia ni texto libre.
2. Ayuda técnica: `NINGUNA` expresa ausencia conocida, `NO_DOCUMENTADO` expresa desconocimiento y `NO_APLICA` se rechaza.
3. Alimentación: textura y líquidos usan `NO_APLICA` solo con vía exclusivamente `ENTERAL`; la vía `MIXTA` exige dato oral o `NO_DOCUMENTADO`.
4. Todo `OTRO/OTRA` exige descripción no vacía y el texto sin opción se rechaza.
5. UUID técnico, referencia operativa e implantación configurable de habitación/plaza siguen las reglas técnicas aprobadas en el ADR 0003.

Se autoriza crear y aplicar `0001` sobre una D1 local desechable con datos sintéticos para revisar el SQL y ejecutar `DB-T01`–`DB-T16`. La aplicación remota continúa prohibida.

## Decisiones que pueden aplazarse

- La auditoría clínica completa de Dirección, incluidos catálogo de finalidades, nivel de supervisión, retención y consulta, si ese perfil no forma parte del primer bloque. Su aplazamiento no autoriza ninguna lectura clínica de Dirección sin permiso y auditoría.
- El actor técnico, la autorización y la auditoría del provisionamiento de centros por la plataforma.
- Los traslados entre centros. Inicialmente se representan como baja o inactivación en origen y alta independiente en destino, sin transferencia automática de identidad, basal, grants ni historial.
- La pantalla y el procedimiento de traslados dentro del centro; el historial de ubicación sí debe permitirlos sin perder trazabilidad.
- La decisión de materializar `resident.baseline_state` o derivarlo en lectura, siempre que no se introduzcan dos fuentes de verdad.
- Códigos externos opcionales de edificio, planta, unidad, habitación o plaza/cama.
- Límites definitivos de longitud, normalización tipográfica y etiquetas visibles de nombres/códigos, siempre que no se creen restricciones irreversibles antes de acordarlos.
- La anotación aclaratoria inmutable sobre versiones históricas y la reactivación de residentes.
- Alertas o analítica sobre accesos auditados, sin convertirlas en rankings individuales ni juicios automáticos.
- Autenticación y operación productivas, incluidos segundo factor, backups y disponibilidad; deben resolverse antes de datos reales o uso productivo, pero no bloquean el primer bloque con datos ficticios.

## Primer bloque de persistencia previsto

El primer bloque implementará únicamente esta secuencia:

> Alta del residente → ubicación inicial → basal pendiente → borrador del creador → firma → primera versión vigente.

La firma deberá ser atómica e idempotente: valida que el residente siga activo, que el borrador corresponda a su creador, que las nueve áreas y Barthel estén completos y que fuentes, fechas y condicionales sean válidos; después crea la primera versión vigente sin estados parciales. Las aportaciones a borradores ajenos, reevaluaciones, rectificaciones, cancelación por ausencia, traslados, reactivación y acceso de Dirección quedan regulados por este contrato para no cerrar mal el modelo, pero no forman parte de la primera entrega.

## Aspectos ya definidos por las fuentes

- El alta administrativa está separada del basal profesional y deja el basal `PENDIENTE`.
- Administración puede dar de alta; Enfermería solo puede hacerlo con permiso explícito del centro. Medicina no recibe ese permiso en la matriz vigente.
- Los centros son provisionados por la plataforma, no creados por Administración del centro.
- Centro y unidad delimitan principalmente el acceso profesional; una restricción por residente es adicional cuando corresponda. Edificio y planta son opcionales; habitación y plaza/cama son ubicación administrativa.
- El historial de ubicación es la única fuente de verdad de la ubicación actual.
- Existen exactamente nueve áreas basales: movilidad, alimentación, continencia, aseo/higiene, cognición, comunicación, conducta, sueño y ayudas habituales.
- Las nueve áreas usan el catálogo `BASAL_AREAS_V0_1`, guardan su versión, admiten observaciones, conservan autoría y deben estar respondidas para firmar. Fuente y fecha comunes se registran una vez en el basal y cada área puede sustituirlas explícitamente. `NO_DOCUMENTADO` no significa normalidad.
- No existe puntuación conjunta de las nueve áreas.
- Barthel usa `BARTHEL_COMUN_V0_1` en todos los centros, conserva diez respuestas y puntuaciones, y guarda el total automático sobre 100.
- CFS queda fuera del contrato y del futuro producto estructurado.
- Las cuatro categorías cognitivas están definidas; la etiología solo corresponde a `DEMENCIA_DOCUMENTADA`, mientras que GDS 1–7 puede registrarse con cualquier categoría si consta documentado. Etiología o GDS exigen fuente clínica específica y fecha.
- `NINGUNA` resuelve la ausencia conocida de ayuda técnica; `NO_DOCUMENTADO` conserva el desconocimiento y `NO_APLICA` se rechaza en ese campo.
- `NO_APLICA` se limita a textura/consistencia de líquidos en alimentación exclusivamente enteral; vía mixta exige datos de la parte oral o `NO_DOCUMENTADO`.
- `OTRA_TEXTURA_ADAPTADA`, `OTRO_PRODUCTO_DE_APOYO` y cualquier `OTRO/OTRA` exigen descripción breve no vacía.
- Las formas de comunicación exigen al menos una selección; `NO_SE_IDENTIFICA_FORMA_EFECTIVA` y `NO_DOCUMENTADO` son excluyentes respecto a las demás.
- Los motivos ordinarios del basal son alta, revisión programada y cambio funcional consolidado.
- Solo puede existir un borrador basal activo por residente. Solo lo firma su creador; otros profesionales autorizados pueden aportar con autoría individual y sin recibir la firma.
- Si el creador deja de estar disponible, la cancelación excepcional permanece bloqueada hasta definir actor y procedimiento; nadie recibe `BASELINE_DRAFT_CANCEL` por inferencia.
- Solo una versión firmada es vigente por residente; una nueva firma archiva la anterior sin eliminarla ni sobrescribirla.
- El inicio de rectificación permanece bloqueado; su soporte estructural futuro no concede permiso ni crea una interfaz.
- La ventana de corrección de seis horas solo se aplica a cursos/notas clínicas y nunca al basal.
- Solo Enfermería y Medicina pueden completar o reevaluar el basal, siempre con permiso específico del centro.
- Administración puede inactivar al residente con motivo obligatorio; la operación cancela el borrador abierto, bloquea nuevas escrituras y conserva lectura autorizada e historial.
- Auxiliar consulta únicamente el basal vigente de residentes asignados; Familiar y Administración no acceden al basal; Dirección requiere acceso clínico específico, solo lectura y auditado.
- Una cuenta multirol actúa con un único perfil activo explícito; los permisos de perfiles distintos no se combinan.
- Los identificadores son opacos/no secuenciales y conocer un identificador no concede acceso.

## Contradicciones históricas resueltas por la línea base v1

Este apartado conserva el rastro de las discrepancias que motivaron la consolidación. Todas quedaron resueltas por `LBF-CONNECT-2026-09-05-V1` y no constituyen decisiones abiertas:

- **CFS:** las referencias activas de PRD v0.3, matriz v0.1 y wireframes anteriores fueron sustituidas. PRD v0.5, matriz v0.2.1 y los wireframes canónicos excluyen CFS y Pfeiffer.
- **Barthel por centro:** `ENF-21` histórico fue sustituido por el catálogo común `BARTHEL_COMUN_V0_1` para todos los centros.
- **GDS:** GDS 1–7 puede registrarse cuando conste documentado con cualquier categoría cognitiva; solo la etiología queda limitada a `DEMENCIA_DOCUMENTADA`.
- **Corrección basal y seis horas:** la ventana de seis horas se reserva a cursos/notas clínicas habilitados. El basal firmado es inmutable y solo se rectifica mediante nueva versión vinculada.
- **Creación del centro:** la plataforma provisiona el centro; Administración gestiona únicamente su estructura subordinada autorizada.
- **Alta por Enfermería:** Administración mantiene el alta por defecto; Enfermería solo puede crear la identidad con el permiso explícito `RESIDENT_IDENTITY_CREATE`.
- **Citas familiares:** cada centro activa exclusivamente `DIRECT` o `REQUEST`; los recorridos no se muestran simultáneamente.

Estas resoluciones son entradas del contrato y de las pruebas. El diseño técnico no puede reinterpretarlas ni reabrirlas.
## Recomendaciones técnicas que no cambian el producto

- Generar IDs, autoría, perfil activo, ámbito y fechas en servidor; rechazar o ignorar equivalentes enviados por el cliente.
- Filtrar profesionalmente por el grant relacional `perfil + centro + unidad` y añadir residente solo cuando exista una restricción individual; validar siempre el residente contra su ubicación vigente. No formar productos cartesianos con listas independientes.
- Aplicar claves foráneas o comprobaciones equivalentes que impidan cruzar centro, unidad, residente, ubicación y versión.
- Asegurar un solo `BORRADOR`, una sola versión `FIRMADO_VIGENTE` y una sola ubicación vigente por residente mediante restricciones D1 compatibles o una transacción equivalente comprobada.
- Firmar y sustituir el basal en una operación atómica e idempotente; usar revisión optimista para borradores.
- Mantener firmadas e históricas inmutables; rectificar con una versión enlazada y nunca con un `UPDATE` silencioso. No aplicar al basal la ventana de seis horas.
- Conservar cada aportación al borrador con autoría individual sin transferir el derecho de firma.
- Aplicar la inactivación como una transición server-side atómica que registra motivo y actor, cancela el borrador abierto y deniega nuevas escrituras sin borrar el historial.
- Implementar las incompatibilidades de selecciones y las condiciones de `NO_APLICA`, fuentes y textos `OTRA` como invariantes de dominio reutilizadas por interfaz y servidor.
- Derivar la ubicación actual del único intervalo vigente del historial; no mantener columnas mutables paralelas como segunda fuente.
- Guardar los diez ítems Barthel, `BARTHEL_COMUN_V0_1` y el total automático; validar que el total coincide con las respuestas.
- Hacer que el gateway server-side escriba la auditoría clínica de Dirección antes de devolver el detalle, en la misma unidad de trabajo lógica; una obligación devuelta por una política no basta por sí sola.
- No registrar texto clínico completo en logs técnicos ni guardar tokens, secretos o datos reales en fixtures y pruebas.
