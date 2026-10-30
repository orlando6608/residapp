# Guía de Sistema Visual y Paleta de Colores: ResidApp

**Propósito:** Definir los estándares estéticos y cromáticos para las vistas Razor (.cshtml) de ResidApp. Toda la maquetación debe ser Mobile-First, accesible (cumpliendo WCAG 2.2 AA) y basada en los componentes responsivos de Bootstrap 5.

---

## 🎨 1. Paleta de Colores Oficial (Variables CSS)

El sistema visual utiliza una base clínica corporativa (Azul), combinada con el factor humano sociosanitario (Verde Teal) y un sistema defensivo de estados de alerta. Los colores de marca son los del logo (`wwwroot/images/logo.svg`) y no cambian.

```css
:root {
  /* 🔵 Colores Principales (Marca, Rigor Técnico y Seguridad) */
  --color-primary-brand: #0D6EFD;       /* Azul Bootstrap (Botones y acciones principales) */
  --color-primary-dark:  #0A58CA;       /* Azul Oscuro (Cabecera, enlaces y textos en azul) */
  
  /* 🩺 Colores Secundarios (El Toque Clínico Asistencial de CJ) */
  --color-secondary-teal: #198754;      /* Verde Sanitario (Estados basales estables, éxitos) */
  --color-secondary-light:#20C997;      /* Menta/Teal Brillante (Destacados asistenciales) */

  /* 🏛️ Neutros de Superficie (Especial PWA Móvil: Evita fatiga visual) */
  --color-bg-main:         #F5F7FA;      /* Fondo de aplicación claro grisáceo */
  --color-bg-card:         #FFFFFF;      /* Fondo de tarjetas y formularios */
  --color-text-dark:       #1A2433;      /* Texto principal de alta legibilidad */
  --color-text-muted:      #5B6676;      /* Subtítulos, horas de relevos y metadatos */
  --color-border:          #E3E8EF;      /* Bordes de tarjetas y separadores */
  --color-border-control:  #7C8796;      /* Borde de campos, casillas y selectores */
  
  /* 🚨 Sistema Semafórico de Alertas Clínicas (Innegociable del PRD) */
  --color-alert-danger:    #DC3545;      /* Cambios basales críticos, incidencias prioritarias */
  --color-alert-warning:   #FFC107;      /* Observaciones en revisión, datos pendientes */
  --color-alert-info:      #0DCAF0;      /* Información complementaria, avisos familiares */
}
```

Contrastes medidos (WCAG 2.2 AA: 4,5:1 para texto y 3:1 para bordes de controles e indicadores):

| Combinación | Contraste |
| --- | --- |
| Texto secundario `#5B6676` sobre el fondo `#F5F7FA` | 5,4:1 |
| Enlace `#0A58CA` sobre el fondo `#F5F7FA` | 6,0:1 |
| Borde de controles `#7C8796` sobre blanco | 3,6:1 |

Los valores anteriores, `#6C757D` (texto secundario) y `#0D6EFD` (enlaces) sobre `#F8F9FA`, daban 4,45:1 y 4,27:1, por debajo del AA. Por eso los enlaces usan el azul oscuro y los botones conservan el azul de marca, que sobre blanco y con texto blanco da 4,5:1.

### Modo oscuro

La app sigue por defecto el ajuste claro u oscuro del dispositivo. En la cabecera hay un selector **Claro / Oscuro / Automático** que cada dispositivo recuerda (`wwwroot/js/tema.js`, sobre `data-bs-theme` de Bootstrap 5.3). Los colores de marca y los semafóricos no cambian; cambian los neutros:

```css
[data-bs-theme=dark] {
  --color-bg-main:        #0F141B;      /* Fondo de aplicación */
  --color-bg-card:        #171D26;      /* Tarjetas, campos y desplegables */
  --color-text-dark:      #E3E8EF;      /* Texto principal (13,7:1 sobre la superficie) */
  --color-text-muted:     #A3ADBA;      /* Texto secundario (7,4:1) */
  --color-border:         #2A3340;
  --color-border-control: #6B7685;      /* 3,7:1 */
  --color-link:           #6EA8FE;      /* 7,0:1 */
  --color-navbar-bg:      #131B26;
}
```

