# Gestión del estado basal y la escala Barthel

El estado basal es la referencia habitual de cada residente: nueve áreas asistenciales más la escala Barthel de diez ítems. Enfermería y Medicina comparten el mismo módulo para crearlo, completarlo y firmarlo; el resto de perfiles solo lo consultan, con distintos niveles de detalle.

## Alcance y exclusiones

Este flujo cubre la creación, edición, firma e historial del estado basal. No cubre:

- El alta administrativa que deja el basal en estado pendiente (ver `docs\historias-usuario\administracion.md`).
- El registro cotidiano del Auxiliar, que solo consulta un resumen del basal vigente (ver [registro-cotidiano-auxiliar.md](registro-cotidiano-auxiliar.md)).
- El acceso de Dirección al basal, que es siempre de solo lectura y condicionado a permiso (ver [supervision-clinica-direccion.md](supervision-clinica-direccion.md)).

## Glosario mínimo

- **Borrador**: versión del basal en proceso de completarse; solo puede existir uno activo por residente.
- **Versión vigente**: la última versión firmada del basal, la que se usa como referencia habitual.
- **Versión histórica**: una versión firmada anterior, conservada y consultable con permiso.
- **Firma**: acción que congela una versión y la convierte en vigente, archivando la anterior.
- **Aportación**: colaboración de un profesional distinto del creador del borrador, identificada, pero sin transferir la autoría ni la capacidad de firmar.

## Flujo paso a paso

1. Enfermería o Medicina abre la ficha del residente y consulta si tiene basal vigente o si está pendiente.
2. Crea un borrador inicial o abre una reevaluación, siempre que tenga el permiso correspondiente para esa acción; solo puede existir un borrador activo por residente al mismo tiempo.
3. Otro profesional autorizado puede añadir una aportación identificada y auditada al borrador, sin convertirse por ello en autor del borrador ni adquirir la capacidad de firmarlo.
4. Se completan las nueve áreas del estado basal (ver tabla siguiente), aplicando las reglas de las decisiones D1-P01 a D1-P08 (ver tabla de reglas). Las nueve áreas deben estar respondidas para poder firmar; `NO_DOCUMENTADO` nunca se interpreta como normalidad.
5. Se completa la escala Barthel común, de diez ítems, con cálculo automático y validado del total sobre 100. Las respuestas y puntuaciones se conservan junto a la versión del basal.
6. La escala CFS (Clinical Frailty Scale) y la escala Pfeiffer fueron retiradas por completo del producto: no existen como pantalla, campo, puntuación, resumen ni contenido de informe.
7. Se firma el borrador. Solo puede firmarlo la misma cuenta que lo creó, actuando con el mismo perfil profesional autorizado; la firma no se transfiere a otra persona. Al firmar se congelan el contenido, la autoría, el perfil firmante, la fecha y hora, la versión del catálogo y el contexto organizativo aplicable; la nueva versión pasa a vigente y la anterior a histórica, en una única operación atómica e idempotente.
8. Si la persona que creó el borrador no puede finalizarlo, el borrador se cancela con un motivo trazable, y otro profesional autorizado crea uno nuevo.
9. Las versiones firmadas quedan disponibles en el historial del basal, junto con su motivo, autoría y vínculos de reevaluación.

## Las nueve áreas del estado basal

| Área |
| --- |
| Movilidad |
| Alimentación |
| Continencia |
| Aseo / higiene |
| Cognición |
| Comunicación |
| Conducta |
| Sueño |
| Ayudas habituales |

Las nueve áreas no generan una puntuación conjunta; la escala Barthel conserva su puntuación independiente.

## Decisiones D1-P01 a D1-P08

| Decisión | Regla aplicada |
| --- | --- |
| `D1-P01` — Sexo documentado | Catálogo cerrado (`male`/`female`/`other`/`unknown`), tomado literalmente de una fuente administrativa; nunca se infiere ni admite texto libre |
| `D1-P08a` — Ayuda técnica | `NINGUNA` significa que consta que no usa ayuda técnica; `NO_DOCUMENTADO` significa que se desconoce; `NO_APLICA` no está permitido en este campo; `OTRA` exige una descripción breve no vacía |
| `D1-P08b` — Alimentación enteral | `NO_APLICA` en textura o consistencia de líquidos solo se admite si la vía es exclusivamente enteral; una vía mixta exige registrar la parte oral o `NO_DOCUMENTADO` |
| `D1-P08c` — Opciones abiertas | Toda opción `OTRO` u `OTRA` exige una descripción breve no vacía; si no se selecciona la opción abierta, su texto debe quedar vacío |

Tres decisiones relacionadas siguen diferidas y denegadas por defecto — no existen todavía como capacidad del producto, por decisión explícita, no por omisión:

| Decisión diferida | Qué queda sin resolver |
| --- | --- |
| `D1-P04` | Traslado entre centros/unidades e inactivación o reactivación del residente |
| `D1-P05` | Cancelación excepcional de un borrador cuando su creador no está disponible, más allá del motivo trazable ya previsto |
| `D1-P06` | Actor, alcance y procedimiento para iniciar una rectificación de un basal firmado |

## Reglas de negocio adicionales

- Un basal firmado es inmutable: no se elimina ni se sobrescribe. Un error se corrige mediante una nueva versión de rectificación vinculada al original, con motivo, autoría y fecha propios.
- La ventana de corrección de seis horas, cuando existe para otros tipos de nota clínica, **nunca** se aplica al basal firmado.
- Medicina nunca obtiene el permiso de alta administrativa por el simple hecho de poder valorar o reevaluar el basal; son permisos independientes.
- Solo puede existir una versión basal vigente por residente; la firma y la sustitución de la versión vigente se ejecutan de forma atómica e idempotente.

## Trazabilidad

| Paso del flujo | Requisitos PRD | Pantalla de referencia |
| --- | --- | --- |
| Ficha del residente y estado del basal | `RES-01` a `RES-05` | ENF-17, ENF-18, MED-19, MED-20 |
| Crear borrador o reevaluar, un único borrador activo | `BAS-09` | ENF-19, ENF-20, MED-21 |
| Aportaciones de terceros | `BAS-11` | ENF-20 |
| Completar las nueve áreas | `BAS-01`, `BAS-15` a `BAS-18` | ENF-20, MED-21 |
| Completar Barthel común | `BAS-02`, `BAS-03` | ENF-21 |
| Retirada de CFS y Pfeiffer | `BAS-02` (retirado `BAS-08`) | ENF-22 |
| Firma del borrador | `BAS-10`, `BAS-13` | ENF-23 |
| Cancelación de borrador | `BAS-12` | ENF-20 |
| Inmutabilidad y rectificación | `BAS-14` | ENF-25, MED-23 |
| Historial del basal | `BAS-05` | ENF-25, MED-22 a MED-24 |

## Nota de procedencia

Este flujo consolida los requisitos `BAS-01` a `BAS-19` del PRD v0.5, las pantallas de basal de los wireframes funcionales de Enfermería v0.3 (ENF-17 a ENF-25) y Medicina v0.3 (MED-19 a MED-24), y el documento de decisiones de producto que cierra `D1-P01` a `D1-P08`, todos documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
