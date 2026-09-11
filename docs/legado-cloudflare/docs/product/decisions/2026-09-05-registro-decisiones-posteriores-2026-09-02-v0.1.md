# Registro de decisiones posteriores al 2 de septiembre de 2026

**Proyecto:** Connect - Plataforma asistencial y de comunicación con familias  
**Versión:** 0.1  
**Fecha de corte:** 5 de septiembre de 2026  
**Estado:** Aprobado como fuente de consolidación del PRD v0.4  

## 1. Finalidad

Este registro reúne las decisiones aprobadas después del cierre documental del 2 de septiembre de 2026. Su finalidad es impedir que el PRD v0.3, la matriz de permisos v0.1 y los wireframes cerrados sigan transmitiendo reglas sustituidas.

Hasta que se declare la nueva línea base funcional, el orden de prevalencia será:

1. Decisiones explícitas recogidas en este registro.
2. PRD v0.3 consolidado.
3. Matriz de permisos de seis perfiles v0.1.
4. Wireframe del perfil correspondiente.
5. AGENTS.md v1.2.
6. Implementación existente.

Las contradicciones deberán resolverse de forma visible durante la consolidación; no se corregirán silenciosamente en el código.

## 2. Resumen de decisiones

| Identificador | Fecha | Decisión | Estado |
| --- | --- | --- | --- |
| DEC-2026-09-04-01 | 04/09/2026 | Barthel común de diez ítems, selección mediante botones y total automático | Aprobada |
| DEC-2026-09-04-02 | 04/09/2026 | Exclusión completa de la CFS | Aprobada |
| DEC-2026-09-04-03 | 04/09/2026 | Basal firmado inmutable; rectificación mediante nueva versión vinculada | Aprobada |
| DEC-2026-09-04-04 | 04/09/2026 | Un borrador basal activo por residente y firma exclusiva de su creador | Aprobada |
| DEC-2026-09-05-01 | 05/09/2026 | Catálogo basal común de nueve áreas | Aprobada |
| DEC-2026-09-05-02 | 05/09/2026 | Autorización basada en cuenta, perfil activo, ámbito y permiso específico | Aprobada |
| DEC-2026-09-05-03 | 05/09/2026 | Estructura organizativa y ubicación longitudinal del residente | Aprobada |
| DEC-2026-09-05-04 | 05/09/2026 | Markdown como fuente canónica en GitHub y PDF como versión cerrada | Aprobada |
| DEC-2026-09-05-05 | 05/09/2026 | Secuencia de consolidación y declaración posterior de línea base | Aprobada |

## 3. Decisiones detalladas

### DEC-2026-09-04-01 - Índice de Barthel común

Se adopta un único módulo de Índice de Barthel compartido por los perfiles profesionales autorizados.

- Consta de los diez ítems del instrumento.
- Cada ítem se responde mediante opciones seleccionables, no mediante introducción manual de la puntuación total.
- La puntuación total se calcula automáticamente.
- El resultado queda integrado en la versión basal a la que pertenece.
- Su creación o reevaluación exige el permiso basal correspondiente.

**Impacto documental:** actualizar PRD, matriz de permisos y wireframes de Enfermería y Medicina. Mantener la consulta resumida donde corresponda en otros perfiles.

### DEC-2026-09-04-02 - Exclusión de la CFS

La Clinical Frailty Scale queda eliminada completamente del alcance funcional actual.

- No se mostrará como campo, selector, resumen, permiso, informe o criterio de aceptación.
- Se elimina la puerta pendiente relativa a licencia, traducción o activo gráfico de la CFS.
- La decisión sustituye todas las referencias condicionales a una futura CFS autorizada.

**Impacto documental:** retirar la CFS del PRD v0.3, matriz v0.1, wireframes de Enfermería, Medicina y Administración, resúmenes basales e informe de derivación.

### DEC-2026-09-04-03 - Inmutabilidad y rectificación del basal

El estado basal sigue el ciclo `BORRADOR -> FIRMADO Y VIGENTE -> HISTÓRICO`.

