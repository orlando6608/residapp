// Indicador de envío (docs/bocetos-pantallas/guia-diseno-sistema-visual.md, «Indicador de envío»): al enviar un formulario
// válido, el botón que lo envía muestra un spinner y se anuncia «Enviando…». Es mejora progresiva: sin JavaScript el
// formulario se envía igual. No usa `disabled` (el valor del botón, p. ej. Accion=Firmar, no viajaría en el POST): un segundo
// envío mientras el primero está en curso se descarta aquí; la defensa real contra el duplicado sigue siendo el operationId
// en el servidor. Un formulario que no navega (descarga de archivo) se excluye con data-sin-carga.
(() => {
  const estado = document.createElement("div");
  estado.className = "visually-hidden";
  estado.setAttribute("role", "status");
  document.body.append(estado);

  const limpiar = () => {
    estado.textContent = "";
    document.querySelectorAll("form[aria-busy]").forEach((formulario) => formulario.removeAttribute("aria-busy"));
    document.querySelectorAll("[data-enviando]").forEach((boton) => {
      boton.removeAttribute("data-enviando");
      boton.removeAttribute("aria-disabled");
      boton.querySelector(".spinner-border")?.remove();
    });
  };

  // Escucha en document y en fase de burbuja: la validación de jquery-validate ya ha actuado sobre el formulario y, si
  // es inválido, ha cancelado el envío (defaultPrevented).
  document.addEventListener("submit", (evento) => {
    const formulario = evento.target;
    if (!(formulario instanceof HTMLFormElement) || formulario.hasAttribute("data-sin-carga")) {
      return;
    }
    if (formulario.getAttribute("aria-busy") === "true") {
      evento.preventDefault();
      return;
    }
    if (evento.defaultPrevented) {
      return;
    }

    formulario.setAttribute("aria-busy", "true");
    estado.textContent = "Enviando…";

    const boton = evento.submitter ?? formulario.querySelector('button[type="submit"], button:not([type])');
    if (boton instanceof HTMLButtonElement) {
      boton.setAttribute("data-enviando", "");
      boton.setAttribute("aria-disabled", "true");
      const spinner = document.createElement("span");
      spinner.className = "spinner-border spinner-border-sm me-2";
      spinner.setAttribute("aria-hidden", "true");
      boton.prepend(spinner);
    }
  });

  // Al volver con «atrás» la página puede recuperarse del bfcache con el envío aún marcado.
  window.addEventListener("pageshow", (evento) => {
    if (evento.persisted) {
      limpiar();
    }
  });
})();
