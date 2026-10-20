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

// Foto de identificación del residente (_FotoResidenteEditar): al elegir una foto, el navegador la reduce a 480 px de lado mayor y la
// vuelve a codificar en JPEG antes de enviarla. Así pesa poco, sale derecha (al dibujarla se aplica la orientación de la cámara) y no
// lleva los metadatos de la cámara, posición GPS incluida. Es mejora progresiva: sin JavaScript, o si el navegador no sabe leer la
// imagen, se envía el fichero tal cual y el servidor solo acepta un JPEG de 1 MB como máximo (ResidentPhoto).
(() => {
  const ladoMaximo = 480;

  document.addEventListener("change", async (evento) => {
    const campo = evento.target;
    if (!(campo instanceof HTMLInputElement) || !campo.hasAttribute("data-foto-residente") || campo.files?.length !== 1) {
      return;
    }

    try {
      const imagen = await createImageBitmap(campo.files[0], { imageOrientation: "from-image" });
      const escala = Math.min(1, ladoMaximo / Math.max(imagen.width, imagen.height));
      const lienzo = document.createElement("canvas");
      lienzo.width = Math.round(imagen.width * escala);
      lienzo.height = Math.round(imagen.height * escala);
      lienzo.getContext("2d").drawImage(imagen, 0, 0, lienzo.width, lienzo.height);
      imagen.close();
      const foto = await new Promise((resolver) => lienzo.toBlob(resolver, "image/jpeg", 0.85));
      if (!foto) {
        return;
      }

      // Cambiar files no vuelve a lanzar «change».
      const ficheros = new DataTransfer();
      ficheros.items.add(new File([foto], "foto.jpg", { type: "image/jpeg" }));
      campo.files = ficheros.files;

      const vista = document.getElementById(campo.dataset.fotoVista ?? "");
      if (vista instanceof HTMLImageElement) {
        if (vista.src.startsWith("blob:")) {
          URL.revokeObjectURL(vista.src);
        }
        vista.src = URL.createObjectURL(foto);
        vista.hidden = false;
      }
    } catch {
      // Se envía la foto original y responde el servidor.
    }
  });
})();
