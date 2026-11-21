# ADR 0017 — Tipo de servicio del centro: Residencia o Centro de Día

Estado: **aceptada en lo técnico** el 2026-11-21 (decisión de Orlando, de una en una). Construye la fase 1 del Centro de Día (`docs/tareas/alta-prioridad/pendientes-centro-de-dia.md`; script `0113`). **No autoriza ningún tratamiento nuevo de datos de personas**: eso lo decide cada función futura con su propio ADR. Las puertas de la persona experta (DPO real o abogado) quedan abiertas y **impiden usar un Centro de Día con personas reales** hasta que valide los borradores de `docs/privacidad/`.
Lectura de protección de datos previa, que este ADR recoge: `docs/privacidad/centro-de-dia-lectura-dpo.md` (borrador del agente `dpo-residapp`, apartado 6.4; lo valida una persona antes de usarlo con datos reales).

Etiquetas: **[Hecho]** verificado en el repositorio; **[Decidido]** lo fijó Orlando; **[Propuesta]** recomendación del DPO o de ingeniería; **[A verificar]** no se ha comprobado.

## Contexto

Hasta ahora todos los centros de ResidApp son residencias. Se quiere abrir el producto a un **Centro de Día**: un servicio al que las personas van por la mañana y del que vuelven a casa por la tarde, sin camas ni turno de noche. Hace falta una forma de saber, de cada centro, de cuál de los dos tipos es, para que las pantallas, los textos y las funciones se adapten sin tocar lo que ya funciona en las residencias.

Lo que existe hoy **[Hecho]**:

- `dbo.centros` guarda `id`, `codigo`, `nombre_visible`, `estado` y `creado_en` (`0002`); **no tiene seguridad por filas** (`0068`): la aplicación lee el nombre del centro de cualquier ámbito de la cuenta, y lo necesitan el selector de ámbito y las invitaciones, que se ven antes de tener ámbito en ese centro.
- La aplicación tiene `UPDATE` de la tabla `centros` entera (matriz de `0091`) para cambiar el nombre.
- El código del centro es inmutable (`0095`, `TR_centers_guard`). El centro reservado `PLATAFORMA` (`0026`) está en la misma tabla y no es un servicio.
- La zona horaria por centro (`0109`) es el precedente: se elige al crear el centro, después solo la corrige Plataforma con un motivo de lista cerrada, en una tabla de solo añadir con seguridad por filas.
- Los módulos opcionales por centro (ADR 0015) se contratan, se encienden y se apagan, con vigencia y anexo de contrato.

## Decisión

1. **Una columna `tipo_de_servicio` en `dbo.centros`** con dos valores, `RESIDENCIA` (por defecto, y el de todos los centros que ya existen) y `CENTRO_DE_DIA`. **[Decidido]** El nombre evita chocar con «modalidad» (la de una cita: presencial, telefónica…, y reserva directa o solicitud previa) y con «tipo» (que en `docs/privacidad/` es la titularidad del centro: pública, concertada o privada).
2. **No es un módulo del ADR 0015.** **[Decidido]** No se contrata, no se enciende ni se apaga: define la naturaleza del servicio, y cambiarla con datos cambiaría las finalidades, las bases jurídicas y los plazos. Por eso no hay anexo, ficha versionada ni vigencia.
3. **Quién lo fija:** Plataforma, **al crear el centro**, según lo que el centro declara en el alta o el contrato (RGPD art. 28.3.a, que exige que el contrato recoja el tipo de tratamiento). El centro no lo cambia desde ninguna pantalla. **El formulario de alta no preselecciona el tipo** y muestra una confirmación antes de crear. **[Decidido]** Así un descuido no crea un Centro de Día como Residencia, que solo se corrige con el centro vacío.
4. **Inmutable, con una única corrección.** **[Decidido]** Solo Plataforma puede corregir un **error de alta**, y solo con el centro **sin ninguna persona**: ni residentes (de cualquier estado, bajas y suspendidos incluidos) ni familiares. El motivo es de una lista cerrada de un único valor, `ERROR_DE_ALTA`, sin texto libre. Con personas, se crea un centro nuevo y sus datos **no se trasladan**.
5. **Fuente de verdad y constancia.** **[Decidido]** La fuente de verdad es la columna (se lee igual que el nombre del centro, sin seguridad por filas). Una tabla de solo añadir, `dbo.tipos_servicio_centro`, guarda quién y cuándo lo fijó o lo corrigió. Un disparador de guarda sobre `centros` y una prueba garantizan que las dos coinciden. Se prefirió a «solo tabla» (como la zona horaria) porque esa tabla tiene seguridad por filas y el selector de ámbito y las invitaciones no pueden leerla; y a «solo columna» porque la aplicación puede escribir en `centros` y la base no garantizaría que solo Plataforma lo cambia.
6. **Única vía de cambio:** un procedimiento con los permisos del dueño de la base, `dbo.corregir_tipo_servicio_centro`. La aplicación tiene `DENY UPDATE (tipo_de_servicio)` sobre `centros`, así que no puede cambiarlo por la vía ordinaria ni con un SQL manipulado. Como el procedimiento se salta la seguridad por filas, **comprueba por sí mismo quién llama** (el ámbito activo de la sesión debe ser de perfil `PLATAFORMA` y de la cuenta indicada) y que el centro está vacío. No devuelve cifras, solo éxito o un código cerrado.
7. **Lectura.** El tipo se lee del centro verificado en la base de datos, **nunca de la cookie del ámbito** (ADR 0008, punto 5), con un único ayudante. Esta es la **lista cerrada de lo que depende del tipo**: la etiqueta, ocultar habitación y cama, el vocabulario de cara a las personas y los valores por defecto del horario de comunicados. **Cualquier función nueva que dependa del tipo** (asistencia diaria, parte a la familia, correo diario, quién recoge, transporte) exige ADR propio, lectura del esquema por el DPO, interruptor apagado por defecto y comprobación del tipo en el caso de uso.
8. **Qué NO es ni cambia.** No autoriza ningún tratamiento nuevo. No cambia la seguridad por filas, los permisos de perfil, los subencargados ni quién es responsable.
9. **Visibilidad.** Plataforma ve el tipo de cualquier centro; el personal, el de su centro; una cuenta con perfiles en varios centros, el de cada uno donde tiene ámbito, en el selector; la familia, solo para el vocabulario, por el vínculo con su residente. La etiqueta es **de texto** (no solo de color: WCAG AA) y la pone un único ayudante. **No aparece** en correos, títulos de pestaña, direcciones, cookies ni en la auditoría de ninguna persona. El centro reservado de Plataforma no muestra etiqueta.
10. **Titularidad y conciertos.** El tipo **no** los codifica ni los deduce. Si el producto necesita registrar plazas concertadas, será otro dato con otro ADR y otra lectura (autoridad de control, esquema de seguridad, dato por persona).
11. **Papeles.** El contrato de encargado y el registro de actividades nombran los dos tipos; la EIPD de cada centro lleva solo los riesgos de lo que ese centro use.

