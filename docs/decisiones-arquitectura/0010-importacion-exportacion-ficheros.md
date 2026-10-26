# ADR 0010 — Importación y exportación por ficheros, con emparejamiento de columnas sin IA

Estado: aceptada el 2026-10-21 (decisión del usuario). Implementada el 2026-10-21 (script `0060`).

## Contexto

El estudio de mercado (`docs/producto/estudio-mercado-ux-2026-10.md`) señala como debilidad n.º 1 el **doble registro**. Las residencias
ya usan un programa de gestión (ResiPlus, Gerosalus, Ekon, Geriges…), y ResidApp no se integraba con nada. Su recomendación R1 es importar
residentes y ubicaciones y exportar. Poner en marcha un centro exigía crear a mano, uno a uno, unidades, edificios, plantas, habitaciones,
camas y residentes.

Se estudió usar la API de Mistral (la misma del ADR 0009) para emparejar las columnas del fichero con los campos de ResidApp. Se descartó.
El problema es pequeño y cerrado (unos diez campos), y un emparejamiento determinista lo resuelve igual o mejor:

- es predecible y se prueba con exactitud;
- no hay ningún tercero en el tratamiento: ni contrato de encargado, ni obligaciones del Reglamento de IA, ni ampliación de la EIPD;
- no depende de una red, una clave, un coste por uso ni la disponibilidad de un proveedor.

La IA se reserva para lo que no tiene alternativa con reglas: el dictado por voz del ADR 0009.

## Decisión

**Ficheros, no API.** Se importan y exportan ficheros CSV, XLS y XLSX desde Administración. Una API de integración tendrá que esperar a la
identidad real (ADR 0007) y queda fuera de este ADR. Es coherente con el monolito y con «no crear API REST» del legado.

**Qué se importa:**

- **Estructura:** unidad (nombre y código), edificio, planta, habitación y plaza (cama).
- **Residentes:** nombre (o nombre y apellidos, que se unen), fecha de nacimiento, sexo documentado, unidad, habitación, plaza y referencia
  interna del programa de origen.

**Qué no se importa todavía:**

- **Usuarios ni trabajadores.** `cuentas.sujeto_externo` es inmutable (`0023`) y su formato futuro (`<tenant>|<oid>`, ADR 0007) no pasa la
  validación actual. Importarlos ahora dejaría identificadores definitivos sin proveedor de identidad. Equipos y turnos dependen de
  cuentas.
- **Familiares.**
- **Nada clínico.** Exportar registros asistenciales hacia el programa de gestión (la otra mitad de R1) sigue fuera del alcance
  (`alcance.md`: integraciones con historias clínicas externas).

**Emparejamiento automático y determinista**, en este orden:

1. **Plantilla del centro.** Si llega un fichero con la misma firma de cabeceras (los nombres de columna normalizados, en orden), se
   aplica su plantilla entera.
2. **Sinónimos.** La cabecera normalizada (sin acentos, mayúsculas ni signos: «F. Nac.» → «f nac») es igual a un sinónimo del catálogo
   (`ExchangeCatalog`) o lo contiene como palabras seguidas. Gana el sinónimo más largo, y si empatan dos campos no se empareja.
3. **Formato de los datos.** La única columna con todo fechas es la fecha de nacimiento, y la única con pocos valores, todos de sexo, es el
   sexo.
4. **El resto, a mano**, con un desplegable. Lo propuesto lleva la marca «Emparejado automáticamente» para que se revise.

Si un programa usa nombres que no están en el catálogo, se empareja a mano una vez y se guarda como plantilla. Si se repite en varios
centros, se añade al catálogo.

**Flujo de importación:** analizar, ajustar, vista previa fila a fila (se importará, ya existe, error con su motivo) e importar.

- La vista previa y la importación se recalculan en el servidor a partir del fichero.
- Se importa fila a fila **con los casos de uso de siempre**: las mismas validaciones, disparadores, seguridad por filas y auditoría que el
  alta a mano. Una fila con error no aborta las demás ni deja nada a medias.
- **Lo que ya existe no se duplica:**
  - la estructura se reconoce por su nombre normalizado;
  - el residente, por su referencia interna entre los activos del ámbito (CJ, 2026-10-25); con el mismo nombre y fecha de nacimiento pero otra referencia, la
    fila es un «posible duplicado» que no se importa salvo que quien importa lo confirme fila a fila, porque dos personas distintas pueden llamarse igual;
  - el alta lleva un identificador de operación derivado de la fila, para que un doble envío no la repita.
- La importación de residentes no crea estructura: se importa antes, incluso desde el mismo fichero.
- Las fechas se leen como día/mes/año o mes/día/año, y año-mes-día siempre. Si son ambiguas, se avisa.
- El sexo nunca se deduce de otro dato. Los valores se reconocen (H/M, V/M, M/F, Hombre…) o se asignan a mano, y la celda vacía es «No
  consta» (la vista previa cuenta cuántos quedan así).
- Si el fichero trae la fecha de ingreso del residente, se guarda (`residentes.fecha_ingreso`); si no, queda sin ella.

**Exportación:**

- Estructura y censo de residentes activos del ámbito: solo identidad administrativa, ubicación y referencia interna.
- En CSV como lo abre Excel en España (UTF-8 con BOM, «;»), con protección contra la inyección de fórmulas.
- Con las columnas de ResidApp, que son las mismas que la plantilla de importación, o con las de una plantilla de exportación creada a
  partir de un fichero de ejemplo de la otra aplicación (solo se leen sus cabeceras).
- Solo se descarga: sin correo ni enlaces. Se audita (`EXPORT_RUN`) antes de entregar el fichero; si no se puede auditar, no se exporta.

## Protección de datos

- **El fichero no se guarda.** Se queda en el navegador y se reenvía en cada paso (`wwwroot/js/intercambio.js`). Se procesa en memoria en
  el servidor, y las columnas que no se emparejan se descartan al leer.
- **Minimización en pantalla.** El análisis no enseña los valores del fichero, solo qué parecen contener y cuántos distintos hay. La vista
  previa enseña lo que se va a importar de cada fila.
- **Las plantillas (`plantillas_intercambio`, script `0060`) no guardan datos personales:** solo cabeceras, campos y valores de sexo
  normalizados. Son del centro y están bajo la seguridad por filas.
- **Auditoría sin datos ni recuentos:** `IMPORT_RUN`, `EXPORT_RUN` y `EXCHANGE_TEMPLATE_SAVE`, en la categoría «Importación y exportación»
  de la auditoría de Administración. Cada alta y cada elemento de estructura creado se audita además como siempre.
- **Solo Administración**, con su ámbito.

## Límites

- 5 MB, 2.000 filas y 60 columnas por fichero; se lee la primera hoja.
- El tipo de fichero se reconoce por su contenido, no por la extensión.
- Los macros nunca se ejecutan: ExcelDataReader 3.9.0 (MIT) solo lee valores.

## Consecuencias

- Los duplicados solo se detectan entre los residentes activos del ámbito de quien importa.
- El episodio del residente importado empieza en la fecha de importación, porque el alta no recibe una fecha de ingreso anterior.
- Lo supuesto al construirlo lo confirma CJ en `docs/pendientes-cj/importacion-exportacion-confirmaciones-cj.html` (no bloquea).
- Una unidad que ya existe no se cambia de edificio ni de planta desde la importación; lo inactivo no se reactiva.