- Un borrador puede permanecer incompleto, pero no puede activarse como vigente hasta superar las validaciones requeridas.
- Al firmarse se congelan contenido, autoría, firma, fecha y contenedores organizativos aplicables.
- Un basal firmado no se edita ni se sobrescribe.
- Una reevaluación crea una nueva versión, que pasa a vigente y archiva la anterior sin alterar su firma.
- Un error en un basal firmado se resuelve mediante una rectificación basal trazable y vinculada, no mediante edición del original.
- La ventana ordinaria de seis horas no se aplica al basal. Esa ventana queda limitada a los cursos o notas clínicas para los que se defina expresamente.

**Impacto documental:** sustituir la regla general de corrección de seis horas por reglas específicas según el tipo de registro; revisar PRD, matriz y wireframes de Enfermería y Medicina.

### DEC-2026-09-04-04 - Propiedad y firma del borrador basal

- Solo puede existir un borrador basal activo por residente.
- Solo la persona que crea el borrador puede firmarlo.
- La firma no puede transferirse ni atribuirse a otro profesional.
- Otros profesionales autorizados pueden realizar aportaciones identificadas y auditadas sin adquirir la autoría del borrador ni la capacidad de firmarlo.
- Si la persona creadora no puede finalizarlo, el borrador se cancela con motivo trazable y se crea uno nuevo por el profesional que asumirá la actuación.

**Impacto documental:** incorporar estados, permisos, concurrencia, cancelación justificada y criterios de aceptación en PRD, matriz, wireframes y futura persistencia.

### DEC-2026-09-05-01 - Catálogo basal común

El basal mantiene exactamente nueve áreas:

1. Movilidad.
2. Alimentación.
3. Continencia.
4. Aseo e higiene.
5. Cognición.
6. Comunicación.
7. Conducta.
8. Sueño.
9. Ayudas habituales.

Reglas consolidadas:

- Movilidad separa el modo habitual de desplazamiento, la ayuda técnica, la ayuda humana y las transferencias.
- Las ayudas técnicas de movilidad no se duplican dentro de «Ayudas habituales».
- Alimentación distingue, al menos, textura normal, troceada, triturada y puré.
- La consistencia habitual de líquidos distingue sin espesante, néctar, miel, pudín y no documentado.
- Cognición puede recoger etiología y GDS 1-7 únicamente cuando consten, junto con fuente y fecha.
- Las opciones `NO_APLICA` y `OTRO` se usarán únicamente donde tengan significado funcional; `OTRO` exigirá descripción cuando sea necesario para que el dato resulte interpretable.
- Las formas de comunicación que sean incompatibles entre sí deberán modelarse como alternativas excluyentes.
- Fuente y fecha pertenecen a la versión basal común y no deben repetirse de forma inconsistente en cada área.

**Impacto documental:** reemplazar el catálogo basal previo en PRD y wireframe de Enfermería; reflejar la consulta del mismo contrato en Medicina, Auxiliar y Dirección cuando sus permisos lo permitan.

### DEC-2026-09-05-02 - Autorización y datos confiables

Toda creación, modificación, firma, rectificación o consulta protegida exige simultáneamente:

- cuenta activa;
- perfil activo seleccionado explícitamente;
- centro y unidad autorizados;
- acceso al residente o ámbito correspondiente;
- permiso específico para la operación.

El servidor obtiene la identidad, el perfil activo, el ámbito, la autoría y la fecha/hora desde la sesión y otras fuentes confiables. Estos valores no se aceptan como campos libres enviados por el cliente.

La política inicial es denegar por defecto. Una cuenta multirol no suma permisos de perfiles que no estén activos.

**Impacto documental:** reforzar PRD, matriz de permisos, criterios de aceptación y futura implementación de autorización en servidor.

### DEC-2026-09-05-03 - Organización y ubicación

- Centro y unidad son contenedores obligatorios.
- Edificio y planta son opcionales.
- Habitación y plaza/cama se utilizan según la estructura configurada por cada centro.
- Los centros son provisionados por la plataforma; Administración gestiona la estructura subordinada autorizada.
- Crear o administrar estructura organizativa no concede por sí mismo acceso a residentes ni a datos clínicos.
- La ubicación del residente debe conservar historial temporal; no se sobrescribe de forma que se pierda dónde estaba ubicado en una fecha anterior.

