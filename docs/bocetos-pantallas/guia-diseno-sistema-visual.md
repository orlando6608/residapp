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
* **Cabecera (Navbar):** Fondo azul oscuro (`--color-primary-dark`; en modo oscuro, `--color-navbar-bg`) con el icono de la app y el nombre **ResidApp** visible. A la derecha lleva el enlace **«Mi panel»** al inicio del perfil activo, el selector de tema y un **menú de usuario** cuyo botón muestra el **Perfil Activo** y, al abrirlo, la identidad, «Cambiar ámbito», «Cambiar de identidad» y «Salir».
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

---

## 🖨️ 4. Identidad de marca en PDF e impresión (obligatorio)

El icono de ResidApp en color es la identidad de marca de la app. **Todo documento que la app genere o que se imprima desde ella lleva arriba el icono en color y el nombre «ResidApp · Plataforma Asistencial Geriátrica»**, separados del contenido por una línea fina gris. Esto incluye los documentos que se añadan en el futuro.

* **PDF generado en servidor (MigraDoc):** cada sección llama a `PdfBranding.AddHeader(section)` (`src/ResidApp.Infrastructure/Pdf/PdfBranding.cs`), que pone el encabezado en **todas las páginas**. No se escribe un encabezado propio en cada renderizador.
* **Impresión o «guardar como PDF» del navegador:** el encabezado ya está en `Views/Shared/_Layout.cshtml` (bloque `.impresion-marca`, visible solo al imprimir) y se repite **arriba de cada página**, con un hueco fijo para que el contenido nunca quede debajo. `wwwroot/css/site.css` imprime el contenedor `.impresion-pagina` como una tabla y el encabezado como su cabecera (`table-header-group` con `break-inside: avoid`), que es lo que hace que el navegador lo repita. Una vista que oculte elementos al imprimir no debe ocultar ese bloque (por eso es un `div`, no un `header`) ni cambiar el `display` del contenedor.
* **Origen del icono:** `wwwroot/images/logo.svg`. Los PDF de servidor usan su versión PNG, `src/ResidApp.Infrastructure/Pdf/Images/logo.png`, porque PDFsharp no importa SVG. Si cambia el SVG, hay que regenerar el PNG.
* Los documentos ya firmados no se regeneran: conservan el aspecto con el que se firmaron.

---

## 🧙‍♂️ Instrucciones directas para Claude Code
Cuando maquetes código Razor (`.cshtml`) en `src/ResidApp.Web/Views/`, implementa de forma estricta las clases utilitarias de Bootstrap 5 que mapean esta paleta:
* Fondos: `bg-body`, `bg-body-tertiary`, `bg-primary`. **Nunca** `bg-white`, `bg-light`, `text-dark`, `text-black`, `btn-dark`, `btn-light` ni colores fijos en `style=""`: no cambian con el modo oscuro.
* Botones y Badges: `btn-primary`, `btn-success`, `badge text-bg-danger`.
* Bordes semánticos para alertas de residentes: `border-start border-4 border-danger` o `border-success`.
* Toda pantalla nueva o modificada se revisa en claro y en oscuro.
