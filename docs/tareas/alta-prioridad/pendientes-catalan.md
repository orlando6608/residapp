# La aplicación en catalán y castellano: traspaso

**Estado a 2026-11-19: propuesta de Orlando, sin ADR ni código. Espera la respuesta de CJ al documento `docs/pendientes-cj/app-catalan-castellano-cj.html` (PR #338, 9 preguntas).** Mientras CJ no conteste y el DPO no lea el alcance nuevo, no se construye nada. CJ puede tardar varios días: este documento deja hecho lo que no depende de él.

## 1. Qué se quiere (Orlando, conversación del 2026-11-19)

- **Toda la aplicación de los centros en castellano y catalán desde el primer uso en Cataluña.** Cada persona elige el idioma de su pantalla (cómo y dónde se guarda: apartado 5, sin decidir).
- **Plataforma queda fuera** (la parte con la que ResidApp opera el servicio; no es personal de un centro): sigue solo en castellano.
- **Nadie está obligado a escribir ni a dictar en catalán.** Lo que escribe o dicta el personal no se traduce.
- **Todo lo que sirve al personal y a su adiestramiento, en catalán**, y en especial **el Manual** (será la referencia y la ayuda en producción). Eso incluye la Guía por perfil, el Flujo de trabajo, «Acceso y sesión» y las guías de pruebas, es decir, todo lo registrado en `.claude/documentacion-usuario.json`.
- **Todo lo que se imprima o salga en PDF sale en el idioma elegido** en la aplicación.
- Para los términos clínicos se consultan diccionarios (apartado 7) y los revisan personas de cada perfil; los valores clínicos no se tocan.

**Choque con lo vigente:** el [ADR 0013](../../decisiones-arquitectura/0013-portal-familiar-dos-idiomas.md) (aceptado el 2026-10-30) deja la parte profesional **fuera de alcance**, y la landing catalana y el correo en catalán avisan de que la aplicación está en castellano. Nada de eso se toca todavía: el ADR se reescribe (o se sustituye por uno nuevo) tras la respuesta de CJ y la consulta al DPO (apartado 3, paso 6). **Lo que ya vale de ese ADR:** el valor cerrado `es`/`ca`, no inferir el idioma (ni por nombre, IP o `Accept-Language`), el parámetro `?idioma=` que nunca escribe en la cuenta y la cookie solo de idioma en las pantallas de entrada.

## 2. Tamaño (medido el 2026-11-19 en el repositorio)

| Qué | Medida |
|---|---|
| Vistas Razor | 262 ficheros, unas 23.300 líneas (Administración 6.150; Manual en `Home` 2.420; Enfermería 1.840; Medicina 1.480; Dirección 1.380; Portal Familiar 1.050) |
| Controladores | unas 12.500 líneas (mensajes de error y de confirmación) |
| Guía por perfil | 9 ficheros JSON, unas 5.000 líneas |
| PDF | 2 ficheros con QuestPDF más el bloque de impresión del `_Layout` |
| Textos totales | **6.000 a 9.000** (estimación mía, no medida; el Portal Familiar y la entrada eran 300 a 400) |

Además de las vistas: textos en capas internas (`ApplicationResult` y los errores), enums de los `<select>`, catálogos en base de datos (tipos de comunicado, equipos), correos (ya previstos por idioma, ADR 0012), página `Offline` y `sw.js`.

## 3. Cuando CJ conteste (en este orden)

1. `git fetch` y mira `origin/main` y los PR abiertos (otras sesiones lo cambian). Comprueba que el PR #338 está desplegado en Azure dev y que CJ ha podido abrir el documento (la app le manda un correo al desplegar; no se ha verificado).
2. Orlando te avisa del correo «CJ ha respondido…». Baja las respuestas con `tools/respuestas-cj.ps1` (no leas ni imprimas la clave; ver `docs/pendientes-cj/README.md`) y lee `respuestas-cj-local/app-catalan-castellano-cj.respuestas.json`. **No rellenes lo que falte y no copies sus comentarios a ningún fichero del repositorio (es público).**
3. Qué decide cada respuesta:

   | Pregunta | Qué decide |
   |---|---|
   | 1.1 | Regla de desempate del glosario: forma oficial o forma de uso |
   | 1.2 | Cómo se escriben los títulos de las escalas con nombre propio (Barthel y otras) |
   | 1.3 | Lista de lo que no se traduce (términos, siglas, escalas, fármacos); es el arranque del glosario |
   | 2.1 | Quién revisa las pantallas en catalán (CJ, personas de cada perfil, traductor externo) |
   | 3.1 | Quién revisa el Manual y las guías |
   | 3.2 | Si CJ revisa cada plantilla de PDF o solo las clínicas o legales |
   | 3.3 | Si un informe en catalán con notas en castellano lleva aviso de idioma mixto |
   | 4.1 | Cuánto personal trabajaría en catalán (demanda real; no cambia lo propuesto) |
   | 4.2 | Qué área va primero |

4. Escribe las notas de ingeniería en `docs/pendientes-cj/app-catalan-castellano-cj.notas.json` (públicas: sin comentarios de CJ ni datos personales). Al desplegarlas, CJ recibe un correo.
5. **Pregunta a Orlando, de una en una (`AskUserQuestion`), lo que es suyo** (apartado 6).
6. **Consulta a `dpo-residapp` antes de tocar el ADR** (regla de `CLAUDE.md`), con las preguntas del apartado 4. Su borrador va en `docs/privacidad/` y lo valida una persona.
7. Reescribe el ADR 0013 (o escribe uno nuevo y marca el 0013 como sustituido en lo que cambie) y actualiza `docs/INDICE.md` y la landing (`docs/tareas/futuro/landing-textos-borrador.md`, fila Q9, y `pendientes-landing.md`).
8. Plan de construcción en PR pequeños (apartado 5) y consulta a `doc-usuario-residapp` y a `qa-revisor-residapp` en cada bloque.

## 4. Preguntas ya preparadas para `dpo-residapp`

1. **Idioma del personal.** ¿Es el mismo dato personal que el de las familias (valor cerrado, no inferido, solo lo cambia la propia persona o Administración como rectificación)? Sin sugerencia agregada a nadie: ¿cambia algo respecto a la lectura del 2026-10-29 (`docs/privacidad/portal-familiar-dos-idiomas.md`)? ¿Conservación, registro de actividades e información al personal?
2. **Pantallas de entrada compartidas** por personal y familias: la cookie de idioma y el parámetro `?idioma=` siguen la regla del ADR 0013; ¿algo más cuando es personal?
3. **PDF e impresión en el idioma elegido.** Si un mismo documento clínico puede salir en dos idiomas, ¿hay que fijar cuál es el que vale o dejar constancia en el documento? ¿Qué pasa con los textos legales y de privacidad en catalán (quién los revisa y cuál prevalece si discrepan)?
4. **Notas del personal en un idioma distinto al del documento** (3.3 del documento de CJ): ¿hace falta algún aviso por transparencia o por exactitud?
5. **Traducir con ayuda de IA durante el desarrollo** textos fijos de la interfaz (no hay datos de nadie): confirmar que no choca con el ADR 0011, que solo regula funciones del producto.
6. **Derechos lingüísticos** en Cataluña (ya abierto: `docs/privacidad/portal-familiar-dos-idiomas.md`, punto 4 de «A verificar»): ¿obligan a la residencia a atender en catalán a residentes y familias? Cambia el peso de la petición.
7. **Catálogos en base de datos** que se muestran en pantalla (tipos de comunicado, equipos): nombres en dos idiomas; ¿alguna cautela?
8. **Autoridad de control** (AEPD o APDCAT) según el centro piloto, ya abierto: revisar si cambia con un centro en Cataluña.

## 5. Propuesta técnica (de ingeniería, **no decidida**)

- **Un solo punto que decide el idioma de cada petición.** Plataforma siempre en castellano. Personal y familias: idioma de la cuenta; si no lo ha elegido, el del centro; antes de entrar, selector, parámetro o castellano (ADR 0013, punto 4).
- **Ficheros de traducción por área** (recursos de .NET o JSON) con **castellano como respaldo por clave**. La prueba de «los dos ficheros tienen las mismas claves» se exige solo en las áreas ya marcadas como traducidas, para poder entregar por áreas sin dejar nada a medias en una pantalla.
- **Fechas y números:** hoy `Program.cs` fija `es-ES` aunque el servidor sea otro (líneas 443 a 450). Decidir si en catalán se usa `ca-ES` (fechas como «19 de novembre de 2026»). Hay que probarlo, también en el contenedor Linux de Azure.
- **Textos fuera de las vistas:** mensajes de controladores, de `ApplicationResult` y de la capa de aplicación, enums de `<select>` y catálogos. Cada uno necesita una clave, no un texto fijo.
- **PDF (QuestPDF) e impresión del navegador** reciben el idioma de la persona que los saca. `PdfBranding.AddHeader` y el bloque de impresión del `_Layout` incluidos.
- **Manual y guías:** dos versiones de cada documento registrado en `.claude/documentacion-usuario.json`; el registro, el sello «Revisado para ResidApp vX.Y», el `data-version` y `GuiaDerivaTests` hay que pensarlos por idioma. Se escriben **a la vez que las pantallas de su área**, no al final.
- **Orden orientativo:** (1) mecanismo, entrada y Portal Familiar (pasos 1 y 2 del ADR 0013); (2) campo de la cuenta, selector y idioma del centro (script nuevo, siguiente número libre al empezar; hoy el `0105` está en uso en la rama de citas externas, comprueba `origin/main`); (3) correos por idioma (ADR 0012); (4) las áreas del personal en el orden de la respuesta 4.2, con su Manual y guía; (5) PDF. La sugerencia al personal del ADR 0013 (punto 5) queda en pausa mientras el DPO real no la apruebe.
- **Glosario:** un fichero versionado con cada término, su traducción, la fuente (TERMCAT, DEMCAT u otra) y quién lo revisó. Los términos clínicos los valida CJ o la persona que él indique.

## 6. Decisiones que son de Orlando (se preguntan de una en una)

1. **Quién revisa el catalán:** el ADR 0012 dice ingeniería más traductor externo y el ADR 0013 dice CJ. El documento de CJ pregunta a CJ; queda decidir si hay traductor externo y quién lo paga.
2. **Prioridad frente al resto:** PR 6 de citas externas (hito 0.5.0), antes de datos reales y esta tarea (que tiene que estar antes del primer centro con personal en catalán).
3. **Si el catalán es módulo base** (supuesto: sí, no se cobra aparte; ver `docs/producto/` sobre módulos base y opcionales).
4. **Dónde se elige el idioma del personal** (supuesto: «Mi cuenta», con el idioma del centro por defecto) y quién fija el idioma por defecto del centro.
5. **La versión que prevalece** si los textos legales discrepan, ya abierto en el ADR 0013.

## 7. Fuentes consultadas el 2026-11-19 (a verificar antes de apoyarse en ellas)

- **TERMCAT, Cercaterm** (buscador oficial de terminología, más de 400.000 conceptos): <https://www.termcat.cat/en/cercaterm>. Parte de sus datos están en abierto con licencia Creative Commons («Terminologia Oberta»); **no he podido confirmar que valga para todo Cercaterm**. Consultar y citar está bien; no copiar en masa sin leer sus condiciones.
- **DEMCAT** (Diccionari enciclopèdic de medicina, con la Acadèmia de Ciències Mèdiques, el IEC, Enciclopèdia Catalana y el TERMCAT; actualización de diciembre de 2024, unos 75.000 términos): <https://docs.academia.cat/noticies/4311/1/g319-20241203-actualitzaciodemcat.pdf>.
- **Cosnautas** ofrece unos 130.000 términos médicos de Cercaterm: <https://www.termcat.cat/sites/default/files/2023-10/G319_20231018_CercatermCosnautas_cat.pdf>.
- **Uso real del catalán en los centros** (Gordillo, *Llengua i Ús*, historias de 2003, muy antiguo): el 74 % de los impresos y plantillas estaban en catalán, pero menos del 23 % de los documentos se rellenaban en catalán: <https://raco.cat/index.php/LlenguaUs/article/download/128199/177728>.
- **No encontré** (búsqueda del 2026-11-19) cómo han resuelto la interfaz otras aplicaciones sanitarias bilingües de Cataluña (La Meva Salut, E-CAP, HC3), ni una traducción validada al catalán del Barthel, el Lawton o el Pfeiffer. Para saberlo habría que preguntar al Servei Català de la Salut, al ICS o al TERMCAT (da asesoramiento), o a quien CJ conozca.

## 8. Qué no hacer

- No construir nada antes de la respuesta de CJ y de la lectura del DPO.
- No traducir ni «normalizar» valores clínicos, rangos ni umbrales; solo los nombres con que se muestran. Un término clínico dudoso se pregunta, no se inventa.
- No tocar Plataforma.
- No traducir lo que escribe el personal, ni con IA (el ADR 0011 no lo aprueba).
- No deducir el idioma de nadie por su nombre, su IP, el centro ni `Accept-Language`.
- No dejar una pantalla a medias: se entrega por áreas completas.
