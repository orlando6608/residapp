# Nota retroactiva sobre los scripts 0075 y 0076

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ.

Los scripts `0075_cierre_excepcional_direccion.sql` y `0076_permiso_revision_calidad.sql` ya están desplegados y **no se consultaron al DPO antes de crearse**. Los he leído junto con el código que los usa. Conclusión: **ninguno bloquea el piloto, pero cada uno deja condiciones antes de usar datos reales.** Lo anoto aquí para que Orlando no tenga que rehacer nada: son decisiones, no errores.

## 0075 — Cierre excepcional por Dirección Clínica

**Qué hace [Hecho]:** Dirección Clínica puede cerrar un episodio con indicaciones médicas abiertas que nadie va a cumplir, escribiendo un motivo de hasta 300 caracteres. Se guarda una fila inmutable en `dbo.cierres_excepcionales_direccion` (motivo, cuenta, hora, número de indicaciones abiertas) (`0075`, líneas 19 a 37) y el mismo motivo se copia en la auditoría (`SqlSupervisionDirectory.cs`, líneas 202 a 206). Las indicaciones **no se tocan** y se siguen contando como sin resolver (`0075`, líneas 6 a 8). No se prepara comunicado a la familia (`Episodio.cshtml`, líneas 96 a 98).

**Lo que está bien:** no se afirma que las indicaciones se cumplieron o no, así que el registro no se falsea (exactitud, art. 5.1.d); el cierre solo es posible con la fila de constancia en la misma transacción (`0075`, líneas 62 a 64); queda la cuenta de quien cierra; el motivo solo lo ve Dirección en la trazabilidad (`SqlChangeInboxDirectory.Traceability.cs`, línea 22).

**Implicaciones de privacidad:**
1. **Motivo en texto libre, inmutable y copiado en dos sitios**, con el mismo problema de conservación y supresión que el de `0077` (ver fichero 05). Aquí el motivo es probable que contenga **datos clínicos** (por qué no se van a cumplir las indicaciones). **[Suposición]** Eso lo convierte en parte de la historia clínica, no en un dato de seguridad: entonces rige su plazo de conservación (**a verificar**) y la inmutabilidad es coherente. **Pregunta para el DPO real o abogado:** ¿es parte de la historia clínica?
2. La ayuda del campo es más floja que la de `0077`: dice «Sin datos clínicos innecesarios» (`Episodio.cshtml`, línea 105), sin advertir de nombres de terceros.
3. **Quién puede modificar el estado de un episodio asistencial.** Aquí lo hace Dirección Clínica, no un profesional que atiende. **Pregunta para abogado y CJ:** ¿lo permite la normativa de documentación clínica de Cataluña y el reglamento de la residencia? **A verificar.**
4. **«No comunicar a la familia»** (`0075`, líneas 10 a 12): quién fue informado de que hay indicaciones sin cumplir es una cuestión clínica y de derechos del paciente, más que de protección de datos. Lo señalo para CJ y el DPO.

**Condición pendiente antes de datos reales:** (a) clasificar el motivo (historia clínica o auditoría) y fijar su conservación; (b) igualar la advertencia del campo a la de `0077`; (c) añadir la actividad al registro de actividades (art. 30) y al anexo de la EIPD; (d) confirmar con abogado la competencia de Dirección Clínica para el cierre.

## 0076 — Permiso `QUALITY_REVIEW`

**Qué hace [Hecho]:** solo añade el permiso al catálogo y exige que lo tenga un perfil de Dirección Clínica (`0076`, líneas 10 a 27). La comprobación está en la aplicación y se repite en SQL (`SqlSupervisionDirectory.cs`, líneas 278 a 291). Lo concede Administración, y las concesiones y revocaciones quedan auditadas (`SqlProfessionalAccountRepository.cs`, líneas 369 y 393). Las Direcciones ya existentes no lo tienen hasta que se lo den (`0076`, líneas 3 y 4).

**Lo que está bien:** el permiso reduce quién ve las pantallas de cifras (necesidad de conocer); la separación (quien lo concede es Administración) es una buena práctica; y la calidad de proceso se mide **«por unidad, nunca por profesional»** (`IProcessQuality.cs`, líneas 25 a 27), lo que evita convertirla en vigilancia del personal.

**Implicaciones de privacidad:**
1. **La pantalla Calidad de proceso muestra nombres.** La lista de episodios con algún hito fuera de plazo incluye el **nombre visible del residente**, la unidad y el tipo de hito (por ejemplo, un protocolo urgente o un informe de derivación), lo que revela información de salud (`Calidad.cshtml`, líneas 136 y 149; `IProcessQuality.cs`, líneas 10 a 12 y 29 a 32). **La consulta no se audita** (`DireccionApplicationService.cs`, líneas 150 a 152). El comentario de las líneas 128 y 129 («ni datos de personas») es cierto para los **indicadores** agregados, pero no para esta pantalla. **No se debe copiar esa frase en el registro de actividades ni en la EIPD.**
2. **[Suposición]** Es coherente con el resto de listas de supervisión de Dirección (también nominales y sin auditar por consulta), pero hay que decidirlo expresamente, no por omisión.
3. **Indicadores agregados por unidad** en una residencia de unos cien residentes: con cifras pequeñas podrían identificar a una persona. **No lo he comprobado en el código; a verificar** si `SupervisionIndicatorRules` oculta o agrupa las cifras pequeñas.

**Condición pendiente antes de datos reales:** (a) decidir y documentar si la consulta nominal de Calidad de proceso queda sin auditoría (justificación) o escribe una fila por consulta (opción más sencilla: **una fila por consulta, no por residente**); (b) revisar las cifras pequeñas de los indicadores; (c) registrar ambas pantallas como actividad de supervisión de Dirección Clínica en el RAT y en la EIPD general.

## Proceso para el futuro

**[Hecho]** `CLAUDE.md` del proyecto ya exige consultar al agente antes de un ADR, esquema SQL, integración o función de IA. Estos dos scripts son anteriores a esa práctica o la omitieron; esta nota los regulariza, pero no sustituye al DPO real.

## A verificar o decidir

1. **[Decidir] Orlando/CJ:** auditoría de la consulta de Calidad de proceso, y advertencia del campo de 0075.
2. **A verificar:** cifras pequeñas en indicadores (código); competencia de Dirección Clínica para cerrar episodios (abogado); si el motivo del cierre es historia clínica (DPO).
3. **[Decidir] DPO y residencia:** conservación del motivo de 0075 (ver fichero 05).