En oscuro, los textos y los botones de contorno de color (`text-danger`, `btn-outline-primary`…) usan los tonos `--bs-*-text-emphasis` de Bootstrap, porque los de serie se quedan en 3,7:1. **Al imprimir o guardar como PDF, la página sale siempre en claro.**

---

## 📱 2. Guía de Aplicación en la Interfaz (UI)

### A. Pantalla de Autenticación (Login)
* **Fondo:** Blanco limpio (`--color-bg-card`) o un degradado sutil hacia el gris claro.
* **Componente Central:** El logotipo SVG inyectado centrado con un ancho máximo de `120px` en móviles.
* **Formulario:** Inputs grandes con la clase `.form-control-lg` de Bootstrap para facilitar la pulsación táctil. El botón de acceso debe usar `.btn-primary` (`#0D6EFD`).

### B. El Panel Principal (Dashboard del Personal de Planta)
Pensado para pantallas táctiles de tablets y smartphones (Auxiliares y Enfermería en movimiento).
* **Cabecera (Navbar):** Fondo azul oscuro (`--color-primary-dark`; en modo oscuro, `--color-navbar-bg`) con el icono de la app y el nombre **ResidApp** visible (con sesión de un centro, el logo y el nombre del centro; véase la sección 4). A la derecha lleva el enlace **«Mi panel»** al inicio del perfil activo, el selector de tema y un **menú de usuario** cuyo botón muestra el **Perfil Activo** y, al abrirlo, la identidad, «Cambiar ámbito», «Cambiar de identidad» y «Salir».
* **Cuerpo:** Fondo gris claro (`--color-bg-main`). Los residentes no se listarán en tablas densas de escritorio; se presentarán en **tarjetas móviles responsivas** (`.card`). Una tarjeta que lleva a una sola pantalla es pulsable entera (`stretched-link` en el enlace del nombre), sin botón a todo el ancho.
* **Tarjetas de Residentes:**
  * Nombre en texto oscuro destacado (`--color-text-dark`).
  * Indicador visual izquierdo (franja de `4px` sólida, solo en el lado izquierdo): **Verde** si su estado basal está resuelto, **Rojo** si hay una alerta o cambio funcional sin resolver (exigencia estricta del PRD). Se escribe `border-start border-4 border-danger` o `border-success`; `residapp-theme.css` devuelve los otros tres lados al borde neutro (en Bootstrap, `border-4` y `border-<color>` afectarían a los cuatro lados y dibujarían un marco).
* **Paneles de inicio por perfil:** cada contador se muestra como una cifra grande con una etiqueta corta debajo, en singular o plural reales (nunca «evento(s)»). El orden de las fichas es fijo, con lo urgente primero; no se reordenan según las cifras.
* **Tablas en el móvil:** toda tabla tiene, por debajo de `md`, su versión en tarjetas o en `list-group` (`d-md-none` + `d-none d-md-block`), con los mismos datos y las mismas acciones. Una tabla con campos de formulario no se duplica (se enviaría dos veces): se maqueta como rejilla (`row`/`col`) que sirva para los dos tamaños. En una pantalla que se imprime como informe, las tarjetas llevan `d-print-none` y la tabla `d-print-block`: el papel A4 queda por debajo de `md` y, sin eso, saldrían las tarjetas.
* **Listas vacías:** el aviso de una lista o bandeja vacía a página completa va en `<p class="estado-vacio">` (recuadro discontinuo centrado), no como una línea gris suelta. Dentro de una tarjeta basta una línea `text-muted`.

