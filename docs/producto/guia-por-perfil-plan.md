# Plan: «Guía por perfil» de ResidApp

> Copia del plan de trabajo de la Guía por perfil (2026-11-05), con las decisiones D1-D10 ya cerradas (§8.1). Hecho: fase 0 (estructura, PLA-02 y control de deriva), fase 1 (guías de Plataforma y de Auxiliar completas, 8 fichas cada una) y, de la fase 2, las guías de Familia (8 fichas), Medicina (13), Enfermería (19) y Dirección (14), el capítulo «Para todos» (11 fichas TOD, `Guia/comun.json`) y, de la fase 3, las tres entregas de Administración (46 fichas: centro, estructura, personas y accesos, residentes y familias, comunicados, citas y operación) y, de la fase 4, los anexos: índice de preguntas, matriz «quién entrega qué a quién», glosario y la guía completa para imprimir (`/Guia/todo`), el generador de PDF (`tools/GuiaPdf`) y la revisión de punta a punta. Los enlaces desde el pie, el Manual, Acceso y el Flujo ya están. Falta el sellado (§5).

> Decisiones D1-D10 cerradas el 2026-11-05 (ver §8.1). No se toca el Manual, el Flujo ni Acceso y sesión sin tu visto bueno; las guías se escriben fase a fase (§5).

## 0. Resumen en diez líneas

- **Qué:** un documento «Guía por perfil» con 7 guías (Plataforma, Administración, Auxiliar, Enfermería, Medicina, Dirección Clínica, Familia) más un capítulo «Para todos» (entrar, cambiar ámbito, Mi cuenta, passkeys…) y tres anexos (matriz «quién entrega qué a quién», glosario, índice de preguntas).
- **Cómo:** cada tarea es una **ficha estructurada** (datos en JSON, una ficha = una pregunta «¿Cómo…?»), que una **vista Razor** dibuja en pantalla y en papel. Los nombres de perfiles y de permisos salen del propio código, no se escriben a mano.
- **Por qué así:** el Manual y el Flujo ya se han desalineado entre sí y con el código (§1: más de 15 incoherencias). Con fichas estructuradas, un **test automático** puede detectar deriva (un perfil o permiso sin ficha, una ruta o un botón que ya no existe, una referencia al Manual rota). Con HTML escrito a mano, no.
- **Tamaño:** unas **120–130 fichas**; impreso, unas 90–130 páginas (Administración es casi la mitad).
- **Decisiones que necesito de ti:** 10, en §8, cada una con mi propuesta por defecto.

---

## 1. Qué he leído y qué he encontrado

### 1.1 Lectura realizada

| Fuente | Cómo se leyó |
|---|---|
| `src/ResidApp.Web/Views/Home/Manual.cshtml` (1.793 líneas) | Entero |
| `src/ResidApp.Web/wwwroot/flujo-de-trabajo.html` (1.842 líneas) | Entero |
| `src/ResidApp.Web/wwwroot/acceso-y-sesion.html` (1.205 líneas) | Entero |
| Código: `SystemProfile`, `ProfilePermissions`, políticas de `ResidentBaselinePolicy`, 15 controladores y sus vistas por perfil, `_Layout`, `site.css`, `PdfBranding`, `Program.cs`, tests de barrido existentes | Dirigida a lo que afecta a la Guía |
| `.claude/agents/doc-usuario-residapp.md`, `CLAUDE.md`, `FlujoDeTrabajoPageTests.cs` | Entero |
| Vistas de Plataforma (`NuevoCentro`, `Centro`) y `HojaInvitacion` | Entero (base de la ficha de ejemplo, §3) |

Lo que **no** he podido comprobar leyendo: lo que depende de JavaScript, de la sesión, del código del móvil o de passkeys (se verificará con la skill `probar-en-navegador` al redactar cada perfil).

### 1.2 Datos de partida útiles

- **Perfiles en el código:** `SystemProfile` (`src/ResidApp.Shared/SystemProfile.cs`) tiene 7 valores: Auxiliar, Enfermería, Medicina, **Familiar**, Administración, **Dirección Clínica**, Plataforma. Etiquetas en pantalla: `SystemProfileDisplay.Label` (`ProfileScopeModels.cs:17-29`).
- **Permisos configurables** (`ProfilePermissions.cs:11-32`; etiquetas en `DireccionModels.cs:131-142`). Ningún perfil los tiene por defecto; solo los concede **Administración**, en *Usuarios → Gestionar → Permisos → «Conceder»/«Revocar»* (`PerfilUsuario.cshtml:200-238`):

| Perfil | Permiso (código) | Etiqueta en pantalla |
|---|---|---|
| Enfermería | `RESIDENT_IDENTITY_CREATE` | Dar de alta residentes |
| Enfermería | `BASELINE_INITIAL_COMPLETE` | Basal inicial (al alta) |
| Enfermería | `BASELINE_REEVALUATE` | Reevaluar el basal |
| Enfermería | `RESIDENT_PHOTO_MANAGE` | Poner, cambiar y quitar la foto del residente |
| Medicina | `BASELINE_INITIAL_COMPLETE` / `BASELINE_REEVALUATE` | Basal inicial / Reevaluar el basal |
| Dirección Clínica | `CLINICAL_DETAIL_READ` | Lectura clínica detallada y auditada |
| Dirección Clínica | `REFERENCE_RANGES_MANAGE` | Gestionar los rangos de referencia de constantes |
| Dirección Clínica | `PROCESS_DEADLINES_MANAGE` | Gestionar los plazos de los hitos del proceso |
| Dirección Clínica | `QUALITY_REVIEW` | Ver los indicadores y la calidad de proceso |

  Plataforma, Administración, Auxiliar y Familiar no tienen permisos configurables. (Existe `BASELINE_DRAFT_CONTRIBUTE` en base de datos pero no se ofrece.)
