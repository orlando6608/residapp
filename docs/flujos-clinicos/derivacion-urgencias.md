# Derivación común a Urgencias

Enfermería y Medicina reutilizan un único módulo funcional para documentar la derivación de un residente a un servicio de urgencias externo, con un informe firmado y en PDF vinculado al evento que la originó.

## Alcance y exclusiones

Este fichero documenta solo el módulo de derivación en sí. No repite cómo se llega hasta aquí: ver [valoracion-escalado-enfermeria.md](valoracion-escalado-enfermeria.md) y [valoracion-conducta-medicina.md](valoracion-conducta-medicina.md) para el flujo completo del protocolo urgente que abre este módulo.

## Glosario mínimo

- **Informe de derivación**: documento que se genera al derivar a un residente a Urgencias, con la información clínica relevante.
- **Vista previa**: revisión obligatoria del informe antes de firmarlo.
- **Firma**: acción que fija el contenido del informe y genera el PDF definitivo.

## Flujo paso a paso

1. El punto de entrada es siempre el protocolo urgente activado desde el flujo de Enfermería o desde el flujo de Medicina; el módulo es idéntico para ambos perfiles.
2. Se genera una vista previa obligatoria del informe antes de poder firmarlo.
3. El profesional puede completar o editar el texto del informe; editarlo no altera los registros de origen (observación, valoración, constantes) de los que se nutre.
4. El profesional firma el informe.
5. Se genera un PDF firmado, vinculado de forma trazable al evento clínico que originó la derivación, y queda accesible desde el Historial del residente.

## Contenido del informe

El informe de derivación reúne: identificación del residente y del centro, el basal relevante, la puntuación de Barthel, cognición y comunicación documentadas, el motivo y la observación de origen, las valoraciones registradas, las constantes, la oxigenoterapia si procede, las actuaciones realizadas, la evolución, y el profesional firmante.

## Reglas de negocio

- El informe nunca incluye la escala CFS: esta escala fue retirada por completo del producto y no existe como campo, puntuación, resumen ni contenido de informe.
- Los servicios contactados y las horas de contacto se conservan en la trazabilidad interna del sistema, pero no forman parte del bloque del informe que se entrega externamente.
- El comportamiento del módulo es idéntico se active desde Enfermería o desde Medicina: mismas pantallas, mismo contenido, misma vista previa obligatoria.
- Toda derivación genera una actualización relevante para la familia y exige documentar el intento de llamada al contacto familiar designado, sin que esa documentación retrase la atención al residente.

## Trazabilidad

| Paso del flujo | Requisitos PRD | Pantalla de referencia |
| --- | --- | --- |
| Punto de entrada desde protocolo urgente | `DER-01` | ENF-11 (Enfermería), MED-13 (Medicina) |
| Vista previa y edición del texto | `DER-02` | ENF-12, MED-14 |
| Contenido del informe | `DER-03` | ENF-12, MED-14 |
| Trazabilidad interna de contactos y horas | `DER-04` | ENF-12, MED-14 |
| Firma y PDF vinculado al evento | `DER-05` | ENF-12, MED-14 |
| Actualización relevante e intento de llamada | `DER-06` | ENF-14, MED-16 |

## Nota de procedencia

Este flujo consolida los requisitos `DER-01` a `DER-06` del PRD v0.5 y las pantallas de derivación de los wireframes funcionales de Enfermería v0.3 (ENF-11, ENF-12) y Medicina v0.3 (MED-13, MED-14), documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
