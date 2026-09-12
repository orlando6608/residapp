# Supervisión clínica de Dirección/Coordinación Clínica

Dirección/Coordinación Clínica es un perfil de supervisión pura: observa el proceso asistencial de forma agregada y, solo con permiso específico, finalidad válida y auditoría previa, puede consultar contenido clínico detallado en modo de solo lectura. Nunca valora, indica, ejecuta, escala, corrige ni cierra nada desde este perfil.

## Alcance y exclusiones

Este fichero documenta la parte clínica de la supervisión: el acceso condicionado a detalle clínico, los indicadores agregados, y la consulta de derivaciones y comunicación familiar en solo lectura. La parte puramente administrativa de este perfil (ámbito de supervisión, gestión de accesos denegados) se resume brevemente al final; el detalle completo vive en `docs\historias-usuario\direccion-coordinacion-clinica.md`.

## Glosario mínimo

- **Permiso + finalidad + auditoría previa**: condición que debe cumplirse, y quedar registrada, antes de que el sistema entregue cualquier contenido clínico detallado a Dirección.
- **Solo lectura**: todo lo que Dirección consulta en el ámbito clínico es de consulta exclusivamente; no puede modificarlo.
- **Cambio explícito de perfil**: acción obligatoria para que una cuenta con varios perfiles pueda actuar clínicamente; mientras actúa como Dirección, no puede hacerlo.

## Flujo paso a paso

1. Dirección abre su panel agregado: cierres pendientes, eventos abiertos o vencidos, seguimientos, continuidad entre turnos, indicaciones con incidencia, derivaciones y estado de la comunicación familiar, todo agregado por su ámbito de supervisión.
2. Navega por unidad o por tipo de pendiente, viendo listas con denominador, antigüedad y estado, sin que esto abra automáticamente ninguna nota clínica completa.
3. Abre el detalle operativo de un pendiente concreto: hitos, responsables de equipo y estado, todavía sin contenido clínico ni ninguna acción asistencial disponible.
4. Si necesita el detalle clínico de un caso, el sistema exige que Dirección disponga de un permiso clínico específico, declare una finalidad válida y esté dentro de su ámbito; el acceso se registra en auditoría **antes** de entregar el contenido, nunca después. Solo entonces se muestra el detalle, que puede incluir el basal vigente si está autorizado, pero nunca la escala CFS (que no existe en el producto).
5. Desde ese detalle, puede desplegar la línea temporal completa del residente (plegada por defecto) y el historial de eventos cerrados, siempre en solo lectura.
6. Consulta paneles de indicadores agregados por ámbito y periodo, con su denominador cuando corresponde: continuidad, seguimientos vencidos, indicaciones pendientes, evolución temporal comparable y calidad de proceso. Estos paneles nunca generan predicción clínica ni juicio automático.
7. Puede consultar derivaciones (el informe firmado, si tiene permiso, nunca generarlo ni firmarlo) y el estado de la comunicación familiar (frecuencia, publicaciones pendientes, errores), siempre en solo lectura.
8. Puede consultar la trazabilidad clínica auditada de los hitos autorizados, y el historial de correcciones y rectificaciones (original, motivo, autor y fechas), sin que ello permita alterar ningún registro.
9. Puede generar informes de actividad agregados por ámbito y periodo, sin que estos incluyan exportación masiva de historias individuales ni rankings nominativos de productividad.
10. Si Dirección necesita actuar clínicamente sobre un caso (valorar, indicar, ejecutar, escalar, corregir o cerrar), debe cambiar explícitamente a otro perfil autorizado: esa acción ya no pertenece a Dirección.

## Reglas de negocio

- Ninguna pantalla de Dirección incluye un control de escritura asistencial.
- Toda lectura clínica detallada exige permiso específico, finalidad declarada, ámbito correspondiente y auditoría registrada antes de servir el contenido, nunca después.
- Consultar el basal (vigente o histórico) desde Dirección es siempre de solo lectura y nunca incluye la escala CFS.
- Los indicadores, paneles e informes agregados de Dirección nunca generan rankings nominativos de productividad individual ni evaluaciones automáticas de desempeño.
- Para actuar clínicamente sobre un caso, la cuenta debe cambiar explícitamente a un perfil asistencial distinto de Dirección.

## Resumen de la parte administrativa de Dirección

Además de la supervisión clínica, Dirección conoce su propio ámbito de supervisión (centros, unidades y permisos vigentes del perfil activo) y recibe mensajes neutros ante un acceso denegado, sin que estos revelen contenido ni la existencia de información fuera de su ámbito. El detalle completo de estas dos capacidades se documenta en `docs\historias-usuario\direccion-coordinacion-clinica.md`.

## Trazabilidad

| Paso del flujo | Requisitos PRD | Pantalla de referencia |
| --- | --- | --- |
| Panel agregado y navegación por unidad/pendiente | `DIR-02` | DIR-01, DIR-02, DIR-03 |
| Detalle operativo sin contenido clínico | `DIR-03` | DIR-04 |
| Acceso condicionado a detalle clínico con auditoría previa | `DIR-04` | DIR-05 |
| Línea temporal e historial en solo lectura | — | DIR-06, DIR-07 |
| Indicadores agregados sin ranking individual | `DIR-08`, `DIR-09` | DIR-08 a DIR-11 |
| Derivaciones y comunicación familiar en solo lectura | `DIR-06`, `DIR-07` | DIR-12, DIR-13 |
| Trazabilidad y correcciones en solo lectura | — | DIR-14, DIR-15 |
| Informes agregados sin exportación masiva | `DIR-10` | DIR-16 |
| Cambio explícito de perfil para actuar clínicamente | `DIR-05` | — |

## Nota de procedencia

Este flujo consolida los requisitos `DIR-01` a `DIR-11` del PRD v0.5 y el wireframe funcional de Dirección/Coordinación Clínica v0.1, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
