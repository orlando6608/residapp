# Antes de usar con residentes reales: lo que hay que hacer primero

**Para quién es:** Orlando, CJ y cualquier sesión de Claude que retome el trabajo. Es una lista de cosas por cerrar, no un dictamen.
Hoy estas funciones solo se usan con datos de prueba (la base de Azure es de desarrollo). Esta lista dice qué falta para que se puedan usar con residentes de verdad.

**Instrucción para Claude:** si Orlando habla de activar el centro piloto, de usar datos reales o de mergear algo que toque el historial tras un traslado, el cierre excepcional
o Calidad de proceso, **recuérdale esta lista antes** de seguir. No la des por cumplida sin que él lo diga.

## Las funciones a las que afecta

| Función | Script | Dónde está explicada |
| --- | --- | --- |
| Historial de los episodios cerrados tras un traslado | `0077` | Borradores `historial-tras-traslado-01…05` de esta carpeta |
| Cierre excepcional de un episodio por Dirección Clínica | `0075` | Nota `historial-tras-traslado-06-nota-retroactiva-0075-0076.md` |
| Permiso y pantalla de Calidad de proceso | `0076` | La misma nota 06 |
| La baja cancela las citas futuras y cierra las solicitudes de cita del residente | `0078` | Más abajo, apartado 4 |

## 1. Que una persona valide los borradores

Los documentos de esta carpeta (`historial-tras-traslado-01…06`) los redactó el agente `dpo-residapp`: son una segunda lectura experta, **no** un DPO designado ni un abogado.
Hasta que **el DPO real de la residencia o un abogado** los lea, corrija y dé por buenos, no se usan con datos reales.

Qué tiene que mirar esa persona (cada borrador deja sus decisiones abiertas al final, en «A verificar o decidir»):
- el registro de actividades de tratamiento y el anexo a la evaluación de impacto (los análisis de riesgos que exige el RGPD cuando se tratan datos de salud);
- el texto informativo para residentes, representantes y personal;
- el procedimiento de revisión de las lecturas `TRANSFER_HISTORY_READ` (quién, cada cuánto);
- cuánto se conserva el motivo que escribe quien pide el historial y cómo se borraría, porque la tabla no admite cambios;
- los plazos legales de conservación de la documentación clínica en Cataluña, que el agente no pudo confirmar en fuente oficial y dejó «a verificar»;
- qué autoridad de control corresponde (AEPD o APDCAT) según el tipo de centro piloto.

## 2. Que Orlando y CJ decidan las mejoras que propone el agente

Son cambios en la aplicación que el agente aconseja y que **no están hechos**. Cada uno está explicado en `docs/tareas/alta-prioridad/pendientes-enfermeria.md` («Tras un traslado entre unidades»),
y la mayoría se han preguntado a CJ en `docs/pendientes-cj/direccion-y-traslados-confirmaciones-cj.html`:

1. Una vista de Dirección con **todas las peticiones** del periodo. Hoy solo ve las lecturas residente por residente, y la pantalla dice «Dirección puede revisarlas». (Pregunta 3.3.)
2. El motivo con **lista cerrada** más texto corto opcional, y que las filas de lectura no repitan el texto libre. (Pregunta 3.2.)
3. **Registrar las peticiones rechazadas**, para detectar sondeos.
4. Un **límite de antigüedad** de los episodios que se pueden abrir. (Pregunta 3.1.)
5. Una **vía de purga** del motivo (por ejemplo, sustituir el texto por una marca fija dejando constancia, como ya hace `0075`).
6. **Auditar la consulta de Calidad de proceso** de `0076`, o documentar por qué no se audita. La pantalla enseña el nombre del residente en la lista de hitos fuera de plazo. (Preguntas 2.2 y 2.3.)

## 3. Condiciones abiertas de `0075` y `0076`

Están en la nota 06. En resumen: clasificar el motivo del cierre excepcional (historia clínica o auditoría) y fijar su conservación, igualar su advertencia a la de `0077`,
confirmar con un abogado que Dirección Clínica puede cambiar el estado de un episodio, y decidir si la consulta nominal de Calidad de proceso se audita.

## 4. La baja que cancela citas (`0078`)

Informe del agente `dpo-residapp` (2026-10-28): admisible con condiciones, y las del código están hechas (causa y motivo fijo en el historial, aviso antes de confirmar, alcance exacto, auditoría solo con identificadores). Lo que **no** es código y valida una persona:

- el texto informativo del Portal Familiar: que, al terminar la estancia, se cancelan las citas futuras y se cierran las solicitudes abiertas, y que el centro avisa por su canal habitual (lo adopta la residencia como responsable);
- quién avisa a la familia y por qué canal, con cuidado en la baja por fallecimiento (la aplicación no envía correos y no avisa);
- que atribuir la cancelación a la cuenta de Administración que dio la baja, con causa y motivo fijos, es adecuado como registro (y que no sirve para evaluar a esa persona);
- una línea en el registro de actividades («cancelación de citas por baja», datos solo de identificación) y el plazo de conservación de las filas nuevas (hoy el de la baja, 5 años, sin confirmar en fuente oficial).

## Cómo se marca como hecho

Cuando una persona valide los borradores y se decidan las mejoras, anótalo aquí con la fecha y quién lo hizo, y quita el recordatorio del traspaso (`docs/prompts-ia/continuar-construccion.md`)
y de la memoria de Claude. Mientras tanto, no se borra nada de esta lista.