**Impacto documental:** revisar PRD, matriz, wireframe de Administración y futura relación temporal entre residente, ubicación y eventos.

### DEC-2026-09-05-04 - Fuentes documentales canónicas

- La documentación funcional canónica se mantendrá en Markdown dentro de GitHub.
- Los PDF se conservarán como versiones cerradas, legibles y archivables, pero no serán la fuente principal para nuevas modificaciones.
- Cada versión deberá conservar fecha, número de versión, estado y relación con el documento al que sustituye.
- No se eliminarán los PDF ya cerrados al publicar las nuevas versiones.

**Impacto documental:** crear fuentes Markdown del PRD, matriz, wireframes afectados, matriz de trazabilidad y declaración de línea base.

### DEC-2026-09-05-05 - Secuencia de consolidación

Se aprueba el siguiente orden de trabajo:

1. Crear este registro de decisiones posteriores al 2 de septiembre.
2. Generar el PRD v0.4 consolidado.
3. Actualizar la matriz de permisos v0.2.
4. Corregir los wireframes afectados sin rediseñarlos innecesariamente.
5. Crear la matriz de trazabilidad `requisito -> permiso -> pantalla -> entidad -> prueba`.
6. Declarar el conjunto resultante como línea base funcional.

No se cerrará el esquema físico D1/Drizzle antes de completar esta secuencia.

## 4. Asuntos expresamente pendientes

Los siguientes puntos no quedan aprobados por este registro:

- Esquema físico definitivo de D1/Drizzle.
- Estrategia definitiva de una base compartida o separación por centro.
- Roles concretos de Enfermería y Medicina que recibirán permiso para crear o reevaluar el basal en cada centro.
- Semántica final de ausencia de ayuda técnica complementaria.
- Tratamiento de líquidos cuando exista alimentación enteral.
- Obligatoriedad de detallar `OTRA_TEXTURA_ADAPTADA` y `OTRO_PRODUCTO_DE_APOYO`.
- Política productiva completa de conservación, copias de seguridad y rectificación de notas clínicas.
- Autorización para utilizar datos reales, que seguirá dependiendo de las medidas técnicas, organizativas y jurídicas correspondientes.

## 5. Documentos que deberán actualizarse

| Documento | Versión de destino | Cambio principal |
| --- | --- | --- |
| PRD consolidado | v0.4 | Integrar todas las decisiones de este registro |
| Matriz de permisos | v0.2 | Retirar CFS y concretar borrador, firma, rectificación y ámbitos |
| Wireframe de Enfermería | v0.2 | Sustituir módulo basal y eliminar CFS |
| Wireframe de Medicina | v0.2 | Eliminar CFS y ajustar consulta/reevaluación basal |
| Wireframe de Administración | v0.2 | Eliminar CFS y ajustar provisionamiento/estructura |
| Wireframe de Auxiliar | v0.3, solo si el texto visible resulta afectado | Mantener consulta basal sin capacidad de modificación |
| Portal Familiar | Sin rediseño previsto | Verificar que no expone basal ni escalas |
| Dirección/Coordinación Clínica | Sin rediseño previsto | Verificar acceso clínico condicionado y de solo lectura |
| Matriz de trazabilidad | v0.1 | Enlazar requisitos, permisos, pantallas, entidades y pruebas |

## 6. Criterio de cierre

Este registro se considerará incorporado cuando:

- no quede ninguna referencia funcional a la CFS;
- la regla de seis horas no afecte al basal;
- borrador, firma exclusiva, cancelación, rectificación e historial basal estén descritos de forma idéntica en todos los documentos aplicables;
- el catálogo de nueve áreas sea único y reutilizado por los perfiles autorizados;
- permisos y ámbitos coincidan entre PRD, matriz y wireframes;
- la matriz de trazabilidad no presente requisitos sin pantalla, permiso, entidad o prueba;
- se emita la declaración formal de línea base funcional.