### D. Color según el valor y etiquetas de estado
* **Un contador a 0 es siempre neutro.** Rojo (cifra y franja) solo si un contador urgente es mayor que 0 (eventos prioritarios, protocolos urgentes, seguimientos vencidos, indicaciones con incidencia); ámbar solo si un contador que pide atención es mayor que 0 (indicaciones sin leer); el resto de recuentos, en el color del texto. El significado nunca depende solo del color: la etiqueta lo dice («prioritarios», «vencidos»).
* **Etiquetas (`badge`):** se escriben con `text-bg-<color>`, y `residapp-theme.css` las pinta con fondo suave y texto oscuro del mismo tono (`--bs-*-bg-subtle` / `--bs-*-text-emphasis`), que se adaptan solas al modo oscuro.

### E. Iconos
Un icono sencillo junto al título ayuda a ubicarse sin leer, sobre todo a quien tiene poca soltura digital. Se usa siempre que sea práctico y facilite la experiencia de la persona, y de forma coherente con el resto del diseño. Reglas:
* **Reutiliza antes de crear.** Antes de añadir un icono, mira la tabla de abajo: si ya hay uno para ese destino o ese concepto, se usa ese. Solo se crea uno nuevo si ninguno encaja, y se registra en la tabla en el mismo cambio.
* **Siempre con texto.** El icono acompaña al título y nunca lo sustituye ni lleva el significado: es decorativo (`aria-hidden="true"`). Tampoco es un valor clínico: nada de caritas, pulgares ni dibujos que parezcan una valoración.
* **Origen:** [Bootstrap Icons](https://icons.getbootstrap.com) 1.11.3 (licencia MIT), copiados en el sprite `wwwroot/images/iconos.svg`, sin CDN. Para añadir uno, se copia su `<path>` como un nuevo `<symbol id="…" viewBox="0 0 16 16" fill="currentColor">` con un identificador en español.
* **Uso:** `<partial name="_Icono" model='"residentes"' />` dentro del título, que va como `<h2 class="h5 card-title d-flex align-items-center gap-2">`. La clase `.icono` (24 px) toma el color de los enlaces (`--color-link`), que cumple AA en claro y oscuro. El sprite está en el shell del service worker.
* **Un icono por destino, el mismo en todos los perfiles:**

| Id | Icono | Para qué |
| --- | --- | --- |
| `residentes` | `people` | Lista de residentes |
| `basal` | `heart-pulse` | Estado basal y su consulta |
| `constantes` | `activity` | Constantes y sus rangos de referencia |
| `enfermeria` | `clipboard2-pulse` | Inicio de Enfermería |
| `medicina` | `journal-medical` | Inicio de Medicina |
| `supervision` | `clipboard-data` | Supervisión de Dirección Clínica |
| `plazos` | `calendar-check` | Plazos de los hitos del proceso |
| `administracion` | `person-vcard` | Administración (ficha administrativa) |
| `alta-residente` | `person-plus` | Alta de un residente |
| `centros` | `buildings` | Centros y su estructura (Plataforma) |
| `prioritarios` | `exclamation-triangle` | Bandeja prioritaria |
| `protocolos` | `lightning-charge` | Protocolos urgentes |
| `seguimientos` | `eye` | Seguimientos |
| `indicaciones` | `clipboard-check` | Indicaciones de Medicina a Enfermería |
| `para-cerrar` | `check2-circle` | Episodios con las indicaciones cumplidas, para cerrar |
| `ordinarios` | `journal-text` | Cambios ordinarios |
| `escalados` | `arrow-up-right-circle` | Escalados a Medicina (y eventos propios) |
| `comunicaciones` | `chat-left-text` | Comunicaciones familiares pendientes de publicar |
| `abiertos` | `folder2-open` | Eventos abiertos de un residente |
| `contacto` | `telephone` | Contactos urgentes |
| `foto` | `camera` | Foto de identificación |
| `identidad` | `person-badge` | Identidad del residente |
| `familiares` | `person-hearts` | Familiares del residente |
| `ubicacion` | `geo-alt` | Historial de ubicación |
| `correcciones` | `pencil-square` | Correcciones de identidad |
| `derivaciones` | `hospital` | Derivaciones a Urgencias |
| `indicadores` | `bar-chart-line` | Indicadores e informe de actividad |
| `calidad` | `patch-check` | Calidad de proceso |
| `peticiones` | `folder-symlink` | Peticiones de historial tras un traslado |
| `comunicados` | `megaphone` | Comunicados a la familia |
| `ambito` | `bullseye` | Ámbito de supervisión y «Cambiar ámbito» |
| `unidades` | `grid-3x3-gap` | Unidades del centro o por unidad |
| `baja` | `person-dash` | Residentes dados de baja |
| `usuarios` | `person-gear` | Usuarios del centro |
| `citas` | `calendar-event` | Citas con la familia |
| `datos-centro` | `building-gear` | Datos del centro |
| `turnos` | `clock` | Turnos |
| `equipos` | `person-workspace` | Equipos |
| `organigrama` | `diagram-3` | Organigrama y cargos |
| `planificacion` | `calendar-week` | Planificación de turnos |
| `importar` / `exportar` | `box-arrow-in-down` / `box-arrow-up` | Importar y exportar datos |
| `auditoria` | `shield-check` | Auditoría |
| `inicio` | `house` | «Mi panel» (menú) |
| `cuenta` | `person-circle` | Menú de usuario y «Mi cuenta» |
| `salir` / `entrar` | `box-arrow-right` / `box-arrow-in-right` | «Salir» e «Iniciar sesión» |
| `sin-cambios` | `check-circle` | Sin cambios (cierre cotidiano del Auxiliar) |
| `registrar-cambio` | `journal-plus` | Registrar cambio |
| `no-valorable` | `slash-circle` | No valorable |
| `dictado` | `mic` | Dictar el cambio |
| `areas` | `list-check` | Áreas observadas |
| `temperatura` | `thermometer-half` | Temperatura |
| `clasificacion` | `tags` | Clasificación ordinario/prioritario |
| `resumen` | `card-checklist` | Resumen antes de confirmar |
| `centro-nuevo` | `building-add` | «Nuevo centro» (Plataforma) |
| `cuentas` | `person-lines-fill` | «Cuentas de varios centros» (Plataforma) |

Reglas de uso según dónde va el icono. Se pone siempre que ayude a ubicarse o a reconocer una opción habitual, y nunca en la etiqueta de un dato, un estado o un valor clínico.
* **Título de tarjeta o de sección** (`h2` con `d-flex align-items-center gap-2`): 24 px, color de enlace. En las fichas de `Enfermeria`/`Medicina`/`Administracion` `Residente`, en las tarjetas de decisión de un evento y en los inicios de cada perfil.
* **Menú de la cabecera y desplegables:** 16 px y color del texto (`.navbar .icono`, `.dropdown-menu .icono`); el elemento lleva `d-flex align-items-center gap-2`.
* **Botón principal:** 20 px y color del texto (`.btn .icono`), con `d-inline-flex align-items-center justify-content-center gap-2`. Solo en botones de acción propios de la pantalla (hoy, los de Plataforma).
* **Tarjetas con cifra en pantalla estrecha** (inicios de Enfermería y Medicina): el icono va encima del título (`flex-column flex-sm-row`), porque palabras largas como «Comunicaciones» no caben a su lado en 375 px.

* **Título de pantalla principal** (`h1` con `h3 d-flex align-items-center gap-2`): el mismo icono que la tarjeta del inicio desde la que se llega, para que la persona reconozca el sitio al llegar. Solo en las pantallas a las que lleva una tarjeta (listas, bandejas y pantallas de gestión), no en los subformularios de una sola acción («Cambiar nombre», «Suspender»…).
* **Portal familiar:** pocos iconos y solo de orientación: «Citas» (`citas`, en el botón de la ficha), «Novedades del centro» (`comunicaciones`), «Mis residentes» (`residentes`) y el teléfono (`contacto`) en el aviso «no es un canal urgente» de las pantallas de citas (`.alert .icono`, 20 px, color del aviso). Nada de signos de alarma, corazones, caras ni iconos en los estados de una cita ni en «Relevante»/«Ordinaria»: lo dicen la etiqueta y el borde, y un símbolo podría leerse como un juicio sobre el estado de su familiar.
* **No llevan icono:** los campos de un formulario (la etiqueta ya los nombra), las columnas y filas de una tabla, los botones de envío (por el aviso «Enviando…» de `site.js`) ni los estados.
* **Pendiente:** títulos de bloque (`fieldset`) de los formularios largos distintos de los del Auxiliar, y el basal (`EnfermeriaBasal`, `Auxiliar/Basal`), tras el visto bueno de CJ al piloto.

### C. Formularios de Registro Clínico (Firma de Estado Basal)
* **Inputs:** Utilizar agrupaciones limpias con `.mb-3`.
* **Botones de Acción Inmediata:** El botón crítico de "Firmar Estado Basal" o "Guardar Incidencia" debe estar ubicado en la zona inferior derecha, ser de tamaño grande `.btn-lg` y usar colores semánticos inequívocos (`.btn-success` para firmas basales estables).

---

## ♿ 3. Accesibilidad Táctica (WCAG 2.2 AA)

Al tratar con perfiles que van desde auxiliares con turnos nocturnos hasta **Familiares** de avanzada edad, el contraste es una prioridad de ingeniería:
1. **Contraste de Texto:** Jamás se usará texto blanco sobre fondos verde claro o amarillo. Los textos sobre el color de alerta amarillo (`--color-alert-warning`) irán obligatoriamente en negro (`#000000`).
2. **Tamaño Táctil Mínimo:** Todos los elementos interactivos (botones, selectores de catálogos como Barthel, enlaces) deben tener una altura mínima de **48px** para evitar errores de pulsación en movilidad. `residapp-theme.css` lo aplica a botones, campos y selectores en cualquier dispositivo con pantalla táctil (`@media (any-pointer: coarse)`) y agranda casillas y radios; con ratón se mantiene el tamaño de Bootstrap. Como en WCAG 2.5.8, quedan exentos los enlaces dentro de un texto y la miga de pan. Las opciones de selección rápida de un formulario se presentan como botones-chip (`input.btn-check` + `label.btn`), no como casillas de 16px; cada pareja va dentro de `<span class="chip-par">` para que, al recibir el foco, la página se desplace al chip y no al principio del grupo.
3. **Texto base de 16px** también en móvil: no se reduce el tamaño raíz en pantallas estrechas.
4. **Contraste de bordes:** campos, casillas y selectores llevan un borde de al menos 3:1 (`--color-border-control`, WCAG 1.4.11).
5. **Foco no tapado (WCAG 2.4.11):** en una pantalla con `.barra-accion` fija, el tema reserva espacio abajo (`scroll-padding-bottom`) para que el campo con el foco nunca quede debajo de la barra.

### Indicador de envío
`site.js` marca todo formulario válido al enviarse: el botón que lo envía muestra un `spinner-border spinner-border-sm` (hereda `currentColor`, vale en claro y oscuro; Bootstrap ya lo ralentiza con `prefers-reduced-motion`), una región `role="status"` anuncia «Enviando…» y un segundo envío se descarta. Reglas al maquetar:
* **No deshabilites el botón de envío con `disabled`:** su `name`/`value` (p. ej. `Accion=Firmar`) no viajaría en el POST. El estado se marca con `data-enviando` y `aria-disabled`.
* Un formulario que no navega (descarga de archivo) lleva `data-sin-carga`. Los enlaces `<a>` de descarga de PDF no pasan por este indicador (es solo de `submit`).
* Nada de `<script>` ni `style=""` en línea para esto: va en `site.js` y `site.css` (CSP futura). El spinner no se imprime.

---

## 🖨️ 4. Identidad de marca en PDF e impresión (obligatorio)

El icono de ResidApp en color es la identidad de marca de la app. **Todo documento que la app genere o que se imprima desde ella lleva arriba el icono en color y el nombre «ResidApp · Plataforma Asistencial Geriátrica»**, separados del contenido por una línea fina gris. Esto incluye los documentos que se añadan en el futuro.

**Marca propia del centro (script 0068, pendiente del visto bueno de CJ).** Cuando quien navega o el documento pertenece a un centro, ese encabezado lo ocupa **la marca del centro**: su logo (sin logo propio, el icono de ResidApp) y su nombre, en vez de «ResidApp · Plataforma Asistencial Geriátrica». ResidApp pasa a una línea discreta **al pie**: «Generado con ResidApp · Plataforma Asistencial Geriátrica». Solo se ve la marca de ResidApp arriba en Plataforma, en las pantallas sin sesión, en la página sin conexión y en los documentos que no son de ningún centro. El logo y el nombre los cambia la Administración del propio centro («Datos del centro»); Plataforma puede subir el logo al dar de alta el centro.

* **PDF generado en servidor (MigraDoc):** cada sección llama a `PdfBranding.AddHeader(section, branding)` (`src/ResidApp.Infrastructure/Pdf/PdfBranding.cs`), que pone el encabezado en **todas las páginas**; `branding` es el nombre y el logo del centro (`ReferralReportBranding`), o `null` para la marca de ResidApp. El pie del renderizador termina con `PdfBranding.AddGeneratedBy(footer, branding)`. No se escribe un encabezado propio en cada renderizador.
* **Impresión o «guardar como PDF» del navegador:** el encabezado ya está en `Views/Shared/_Layout.cshtml` (bloque `.impresion-marca`, visible solo al imprimir) y se repite **arriba de cada página**, con un hueco fijo para que el contenido nunca quede debajo; con marca de centro, el bloque `.impresion-pie` repite abajo «Generado con ResidApp». `wwwroot/css/site.css` imprime el contenedor `.impresion-pagina` como una tabla, el encabezado como su cabecera (`table-header-group` con `break-inside: avoid`) y el pie como su pie (`table-footer-group`), que es lo que hace que el navegador los repita. Una vista que oculte elementos al imprimir no debe ocultar esos bloques (por eso son `div`, no `header`/`footer`) ni cambiar el `display` del contenedor.
* **Origen de los iconos:** el de ResidApp, `wwwroot/images/logo.svg`; los PDF de servidor usan su versión PNG, `src/ResidApp.Infrastructure/Pdf/Images/logo.png`, porque PDFsharp no importa SVG. Si cambia el SVG, hay que regenerar el PNG. El logo de un centro es un PNG o JPEG de hasta 512 KB y 1024 px por lado, guardado en `dbo.logos_centro`; al subirlo se encuadra en un recuadro apaisado 3:1 (768×256 px, márgenes transparentes) con mover, acercar y alejar; en pantalla va sobre un recuadro blanco (`.logo-centro`) para que se vea igual en claro y en oscuro.
* Los documentos ya firmados no se regeneran: conservan el aspecto con el que se firmaron.

---

## 🧙‍♂️ Instrucciones directas para Claude Code
Cuando maquetes código Razor (`.cshtml`) en `src/ResidApp.Web/Views/`, implementa de forma estricta las clases utilitarias de Bootstrap 5 que mapean esta paleta:
* Fondos: `bg-body`, `bg-body-tertiary`, `bg-primary`. **Nunca** `bg-white`, `bg-light`, `text-dark`, `text-black`, `btn-dark`, `btn-light` ni colores fijos en `style=""`: no cambian con el modo oscuro.
* Botones y Badges: `btn-primary`, `btn-success`, `badge text-bg-danger`.
* Bordes semánticos para alertas de residentes: `border-start border-4 border-danger` o `border-success`.
* Toda pantalla nueva o modificada se revisa en claro y en oscuro.
