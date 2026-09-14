# Guía de Sistema Visual y Paleta de Colores: ResidApp

**Propósito:** Definir los estándares estéticos y cromáticos para las vistas Razor (.cshtml) de ResidApp. Toda la maquetación debe ser Mobile-First, accesible (cumpliendo WCAG 2.2 AA) y basada en los componentes responsivos de Bootstrap 5.

---

## 🎨 1. Paleta de Colores Oficial (Variables CSS)

El sistema visual utiliza una base clínica corporativa (Azul), combinada con el factor humano sociosanitario (Verde Teal) y un sistema defensivo de estados de alerta.

```css
:root {
  /* 🔵 Colores Principales (Marca, Rigor Técnico y Seguridad) */
  --color-primary-brand: #0D6EFD;       /* Azul Bootstrap (Acciones principales, enlaces) */
  --color-primary-dark:  #0A58CA;       /* Azul Oscuro (Encabezados corporativos, botones activos) */
  
  /* 🩺 Colores Secundarios (El Toque Clínico Asistencial de CJ) */
  --color-secondary-teal: #198754;      /* Verde Sanitario (Estados basales estables, éxitos) */
  --color-secondary-light:#20C997;      /* Menta/Teal Brillante (Destacados asistenciales) */

  /* 🏛️ Neutros de Superficie (Especial PWA Móvil: Evita fatiga visual) */
  --color-bg-main:         #F8F9FA;      /* Fondo de aplicación claro grisáceo */
  --color-bg-card:         #FFFFFF;      /* Fondo de tarjetas y formularios */
  --color-text-dark:       #212529;      /* Texto principal de alta legibilidad */
  --color-text-muted:      #6C757D;      /* Subtítulos, horas de relevos y metadatos */
  
  /* 🚨 Sistema Semafórico de Alertas Clínicas (Innegociable del PRD) */
  --color-alert-danger:    #DC3545;      /* Cambios basales críticos, incidencias prioritarias */
  --color-alert-warning:   #FFC107;      /* Observaciones en revisión, datos pendientes */
  --color-alert-info:      #0DCAF0;      /* Información complementaria, avisos familiares */
}
```

---

## 📱 2. Guía de Aplicación en la Interfaz (UI)

### A. Pantalla de Autenticación (Login)
* **Fondo:** Blanco limpio (`--color-bg-card`) o un degradado sutil hacia el gris claro.
* **Componente Central:** El logotipo SVG inyectado centrado con un ancho máximo de `120px` en móviles.
* **Formulario:** Inputs grandes con la clase `.form-control-lg` de Bootstrap para facilitar la pulsación táctil. El botón de acceso debe usar `.btn-primary` (`#0D6EFD`).

### B. El Panel Principal (Dashboard del Personal de Planta)
Pensado para pantallas táctiles de tablets y smartphones (Auxiliares y Enfermería en movimiento).
* **Cabecera (Navbar):** Fondo azul oscuro (`--color-primary-dark`) con el isotipo en blanco y el nombre **ResidApp** visible. Debe mostrar claramente el **Perfil Activo** (ej: *"Rol: ENFERMERÍA"* con una etiqueta tipo *badge*).
* **Cuerpo:** Fondo gris claro (`--color-bg-main`). Los residentes no se listarán en tablas densas de escritorio; se presentarán en **tarjetas móviles responsivas** (`.card`).
* **Tarjetas de Residentes:**
  * Nombre en texto oscuro destacado (`--color-text-dark`).
  * Indicador visual izquierdo (borde de `4px` sólido): **Verde** si su estado basal está resuelto, **Rojo** si hay una alerta o cambio funcional sin resolver (exigencia estricta del PRD).

### C. Formularios de Registro Clínico (Firma de Estado Basal)
* **Inputs:** Utilizar agrupaciones limpias con `.mb-3`.
* **Botones de Acción Inmediata:** El botón crítico de "Firmar Estado Basal" o "Guardar Incidencia" debe estar ubicado en la zona inferior derecha, ser de tamaño grande `.btn-lg` y usar colores semánticos inequívocos (`.btn-success` para firmas basales estables).

---

## ♿ 3. Accesibilidad Táctica (WCAG 2.2 AA)

Al tratar con perfiles que van desde auxiliares con turnos nocturnos hasta **Familiares** de avanzada edad, el contraste es una prioridad de ingeniería:
1. **Contraste de Texto:** Jamás se usará texto blanco sobre fondos verde claro o amarillo. Los textos sobre el color de alerta amarillo (`--color-alert-warning`) irán obligatoriamente en negro (`#000000`).
2. **Tamaño Táctil Mínimo:** Todos los elementos interactivos (botones, selectores de catálogos como Barthel, enlaces) deben tener una altura mínima de **48px** para evitar errores de pulsación en movilidad.

---

## 🧙‍♂️ Instrucciones directas para Claude Code
Cuando maquetes código Razor (`.cshtml`) en `src/ResidApp.Web/Views/`, implementa de forma estricta las clases utilitarias de Bootstrap 5 que mapean esta paleta:
* Fondos: `bg-light`, `bg-white`, `bg-primary`.
* Botones y Badges: `btn-primary`, `btn-success`, `badge-danger`.
* Bordes semánticos para alertas de residentes: `border-start border-4 border-danger` o `border-success`.