- **No hay menú por perfil:** un único enlace «Mi panel» lleva al inicio de cada perfil (`_Layout.cshtml:29-39`); el resto son las tarjetas del inicio. La Guía se organiza igual (inicio → tarjeta → pantalla).
- **Versión de la app (actualizado 2026-11-05, PR #146):** ya hay versionado semántico. La versión vive solo en `src/Directory.Build.props` (hoy `0.1.0`, release-please la sube al cerrar un hito; no se edita a mano sin tu visto bueno). Cada documento registrado lleva el sello **«Revisado para ResidApp vX.Y.Z»** y el campo `revisadoPara` en `.claude/documentacion-usuario.json`. El pie de `_Layout` muestra la versión **solo con sesión iniciada**. Ver D4.
- **PDF en servidor:** solo el informe de derivación (MigraDoc/PDFsharp, `PdfBranding.AddHeader`). **Impresión en navegador:** `site.css:29-56` (cabecera de marca repetida) y `tema.js` (fuerza modo claro al imprimir). Ni el Manual ni el Flujo usan `@page`.

### 1.3 Incoherencias y huecos (clasificados)

**A. De nombres de perfil**

| # | Dónde | Qué ocurre |
|---|---|---|
| A1 | Manual L144, L1654, L1671 | «Dirección / Coordinación Clínica» frente a «Dirección Clínica» (resto del Manual, Flujo y UI). Manual L993 y L1788 admiten que aún no se sabe cómo distinguir la «Coordinación Clínica». El código lo repite en `Home/Index.cshtml:130,142` y `RangosReferencia/Index.cshtml:22,39`. |
| A2 | Flujo L733, 1551, 1626, 1653, 1736 | «Dirección» a secas. |
| A3 | Código frente a documentos | La UI y el enum dicen **«Familiar»**; el Manual y el Flujo alternan «Familiar» y «Familia» (perfil) y usan «familiar» también para la persona de la ficha. |
| A4 | Flujo L1126 | «La Auxiliar» (resto del Flujo y de Acceso: «el Auxiliar»). |
| A5 | Manual L1341 vs L1406 | Orden distinto: «Enfermería y Medicina» / «Medicina y Enfermería». |
| A6 | Acceso L413, L463 | «Plataforma · soporte» y «Operador de Plataforma»; Manual: «soporte». |

**B. De permisos**

| # | Dónde | Qué ocurre |
|---|---|---|
| B1 | Manual, Flujo, Acceso | Hablan de **un solo «permiso de basal»**; el código tiene **dos** (inicial y reevaluar) y, además, para Enfermería no se mencionan en ningún sitio como permisos separados (Acceso A:945/950, Flujo F:1144, Manual L941-944). |
| B2 | Flujo F:437, F:439 | Las tarjetas de Administración y Enfermería no recogen que Enfermería, con permiso, da de alta residentes y gestiona la foto (sí lo dice de pasada F:1120/1125). |
| B3 | Manual L13 vs L17 vs L658 | Enfermería «estado basal completo» sin permiso en L13; con permiso en L658/L941. |
| B4 | Manual L660 / L1672-1673 / L1654 | Redacción confusa sobre quién tiene el permiso de rangos; en el código solo Dirección Clínica (migración 0039). |
| B5 | `RangosReferencia/Index.cshtml:20-22,38-39` | **Texto del código incorrecto**: dice «Medicina o Dirección/Coordinación Clínica». *(fuera de alcance: se menciona, no se corrige)* |
| B6 | `Enfermeria/Residente.cshtml:53-55` | El botón «Crear borrador de basal»/«Iniciar reevaluación» sale siempre a Enfermería; el permiso se exige al crear el borrador. En Medicina el botón sí depende del permiso. Es coherente con `gestion-basal-barthel.md` línea 24 («siempre que tenga el permiso correspondiente»), pero la Guía debe decirlo. |
| B7 | Flujo F:441 / Acceso A:938-940 | «Indicadores» (`QUALITY_REVIEW`) falta en Acceso; en el Flujo, la lectura clínica se presenta como «finalidad declarada», no como permiso. |

**C. De estructura y mantenimiento**

| # | Dónde | Qué ocurre |
|---|---|---|
| C1 | Manual | **Sin numeración** (ni de títulos ni del índice) → «ver Manual, apartado 14» no existe. 36 entradas de índice en orden distinto al del cuerpo (Citas: índice L81, cuerpo L551); `mi-cuenta` (L164) no está en el índice. |
| C2 | Manual / Flujo / Acceso | Sin versión de la app. Fechas: Flujo «31/10/2026», Acceso «30/10/2026», Manual sin fecha. |
| C3 | Flujo vs Acceso | El CSS está **copiado** en cada archivo y ya ha divergido: Acceso no define `--lane-aux-bg`, `--lane-enf-bg`, `--lane-med-bg` ni `--lane-dir-bg`. El pie del Flujo no enlaza a Acceso y sesión. |
| C4 | Flujo | Cuatro numeraciones independientes (mapa 1-12, hitos de Rosa 1-23, «Paso a paso» por sección, números internos de cada diagrama). Citar «paso N del Flujo» en papel sería frágil: la Guía citará **sección**, no número de paso. |
| C5 | Flujo | Dos secciones sin entrada en el índice (`leer`, `todavia`). El HTML se generó con un script que no está en el repo (F:5-6). |
| C6 | Textos de botones | La invitación se llama «Crear invitación de acceso o de recuperación» (Plataforma, `Centro.cshtml:58`), «Invitación de recuperación» (Plataforma, Cuentas) y «Crear invitación de acceso» (Administración). «Imprimir» (hoja de invitación) frente a «Imprimir / guardar como PDF» (Indicadores). «Entrar con otra cuenta» y «Cambiar ámbito». La Guía debe usar cada texto **exacto** en su pantalla. |
| C7 | Manual en papel | El índice lateral **no se oculta al imprimir**; no hay `@page` ni saltos de página; no hay cabecera con fecha/versión. |
| C8 | Hueco | Ningún documento cuenta **qué ve Administración en un centro recién creado** (solo se deduce de L664-666, L102-105 y L839-842). Es justo lo que pide la prueba de aceptación. |
| C9 | Código (fuera de alcance) | `SystemProfile.cs:3` dice «Los 6 perfiles» y son 7. |

**D. Contenido que no es de «usuario real»** (a excluir de la Guía; propuesta de destino en D5)

- **Manual:** aviso inicial L30; L176, L182-186 («Windows de Orlando»); sección entera `cuentas-prueba` (L228-349: cuentas de CJ y de Orlando, residentes ficticios, `olmos-*`, `varios-centros`, `plataforma-cj`, recorrido con Lucía); referencias a «CJ» (L546, 840, 1095, 1099, 1665); «en las pruebas…» (L475, 580, 595); `almendros-medicina`/`almendros-direccion` (L1389, L1679); «política provisional» (L1647); «solo quien administra la base de datos» (L912).
- **Flujo:** comentario de desarrollo F:2-8; «Para… quien prueba la aplicación» F:407; «Ver los plazos de CJ» F:1023, 1768.
- **Acceso:** sección `#cuentas` (A:918-979), `#mantener` (A:1072-1197), tabla con «CJ / Orlando» (A:470-481), A:315, 347, 372, 526, 806, 827.

---

## 2. Formato y ubicación técnica

### 2.1 Opciones

| | A. HTML estático a mano (como el Flujo) | B. Vista Razor + fichas en JSON (**recomendada**) | C. Generador que escribe HTML estático |
|---|---|---|---|
| Esfuerzo inicial | Bajo | Medio | Medio-alto |
| Mantener ~125 fichas | Alto: maquetado repetido a mano, 3 índices y la matriz a mano | **Bajo:** la plantilla es una sola; índices y matriz salen de los datos | Bajo, pero hay que acordarse de regenerar |
| Control de deriva | Casi imposible (hay que analizar HTML libre) | **Fácil:** el test lee JSON, no prosa | Fácil, más un test de «regenerado = commiteado» |
| Nombres de perfil/permiso | A mano → derivan | **Se leen del código** (`SystemProfileDisplay`, catálogo de permisos) | Igual que B, en tiempo de generación |
| Imprimir un solo perfil | Un archivo por perfil (copias del CSS) | **Ruta por perfil** (`/Guia/Plataforma`), misma plantilla | Un archivo por perfil |
| Riesgo | Deriva y errores de copia | Tocar `Program.cs`/controlador nuevo; hay que redesplegar para cambiar texto (igual que el Manual) | Archivos generados que caducan; dos fuentes de verdad |

**Recomendación: B.** Mínimo código (sin dependencias nuevas: `System.Text.Json` ya viene con .NET), y es lo que hace viable el control automático de deriva (§6.c).

### 2.2 Estructura propuesta (todo nuevo; nada existente se modifica en la primera entrega)

```
src/ResidApp.Web/Guia/                      ← datos (JSON, copiados a la salida como los docs de CJ)
  comun.json  plataforma.json  administracion.json  auxiliar.json
  enfermeria.json  medicina.json  direccion.json  familia.json
  glosario.json
src/ResidApp.Web/Controllers/GuiaController.cs   ← [AllowAnonymous] como Home (decisión D9)
src/ResidApp.Web/Views/Guia/
  Index.cshtml        (portada + índice por perfil)
  Perfil.cshtml       (una guía: intro, rutina, diagrama, siempre/con permiso/nunca, fichas)
  Preguntas.cshtml    (índice alfabético de «¿Cómo…?»)
  Matriz.cshtml       (quién entrega qué a quién, generada de `destinatarios`)
  Glosario.cshtml
  Todo.cshtml         (imprimir todo: cada perfil en página nueva)
  _Ficha.cshtml  _Diagrama-<perfil>.cshtml (7 diagramas SVG a mano)
src/ResidApp.Web/wwwroot/css/guia.css       ← colores por perfil (mismos hex que el Flujo) + impresión
tests/FunctionalTests/GuiaDerivaTests.cs    ← control de deriva (§6.c)
```

Rutas: `/Guia`, `/Guia/{perfil}`, `/Guia/Preguntas`, `/Guia/Matriz`, `/Guia/Glosario`, `/Guia/Todo`. Enlaces desde el pie de `_Layout.cshtml` (junto a Manual, Flujo y Acceso) y desde cabeceras de Manual y Flujo.

### 2.3 Apoyo gráfico sin capturas

1. **Diagrama «De quién depende y a quién le entrega trabajo»** por perfil: SVG a mano en el estilo del Flujo (calles y cajas con las mismas clases `box`/`flecha`), 7 en total.
2. **Tira de destinatarios en cada ficha** (chips de color por perfil: «Plataforma → Administración») generada de los datos: no se dibuja nada, no puede quedar obsoleta.
3. **Mini-esquema de pantalla** (opcional, solo en fichas complejas): cajas con los nombres de botones, dibujadas con CSS desde los datos. Se decide ficha a ficha; no es obligatorio.
4. Colores por perfil: los **mismos hex que el Flujo** (Plataforma `#4a5560`, Administración `#2e6b2f`, Auxiliar `#6b4fa0`, Enfermería `#1b5f6b`, Medicina `#2457a6`, Dirección Clínica `#9b2c4a`, Familia `#8a5a12`, con sus fondos y variantes oscuras). Un test compara los hex de `guia.css` con los del Flujo para que no diverjan (aprendizaje de C3).

### 2.4 Impresión, numeración y PDF

- **Códigos de ficha estables, no números de página:** `PLA-01`, `ADM-17`… Nunca se renumeran ni se reutilizan (el test lo comprueba con una lista de códigos retirados). En papel se busca por código.
- **Índice de preguntas** alfabético con el código y el perfil; **índice por perfil** al inicio de cada guía.
- **Referencias legibles sin clic**, impresas como texto: *«Manual de usuario, apartado «Centros nuevos» (/Home/Manual#plataforma-centros)»* y *«Flujo de trabajo, sección «Antes del primer residente: un centro nuevo» (/flujo-de-trabajo.html#centro)»*. El test comprueba que el título citado coincide con el `<h2>` real (D3 decide si además se numera el Manual).
- **CSS de impresión** (`guia.css`): `@page { size: A4; margin }`; cada perfil con `break-before: page`; `.ficha { break-inside: avoid }`; títulos con `break-after: avoid`; modo claro forzado (la misma norma de `tema.js`); se oculta navegación e índice lateral (`d-print-none`); **etiquetas de texto además de color**: `[SIEMPRE]`, `[REQUIERE PERMISO: Reevaluar el basal · lo concede Administración]`, `[NUNCA]`, más forma/icono distinto, para blanco y negro.
- **Un solo perfil:** `/Guia/Plataforma` → Ctrl+P imprime solo esa guía (portada, índice del perfil, fichas, glosario abreviado de los términos que usa).
- **Cabecera/pie con fecha de revisión y versión:** pie fijo (`position: fixed` en `@media print`, se repite en cada página en Chrome/Edge/Firefox): «Revisado para ResidApp v0.1.0 · Estado a 05/11/2026 · Guía por perfil — <perfil>». Es el **sello de revisión** de la convención nueva (como en el Manual y el Flujo), **no** la versión en ejecución: sale del campo `revisadoPara` del JSON de cada perfil (que debe coincidir con el del registro) y la **fecha** del campo `revisado`. La Guía es pública y el pie de la app oculta la versión sin sesión, así que **no lee** la versión del ensamblado.
- **Número de página:** CSS puro no lo da de forma fiable en todos los navegadores (los cuadros de margen `@bottom-center` solo en Chrome/Edge recientes; `target-counter()` para «página X» en el índice no existe en Chrome). Por eso la unidad de búsqueda es el **código**, no la página.
- **PDF:** v1 = «Imprimir → Guardar como PDF» desde el navegador (cero código). v2 (opcional, D7) = script con Playwright (ya usado por la skill `probar-en-navegador`) que genera un PDF por perfil con «Página X de Y» y un índice con números de página (dos pasadas: se renderiza, se mide en qué página cae cada código y se inyecta en el índice). Coste ~1 sesión; solo vale la pena si vais a repartir papel.

---

## 3. Plantilla de ficha y ficha de ejemplo real

### 3.1 Esquema (cada ficha, un objeto del JSON del perfil)

```jsonc
{
  "codigo": "PLA-02",                      // estable, único, nunca reutilizado
  "pregunta": "¿Cómo creo un centro nuevo?",
  "perfil": "Plataforma",                  // debe existir en SystemProfile
  "palabras": ["alta de centro", "primer administrador", "unidad", "logo"],   // para el índice
  "permisos": [],                          // códigos de ProfilePermissions; vacío = siempre
  "concede": null,                         // quién concede el permiso (si lo hay)
  "antes": ["…"],                          // datos, permisos, tareas previas de otros perfiles
  "pasos": [ { "n": 1, "texto": "…", "botones": ["Nuevo centro"] } ],
  "despues": { "texto": "…", "destinatarios": ["Administración"] },
  "dudas": [ { "p": "…", "r": "…" } ],
  "rutas": ["Plataforma/NuevoCentro", "Plataforma/Centro"],   // para el agente y para el test
  "mensajes": ["Ya existe un centro con ese código…"],       // textos del servidor citados literalmente
  "manual":  { "id": "plataforma-centros", "titulo": "Centros nuevos" },
  "flujo":   { "id": "centro", "titulo": "Antes del primer residente: un centro nuevo" },
  "acceso":  { "id": "pedir", "titulo": "Cómo se pide el acceso" }
}
```

Y por perfil: `proposito`, `rutina`, `dependeDe`, `entregaA`, `siempre`, `conPermiso`, `nunca`, `revisado`.

### 3.2 Ficha de ejemplo — PLA-02 «¿Cómo creo un centro nuevo?» (Plataforma)

*Construida con `NuevoCentro.cshtml`, `NewCenterFormModel`, `NewCenter.Validate`, `PlataformaController`, `Centro.cshtml`, `HojaInvitacion.cshtml` y el apartado «Centros nuevos» del Manual (L889-926). Pendiente de comprobar en navegador al redactar: que el primer administrador queda como «Administración principal» (lo dice el Manual L664; no lo he verificado en el repositorio SQL) y la pantalla exacta que ve Administración al entrar.*

> **PLA-02 · ¿Cómo creo un centro nuevo y qué datos necesito?** &nbsp; `[SIEMPRE]` (sin permiso configurable) &nbsp; Plataforma → **Administración**
>
> **Qué necesito antes**
> - Estar dentro con el perfil **Plataforma** (se entra con *passkey*; ver ACC-03).
> - Del **centro**: un *código* (2 a 64 caracteres: letras sin acentos, dígitos, «-» o «_»; se guarda en mayúsculas; **no se puede cambiar después**; no puede ser «PLATAFORMA») y un *nombre* (hasta 200 caracteres).
> - De su **primera unidad**: código (mismas reglas) y nombre.
> - Del **primer administrador**: un *identificador de acceso* (3 a 200 caracteres; letras, dígitos y `. _ - @`, sin espacios; **no puede ser el de una cuenta que ya exista**) y su nombre.
> - Opcional, el **logo**: PNG o JPEG de hasta 512 KB y 1.024 px por lado. Después lo cambia la Administración del propio centro.
> - Una forma de **entregar la invitación en mano** al administrador (impresora o pantalla para enseñarle el código QR).
>
> **Pasos**
> 1. En tu inicio, abre «**Centros**» y pulsa «**Nuevo centro**».
> 2. En «**Centro**» escribe «Código del centro» y «Nombre del centro»; si quieres, adjunta «Logo del centro (opcional)».
> 3. En «**Primera unidad**» escribe «Código de la primera unidad» y «Nombre de la primera unidad».
> 4. En «**Primer administrador**» escribe «Identificador de acceso del administrador» y «Nombre del administrador».
> 5. Pulsa «**Crear centro**». Se crea todo a la vez o nada: el centro, la unidad y la cuenta del administrador (activa, con perfil Administración y la unidad concedida).
> 6. Verás el aviso verde «Centro creado…». En el centro nuevo, pulsa «**Administraciones y unidades**».
> 7. En la tarjeta del administrador, pulsa «**Crear invitación de acceso o de recuperación**». Sale la hoja con el usuario, el centro, «Vale hasta» (72 horas, una sola vez) y un código QR.
> 8. Pulsa «**Imprimir**» (o enséñale la pantalla) y **entrégasela en mano**.
>
> **Qué pasa después y a quién le llega**
> - **A Administración:** en cuanto use la invitación elige su contraseña (mínimo 12 caracteres) y prepara el código del móvil; entra en «Inicio de Administración» de su centro, con la unidad concedida. Verá el centro vacío: aún no hay residentes ni usuarios. Lo primero que le toca (ver ADM-01 a ADM-06): datos y logo del centro, estructura (edificios, habitaciones, plazas), alta de profesionales con sus perfiles y permisos.
> - **A ti (Plataforma):** tu parte termina al entregar la hoja. No ves residentes ni contenido clínico. Todo queda en la auditoría.
>
> **Dudas y errores frecuentes**
> - *«Ya existe un centro con ese código o una cuenta con ese identificador de acceso.»* → No se ha creado nada; cambia el código o el identificador.
> - *«Revisa los datos: los códigos llevan de 2 a 64 caracteres…»* → Corrige el código; recuerda que no admite acentos ni espacios.
> - *El logo no se acepta* → Debe ser PNG o JPEG, de hasta 512 KB y 1.024 px por lado.
> - *Perdí la hoja o caducó (72 h)* → Vuelve a «Administraciones y unidades» y crea otra: **la nueva anula la anterior** (PLA-03).
> - *El botón de invitación no aparece / no deja crearla* → La cuenta de ese administrador está suspendida (insignia «Cuenta suspendida»); primero reactívala (PLA-07).
>
> **Dónde se explica con más detalle** — Manual de usuario, apartado «Centros nuevos» (`/Home/Manual#plataforma-centros`) · Flujo de trabajo, sección «Antes del primer residente: un centro nuevo» (`/flujo-de-trabajo.html#centro`) · Acceso y sesión, sección «Cómo se pide el acceso» (`/acceso-y-sesion.html#pedir`).
>
> *Campos de control (no se imprimen):* `rutas: Plataforma/NuevoCentro, Plataforma/Centro, Plataforma/Invitacion` · `botones: Nuevo centro, Crear centro, Administraciones y unidades, Crear invitación de acceso o de recuperación, Imprimir`.

Prueba de aceptación: «¿Cómo creo un centro nuevo y qué datos necesito?» → en `/Guia/Preguntas` (o buscando PLA-02 en el índice impreso) llega a esta ficha en <1 minuto; la ficha cubre datos a preparar, pasos, qué ve Administración y qué debe hacer Plataforma después (entregar la invitación).

---

## 4. Inventario de tareas por perfil (preguntas «¿Cómo…?»)

Los permisos configurables se marcan **[P: …]**. «Para todos» se escribe una sola vez y cada perfil lo enlaza (no se repite).

**PARA TODOS (≈8)**: ¿cómo entro la primera vez con mi invitación? · ¿cómo entro cada día (contraseña + código del móvil)? · ¿cómo registro una passkey? · ¿cómo cambio de ámbito/centro? · ¿cómo cambio mi contraseña, veo mis sesiones o regenero códigos (Mi cuenta)? · ¿cómo recupero el acceso? · ¿cómo cambio colores/modo oscuro? · ¿cómo imprimo o guardo en PDF?

**PLATAFORMA (≈10)**: entrar con passkey y qué hacer si las pierdo · **crear un centro nuevo** · crear/recrear la invitación de una Administración · marcar o quitar la marca de «Administración principal» · añadir una unidad al ámbito de una Administración · recuperar el acceso de una Administración sin otra · ver y suspender/reactivar cuentas de varios centros · invitar a una cuenta de varios centros · qué hay (y qué no) en lo que ve Plataforma · dar de baja/ver la auditoría de lo hecho.

**AUXILIAR (≈8)**: ver mis residentes · consultar el basal (solo lectura) y la foto · cerrar turno «Sin cambios» · marcar «No valorable» · registrar un cambio ordinario · registrar un cambio prioritario · dictar el cambio por voz (si el centro lo tiene activado) · qué pasa después y a quién le llega.

**FAMILIA (≈8)**: entrar al Portal Familiar · elegir residente · leer novedades (Relevantes/Ordinarias/Todas) · leer el resumen semanal o diario · reservar una cita directa · solicitar una cita y aceptar o pedir otro horario · reprogramar o cancelar una cita · qué ve y qué no ve la familia.

**MEDICINA (≈12)**: ver mis residentes y escalados · valorar un escalado · dar una indicación a Enfermería · registrar un evento propio · seguimiento médico y continuidad entre turnos · activar el protocolo urgente · derivar a Urgencias (informe PDF) · cerrar el episodio (con indicaciones cumplidas) · corregir o rectificar mi valoración · ver historial y línea temporal · corregir una comunicación a la familia · **[P: Basal inicial / Reevaluar el basal]** crear, completar y firmar el basal.

**ENFERMERÍA (≈18)**: ver y buscar residentes · registrar un evento · usar las bandejas (prioritarios, para cerrar, ordinarios, indicaciones, escalados, comunicaciones) · empezar una valoración y anotar constantes (aviso de rangos) · decidir (cerrar/seguimiento/escalar/urgente) · escalar a Medicina · leer y registrar como realizada una indicación · seguimiento · transferir el turno y confirmar la recepción · cerrar con decisión de comunicación a la familia · corregir un comunicado no publicado · activar el protocolo urgente · derivar a Urgencias y firmar el informe · registrar intentos de llamada · historial y línea temporal · corregir o rectificar mi valoración · **[P: Dar de alta residentes]** dar de alta un residente · **[P: Basal inicial / Reevaluar el basal]** crear/reevaluar y firmar el basal · **[P: Foto del residente]** poner, cambiar o quitar la foto.

**DIRECCIÓN CLÍNICA (≈13)**: ver la supervisión por unidad · ver las derivaciones a Urgencias · abrir un episodio y cerrarlo con motivo · ver las peticiones de historial tras un traslado · adelantar, corregir o retirar un comunicado · cambiar mi ámbito de supervisión · ver el organigrama · **[P: Lectura clínica]** consultar el basal con finalidad (auditado) · **[P: Indicadores]** ver indicadores e informe de actividad · **[P: Indicadores]** ver la calidad de proceso · **[P: Plazos]** cambiar los plazos de los hitos · **[P: Rangos]** fijar los rangos de referencia · qué ve y qué no ve Dirección.

**ADMINISTRACIÓN (≈45)** — por grupos:
- *Centro y estructura (≈8):* cambiar nombre y logo del centro · crear/renombrar/desactivar unidades · edificios y plantas · habitaciones y plazas · ubicar una unidad · ámbito del centro y Administración principal · importar datos · exportar datos.
- *Personas y accesos (≈9):* dar de alta un usuario · crear su invitación de acceso o de recuperación · conceder/revocar un perfil · cambiar las unidades de un perfil · **conceder o revocar un permiso configurable** · asignar residentes a un Auxiliar · suspender/reactivar una cuenta · dar acceso a alguien de otro centro (vinculaciones) · ver la auditoría administrativa.
- *Residentes (≈9):* dar de alta un residente · ver y completar la ficha administrativa · corregir la identidad · trasladar · suspender (ingreso hospitalario) y reanudar · dar de baja y reactivar · poner la foto con consentimiento · ver los residentes dados de baja · atender los avisos de «Requiere atención».
- *Familias (≈6):* añadir y vincular un familiar · abrir/activar/renovar una autorización · dar acceso al Portal Familiar (invitación) · registrar el contacto urgente · desvincular · anonimizar a un familiar (solo Administración principal; «aún no se usa con familias reales»).
- *Comunicados (≈4):* publicar ahora un comunicado · publicar una corrección · retirarlo · fijar la hora y frecuencia de envío.
- *Citas (≈6):* configurar franjas y bloqueos · ver la agenda · gestionar solicitudes y proponer día y hora · reprogramar/cancelar · finalizar y cerrar citas pasadas · avisos por correo (si están activados).
- *Operación del centro (≈5):* turnos · equipos · planificación de turnos · organigrama y cargos · cargar la plantilla de cargos.

**Total ≈ 120–130 fichas.** Equivale, impresas, a 90–130 páginas (media página a una por ficha, más intros y anexos).

---

## 5. Orden de trabajo

Tu orden (Auxiliar → Plataforma → Familia → Medicina → Enfermería → Dirección → Administración al final) es razonable. **Cambios que haría:**

1. **Fase 0 — Estructura (antes de escribir contenido):** `guia.css`, plantilla de ficha, controlador y vistas, `GuiaDerivaTests` mínimo y el capítulo «Para todos». Se prueba con **solo la ficha PLA-02** (§3.2). Sin esto no hay forma de medir el formato.
2. **Fase 1 — Plataforma primero, luego Auxiliar:** Plataforma es tu prueba de aceptación y la que obliga a documentar el relevo hacia Administración (hueco C8). Auxiliar es el perfil más corto y sirve para afinar el tono y el formato impreso con poco riesgo.
3. **Fase 2 — Familia, Medicina, Enfermería, Dirección** (como propones). Enfermería es la que lleva más permisos: ahí se resuelven B1-B3.
4. **Fase 3 — Administración, partida en tres entregas** (Centro/estructura + Personas/accesos; Residentes + Familias; Comunicados + Citas + Operación). Es casi la mitad del total; revisarla de una vez sería inmanejable.
5. **Fase 4 — Anexos y cierre:** matriz «quién entrega qué a quién» (generada), glosario, índice de preguntas, `Todo`, enlaces desde pie/Manual/Flujo, revisión con `doc-usuario-residapp` y `qa-revisor-residapp` (según `CLAUDE.md`), actualización de `continuar-construccion.md`.

Cada fase termina con: test de deriva en verde, recorrido real con `probar-en-navegador`, impresión de prueba (en blanco y negro) y tu visto bueno.

---

## 6. Mantenimiento y control de deriva

### 6.a Cómo sabe el agente que existe la Guía

El agente `doc-usuario-residapp` y el hook `aviso-doc-usuario.js` solo conocen lo que figura en `.claude/documentacion-usuario.json`. Hay que añadir ahí `src/ResidApp.Web/Guia/*.json` (los datos; ver §6.b). Como cada ficha declara `rutas`, `botones`, `permisos`, `manual`, `flujo` y `acceso`, el agente puede responder a «he cambiado X» con un `Grep` en los JSON, sin leer las 125 fichas.

**Cambios en el código que obligan a revisar la Guía:**

| Cambio | Qué hay que tocar |
|---|---|
| Acción nueva/renombrada/retirada de un controlador de perfil, o cambio de verbo/ruta | Fichas cuyo `rutas` la nombre; ficha nueva si es tarea nueva |
| Texto de botón, pestaña, tarjeta o mensaje visible en una vista | `botones`/`mensajes` de las fichas afectadas (el test lo detecta) |
| Perfil nuevo o renombrado (`SystemProfile`, `SystemProfileDisplay`) | Guía de ese perfil, matriz, índices (el test lo detecta) |
| Permiso nuevo/retirado/renombrado (`ProfilePermissions`, etiquetas) | Fichas con `permisos`, apartado «con permiso» del perfil (el test lo detecta) |
| Cambio de a quién le llega algo (flujos entre perfiles) | `destinatarios`, diagrama del perfil, matriz |
| Reglas visibles: longitudes, caducidades (72 h, 12 caracteres…), límites de logo | `antes`/`dudas` de las fichas |
| Módulo que sale de «Próximamente» en el Manual | Fichas nuevas, `nunca` de los perfiles |
| Cambio en el Manual o el Flujo (id o título de sección) | `manual`/`flujo` de las fichas (el test lo detecta) |

### 6.b Cambios propuestos al registro, al agente y a `CLAUDE.md` (NO aplicados; para tu aprobación)

**Actualización tras releer el repositorio (2026-11-05):** `CLAUDE.md` y el agente ya no llevan la lista de documentos escrita. Hay un **registro único**, `.claude/documentacion-usuario.json`, que leen el agente y el hook `.claude/hooks/aviso-doc-usuario.js` (aviso una vez por sesión al editar `Views/` o `Controllers/`). Mi primera propuesta de diff (tocar la descripción del agente y la frase de `CLAUDE.md`) **queda anulada**: ahora basta una línea en el registro, y el agente solo necesita unas pocas reglas propias para las fichas.

**1. Registro** (una línea en `documentos`; y, opcionalmente, dos `disparadores` nuevos para que el hook avise también al tocar perfiles y permisos, que hoy viven fuera de `Views/` y `Controllers/`):

```diff
--- a/.claude/documentacion-usuario.json
+++ b/.claude/documentacion-usuario.json
@@ "disparadores"
     "src/ResidApp.Web/Views/",
-    "src/ResidApp.Web/Controllers/"
+    "src/ResidApp.Web/Controllers/",
+    "src/ResidApp.Shared/SystemProfile.cs",
+    "src/ResidApp.Domain/Accounts/ProfilePermissions.cs"
   ],
@@ "documentos"
     { "ruta": "src/ResidApp.Web/wwwroot/acceso-y-sesion.html", "que": "Acceso y sesión" },
+    { "ruta": "src/ResidApp.Web/Guia/*.json", "que": "Guía por perfil (/Guia): una ficha «¿Cómo…?» por tarea; declara rutas, botones, mensajes, permisos, destinatarios y referencias al Manual, al Flujo y a Acceso", "revisadoPara": null },
     { "ruta": "docs/pendientes-cj/pruebas-*.html", ...
```

Notas: (a) el hook ignora los documentos solo si la ruta es exacta y sin `*`; los JSON de la Guía no están bajo un disparador, así que no hay conflicto, pero `Views/Guia/` (las plantillas) **sí** cuelga de `Views/` y disparará el aviso al editarlas: es inocuo (una vez por sesión). (b) Los `*.json` de la Guía se escriben a mano, no son «código a vigilar».

**2. Agente** (`.claude/agents/doc-usuario-residapp.md`): ya lee el registro, así que **no hace falta tocar «Qué documentos revisas»**. Solo añadiría tres líneas a «Qué compruebas» y ajustaría una de «Cómo trabajas»:

```diff
--- a/.claude/agents/doc-usuario-residapp.md
+++ b/.claude/agents/doc-usuario-residapp.md
@@ ## Qué compruebas
+- **Guía por perfil** (fichas en `src/ResidApp.Web/Guia/*.json`): cada ficha frente al código (`rutas` = Controlador/Acción existentes; `botones` y `mensajes` literales en la vista o el controlador; `permisos` de `ProfilePermissions`; perfiles de `SystemProfile`) y frente al Manual, el Flujo y Acceso (la sección citada existe y su título coincide). La Guía resume y enlaza: si una ficha copia un párrafo del Manual, señálalo.
+- **Contenido que no es de usuario real** (cuentas de prueba, notas de Orlando o CJ, avisos de entorno de pruebas) no debe aparecer en la Guía, que es pública.
+- Si cambia una ficha, recuerda que hay que actualizar su campo `revisado`.
@@ ## Cómo trabajas
-- Si un desajuste es mecánico y repetible (un enlace roto, un texto de botón), propón además un test en `tests/FunctionalTests/`. Hoy `PendientesCjPagesTests.cs` ...
+- Si un desajuste es mecánico y repetible, mira antes `tests/FunctionalTests/GuiaDerivaTests.cs`: si ya lo cubre, di por qué no ha fallado; si no, propón ampliarlo. (Resto de la frase sin cambios.)
@@ ## Cómo respondes  (tamaño de la actualización)
+  - Una ficha nueva o reescrita cuenta como un punto de edición; más de unos 3 fichas, o un perfil nuevo, es **larga**.
```

**3. `CLAUDE.md`:** la regla «Documentación de usuario» ya remite al registro, así que no cambia. Solo añadiría una viñeta en «Dónde está el resto del contexto» (junto a la del Manual):

```diff
+- `src\ResidApp.Web\Guia\*.json` y `src\ResidApp.Web\Views\Guia\` (ruta `/Guia`) — Guía por perfil: «soy X, ¿cómo hago Y?». Una ficha por tarea; resume y enlaza al Manual. Registrada en `.claude\documentacion-usuario.json`; `tests\FunctionalTests\GuiaDerivaTests.cs` avisa de lo mecánico.
```

**4. Relevo:** `docs/prompts-ia/continuar-construccion.md` se actualiza al cerrar cada fase (regla existente de `CLAUDE.md`).

### 6.c Control automático de deriva — `tests/FunctionalTests/GuiaDerivaTests.cs`

Va en `FunctionalTests` porque es el único proyecto de pruebas que referencia `ResidApp.Web` (`UnitTests` no). No necesita arrancar la app ni la base de datos: lee ficheros (`Guia/*.json`, `Manual.cshtml`, vistas, HTML estáticos) y usa reflexión sobre los controladores.

| # | Falla si… | Cómo | Fiabilidad |
|---|---|---|---|
| 1 | Hay un `SystemProfile` sin archivo/guía, o una guía con perfil inexistente | `Enum.GetValues<SystemProfile>()` | Alta |
| 2 | Hay un permiso de `ProfilePermissions.For(perfil)` que no sale en ninguna ficha de ese perfil, o una ficha cita un permiso que no existe | Catálogo en C# frente a `permisos` | Alta |
| 3 | Una `ruta` Controlador/Acción de una ficha ya no existe | Reflexión sobre `ControllerBase` (modelo: `SesionTests.cs:82-117`) | Alta |
| 4 | Un `boton` no aparece literal en las vistas de ese controlador (ni en `Shared`) | Búsqueda de texto en `Views/<Controlador>/*.cshtml` | Media: falsos fallos si el texto sale de C# (por eso `mensajes` se busca también en `*.cs`; lista de excepciones explícita) |
| 5 | El `id` de una referencia a Manual/Flujo/Acceso no existe, o su título ya no coincide con el `<h2>` | Parseo simple de `id="…"` y del `<h2>` siguiente | Alta |
| 6 | Códigos de ficha duplicados, o reutilizados tras retirarse; campos obligatorios vacíos; destinatarios no válidos | Validación del JSON | Alta |
| 7 | Los hex de `guia.css` ya no son los del Flujo | Comparación de variables | Alta |
| 7b | `revisadoPara` de un JSON de la Guía no coincide con el del registro, o es posterior a la versión de `Directory.Build.props` | Lectura de `.claude/documentacion-usuario.json` y `src/Directory.Build.props` | Alta |
| 8 | Aparece texto de uso interno («Orlando», «almendros-», «CJ», «entorno de pruebas») en la Guía | Lista de palabras prohibidas | Media (red de seguridad, no garantía) |
| 9 | La matriz «quién entrega a quién» no se corresponde con `destinatarios` | No hace falta test: la matriz **se genera** de los datos | — |

**Lo que NO detecta** (sigue necesitando al agente y a una persona): que los pasos estén en el orden correcto, que la explicación sea comprensible, que lo que dice una pantalla de JavaScript funcione, y que una ficha **nueva** falte para una acción nueva (el test solo avisa de acciones citadas que desaparecen, no de acciones nuevas sin ficha; ver la extensión opcional de cobertura más abajo).

**Coste y valor:** unas 200-250 líneas de test, ejecución en milisegundos, sin dependencias. **Merece la pena**: la documentación actual ya tiene más de 15 desajustes (§1.3) que un test así habría cazado en su mayoría (A1, A3, B2, B4, C1, C3, C6).

*Extensión opcional (media jornada):* test que lista las acciones GET de cada perfil **sin ficha** (cobertura). Lo recomiendo como **aviso**, no como fallo, porque hay acciones que no son tareas (descargas, vistas de apoyo).

### 6.d Fuente única: comparación y recomendación

| | 1. Fichas a mano en HTML | 2. **Fichas estructuradas (JSON) → Guía** (recomendada) | 3. Fichas estructuradas → Guía **y** partes del Manual |
|---|---|---|---|
| Deriva Guía↔código | Alta | **Baja y comprobable** | Baja |
| Deriva Guía↔Manual | Alta | Media: referencias comprobadas por test; el texto del Manual sigue siendo suyo | Baja |
| Riesgo | Mucho texto repetido | Poco | **Alto:** el Manual está organizado por pantalla y escrito con matices (p. ej. 36 apartados con prosa propia); generarlo desde fichas por tarea lo aplanaría o forzaría fichas «por pantalla» que ya no son «¿Cómo…?» |
| Coste | Bajo ahora, alto después | Medio | Alto, con migración del Manual |

**Recomendación: la 2.** Una sola vez se escribe la tarea (la ficha) y se **enlaza** al Manual para el detalle. Como mejora barata y de bajo riesgo, más adelante se puede generar en el Manual **solo** la tabla del catálogo de permisos (Manual L654-662) desde `ProfilePermissions`, que es el dato que más se repite y más se desalinea. No generaría el resto del Manual.

---

## 7. Cambios sugeridos en el Manual, el Flujo y Acceso (NO se aplican)

**Manual**
1. Numerar o dar código estable a los `<h2>` (decisión D3) y **reordenar el índice** para que siga el cuerpo; añadir `mi-cuenta` al índice.
2. Unificar el nombre del perfil (D1) en L144, L1654, L1671, L993, L1788 y en las insignias; resolver «Familiar»/«Familia».
3. Hablar de los dos permisos de basal (inicial y reevaluar) donde hoy se dice «permiso de basal» (L17, L658, L941-944).
4. Añadir un apartado **«Qué ve Administración en un centro recién creado»** (hueco C8).
5. **Sacar de la vista pública** la sección `cuentas-prueba` (L228-349) y las demás notas de §1.3.D; propuesta de destino en D5.
6. Cabecera con fecha de revisión y versión; ocultar el índice lateral al imprimir (`d-print-none`); `@page` y saltos de página.
7. Un enlace por perfil al inicio de cada sección («Guía de este perfil: /Guia/enfermeria»).

**Flujo**
1. «La Auxiliar» → «el Auxiliar» (F:1126); «Dirección» → «Dirección Clínica»; unificar «Enfermería o Medicina».
2. Tarjetas «Quién interviene» (F:437, 439): incluir los permisos de Enfermería (alta, foto, basal inicial/reevaluar).
3. Añadir el enlace a «Acceso y sesión» en el pie; igualar la fecha de «Estado a…».
4. Sustituir «Ver los plazos de CJ» (F:1023, 1768) por «valores sugeridos».
5. En los `p.ir` («Más en…»), añadir «y en la Guía: PLA-02».

**Acceso y sesión**
1. Añadir las variables `--lane-*-bg` que faltan (o, mejor, unificar el CSS compartido con la Guía en una hoja única; a medio plazo).
2. Mover `#cuentas` y `#mantener` fuera de la página pública (D5) y quitar las menciones a CJ/Orlando.

**Código (fuera de alcance; solo aviso):** `RangosReferencia/Index.cshtml:20-22,38-39`, `Home/Index.cshtml:130,142` (nombre del perfil de Dirección); `SystemProfile.cs:3` («6 perfiles»).

---

## 8. Decisiones que necesito de ti, riesgos y criterios de aceptación

### 8.1 Decisiones (TOMADAS el 2026-11-05)

| # | Decisión | Resultado | Consecuencia para el plan |
|---|---|---|---|
| D1 | Nombres de perfil | **«Dirección / Coordinación Clínica»** (y «Familia») | La etiqueta de la app sigue siendo «Dirección Clínica» (`SystemProfileDisplay.Label`): la guía de este perfil dirá en su primera línea «en pantalla, Dirección Clínica». El test #1 compara perfiles por el valor del enum, no por el texto del título. Cuidado: es adelantar una decisión de producto que el Manual (L993, L1788) dice sin tomar; avisar a CJ. |
| D2 | Formato | **Vista Razor + fichas JSON** | Tal como §2. |
| D3 | Referencias al Manual | **Título + ruta impresa**, sin numerar el Manual | Tal como §2.4. |
| D4 | Versión de la app | **Reformulada tras el PR #146** (versionado semántico ya desplegado): sello «Revisado para ResidApp vX.Y.Z» + fecha | La Guía **no lee** la versión en ejecución (es pública y el pie de la app la oculta sin sesión). Imprime el sello desde `revisadoPara` de cada JSON, igual que el Manual y el Flujo. Se entra como documento en el registro con `revisadoPara: null` hasta que se repase de punta a punta. Sustituye a la propuesta anterior del hash del commit; ya no hay nada que verificar en `ci-cd.yml`. |
| D5 | Contenido interno | **Solo dejarlo fuera de la Guía** | No se mueve nada ahora; se propone el traslado en un cambio aparte. |
| D6 | Permisos de basal | **Los dos permisos, tal como el código** | Las fichas de Enfermería y Medicina nombran «Basal inicial (al alta)» y «Reevaluar el basal» y explican que el botón de Enfermería sale siempre. |
| D7 | PDF | **Generar PDFs con Playwright** (v2 incluida) | Se añade a la fase 4 un script que genera un PDF por perfil con «Página X de Y» y un índice con números de página (dos pasadas). Playwright no es dependencia del proyecto web: va en `tools/` (como `PruebaDictado`), no en `src/`. El imprimir-desde-navegador sigue funcionando igual. |
| D8 | Orden | **El de §5** | Fase 0 → Plataforma y Auxiliar → Familia, Medicina, Enfermería, Dirección → Administración en 3 entregas → anexos y cierre. |
| D9 | Visibilidad | **Pública, sin sesión** | `GuiaController` con `[AllowAnonymous]`; el test #8 de palabras prohibidas pasa a ser obligatorio. |
| D10 | Idioma | **Solo español** | Catalán para Familia, si CJ lo pide, en una segunda fase. |
| D11 | Qué significan «Depende de», «Le entrega trabajo a» y la matriz (2026-11-06) | **Dos criterios distintos** | «Le entrega trabajo a» (`entregaA`) es solo trabajo que se pasa a otro perfil para que actúe; «Depende de» (`dependeDe`) es de quién se necesita algo (datos, accesos o trabajo previo) y **no** es el inverso exacto de `entregaA`. La matriz «Quién entrega qué a quién» sale de «Qué pasa después» de cada ficha y es más amplia: incluye lo que otro perfil solo ve (p. ej. el basal en solo lectura para Auxiliar). La prueba exige que cada `entregaA` tenga al menos una ficha que lo haga; no exige lo contrario. Una frase negativa («no lo ve hasta que…») no es una entrega: va en `dudas` o `antes`. Se explica en cada guía, bajo el diagrama. |

### 8.2 Riesgos

- **Tamaño:** ~125 fichas; si se escriben sin disciplina acaban copiando el Manual. *Mitigación:* cada ficha ≤ 1 página, regla «resume y enlaza», revisión del agente.
- **Deriva silenciosa** de lo que el test no ve (orden de pasos, claridad). *Mitigación:* campo `revisado`, revisión con `doc-usuario-residapp`, recorrido con `probar-en-navegador` por perfil.
- **Falsos fallos del test de botones** (textos que vienen de C#). *Mitigación:* búsqueda también en `*.cs` y lista de excepciones explícita.
- **Contenido interno filtrado a la web pública.** *Mitigación:* D5, D9 y el test #8.
- **Sello desfasado:** el sello solo vale si alguien repasa la Guía al cerrar una versión MENOR (regla de `CLAUDE.md`). *Mitigación:* el test 7b y el informe del agente (sello actual frente a versión actual). Con ~125 fichas, ese repaso es trabajo real en cada hito.
- **Protección de datos:** la Guía no trata datos personales ni IA; los ejemplos son ficticios (Rosa, Elena, Los Almendros). No creo que requiera al agente `dpo-residapp`, pero lo consultaré si una ficha describe anonimización de familiares (ADM, «todavía no se usa con familias reales»).
- **Imprimir con paginación** exacta es limitado por navegadores (§2.4). *Mitigación:* códigos de ficha como referencia principal.

### 8.3 Criterios de aceptación

1. **Prueba del centro nuevo:** desde el índice de preguntas, «¿Cómo creo un centro nuevo y qué datos necesito?» lleva en <1 minuto a PLA-02, que contiene: datos a preparar, pasos con nombres exactos de botones, qué ve Administración al terminar, y que Plataforma debe entregar la invitación.
2. Cada perfil tiene: propósito, rutina, diagrama «de quién depende / a quién entrega», «siempre / con permiso / nunca», y fichas con el esquema de §3.1.
3. Cada tarea aparece **una sola vez** (el test de duplicados `pregunta`/`codigo` pasa); el detalle largo sigue en el Manual.
4. Todos los permisos de §1.2 están marcados «REQUIERE PERMISO» con quién lo concede, en las fichas correspondientes.
5. **Impresión:** un perfil se imprime solo desde `/Guia/{perfil}`; cada perfil empieza en página nueva; ninguna ficha se parte; pie con el sello «Revisado para ResidApp vX.Y.Z» y la fecha; legible en blanco y negro (etiquetas de texto además de color); referencias cruzadas legibles sin clic.
6. Sin cuentas de prueba, notas de Orlando/CJ ni avisos de entorno de pruebas.
7. `GuiaDerivaTests` en verde y suite completa en verde; agente y `CLAUDE.md` actualizados (con tu visto bueno al diff de §6.b).
8. Revisión con `doc-usuario-residapp` y `qa-revisor-residapp` sin hallazgos graves; `continuar-construccion.md` actualizado.

---

## 9. Verificación (cuando se ejecute el plan)

- `dotnet test tests/FunctionalTests --filter GuiaDerivaTests` (sin BD) y la suite completa contra la BD local.
- Recorrido por perfil con la skill `probar-en-navegador` (claro, oscuro, 375 px, sin desplazamiento lateral ni errores de consola) comprobando que cada nombre de botón de las fichas es el que se ve.
- Impresión de prueba de `/Guia/Plataforma` y `/Guia/Todo` en blanco y negro y a A4: saltos de página, fichas sin partir, pie con fecha.
- Prueba de aceptación §8.3.1 cronometrada, con alguien que no haya escrito la guía.