## Lo que construye el script `0113` (resumen; el detalle está en el propio script)

- La columna, con `DEFAULT 'RESIDENCIA'` y un `CHECK` de los dos valores.
- La tabla `tipos_servicio_centro`, de solo añadir (disparador `INSTEAD OF UPDATE, DELETE`), con seguridad por filas como `zonas_centro`, sin recuentos ni texto libre. La cuenta del operador es un dato personal seudonimizado, como en `zonas_centro` y `modulos_centro`; su plazo de conservación queda sin decidir con el de esas dos tablas.
- Un disparador de validación sobre la tabla y otro de guarda sobre `centros`.
- Los permisos: `INSERT` y `DENY UPDATE, DELETE` en la tabla nueva; `DENY UPDATE (tipo_de_servicio)` en `centros`; nada para `residapp_tareas`.
- El procedimiento de corrección.

El alta de un centro (código de aplicación) inserta el centro con su tipo y la fila 1 `ALTA` en la misma transacción.

## Consecuencias

- **[Hecho]** Ninguna residencia existente cambia: el valor por defecto es `RESIDENCIA` y el historial de sus centros empieza vacío.
- La prueba de permisos de `0091` aprende una excepción de columna: `centros` sigue con `UPDATE`, pero no sobre `tipo_de_servicio`.
- El alta de residente no bloquea la fila del centro (`SqlResidentRepository`), así que el procedimiento bloquea esa fila y lee las personas con bloqueo para que un alta concurrente no deje un centro con personas y el tipo cambiado. Puede dar un interbloqueo (error 1205, como en `0107`) con un alta simultánea: SQL Server deja caer a uno de los dos, que falla con un error genérico de «no disponible» y se repite a mano; **la aplicación no reintenta**. Se acepta porque la corrección es un acto raro de Plataforma sobre un centro sin personas. Las pruebas cubren las esperas (una corrección que espera a un alta a medias, un alta que espera a una corrección, y la ventana entre comprobar «sin personas» y cambiar el tipo), pero no provocan el interbloqueo. **[Decidir]** Orlando, si algún día conviene un reintento automático.
- La etiqueta del tipo se añade a las listas de centros (Plataforma, selector de ámbito, invitaciones): inventario en `pendientes-centro-de-dia.md`.

## Asuntos abiertos, sin resolver en este ADR

- El **paso de una persona de un servicio a otro de la misma entidad**: serían dos centros distintos, y el traslado de ResidApp solo vale entre unidades del mismo centro (`D1-P04`), así que sería un alta nueva, sin historial compartido. Falta decidir qué se exporta y con qué base jurídica.
- Las **cuentas con perfiles en los dos centros** (ADR 0007, pendiente del DPO real).
- Si la entidad es **un responsable o dos**.
- El **plazo de conservación** de `tipos_servicio_centro`.
- La **base jurídica** del art. 9.2.h para un servicio social, la autoridad de control y la obligación del ENS: apartado 6 de la lectura del DPO, para la persona experta.
