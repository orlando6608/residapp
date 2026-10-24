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

// Foto de identificación del residente (_FotoResidenteEditar): al elegir una foto, se muestra el encuadre (un lienzo cuadrado de
// 480 px), donde se mueve arrastrándola, con los botones de flecha o con las flechas del teclado, y se acerca con «Acercar». Lo que
// se ve en el lienzo es lo que se envía: un JPEG cuadrado de 480 px, que pesa poco, sale derecho (al dibujarla se aplica la
// orientación de la cámara) y no lleva los metadatos de la cámara, posición GPS incluida. La foto cubre siempre todo el cuadrado.
// Es mejora progresiva: sin JavaScript, o si el navegador no sabe leer la imagen, se envía el fichero tal cual y el servidor solo
// acepta un JPEG de 1 MB como máximo (ResidentPhoto).
(() => {
  const lado = 480;
  const paso = 16;
  let foto = null;
  let arrastre = null;

  // El desplazamiento (x, y) es el del centro de la foto respecto al centro del cuadrado, en píxeles del lienzo; se limita para
  // que no queden bordes vacíos.
  const dibujar = () => {
    const escala = Math.max(lado / foto.imagen.width, lado / foto.imagen.height) * foto.zoom;
    const ancho = foto.imagen.width * escala;
    const alto = foto.imagen.height * escala;
    foto.x = Math.max(-(ancho - lado) / 2, Math.min((ancho - lado) / 2, foto.x));
    foto.y = Math.max(-(alto - lado) / 2, Math.min((alto - lado) / 2, foto.y));
    foto.lienzo.getContext("2d").drawImage(foto.imagen, (lado - ancho) / 2 + foto.x, (lado - alto) / 2 + foto.y, ancho, alto);
  };

  // Síncrono (toDataURL, no toBlob): al pulsar «Guardar foto» justo después de mover, el fichero ya es el del último encuadre.
  // Cambiar files no vuelve a lanzar «change».
  const actualizarFichero = () => {
    const datos = atob(foto.lienzo.toDataURL("image/jpeg", 0.85).split(",")[1]);
    const bytes = Uint8Array.from(datos, (caracter) => caracter.charCodeAt(0));
    const ficheros = new DataTransfer();
    ficheros.items.add(new File([bytes], "foto.jpg", { type: "image/jpeg" }));
    foto.campo.files = ficheros.files;
  };

  const ajustar = (cambio) => {
    if (!foto) {
      return;
    }
    cambio();
    dibujar();
    actualizarFichero();
  };

  document.addEventListener("change", async (evento) => {
    const campo = evento.target;
    if (!(campo instanceof HTMLInputElement) || !campo.hasAttribute("data-foto-residente")) {
      return;
    }

    // El encuadre de una foto anterior no puede quedar a la vista con otro fichero en el campo.
    const encuadre = document.getElementById(campo.dataset.fotoEncuadre ?? "");
    const lienzo = encuadre?.querySelector("[data-foto-lienzo]");
    const acercar = encuadre?.querySelector("[data-foto-acercar]");
    foto?.imagen.close();
    foto = null;
    arrastre = null;
    if (encuadre) {
      encuadre.hidden = true;
    }
    if (campo.files?.length !== 1 || !(lienzo instanceof HTMLCanvasElement) || !(acercar instanceof HTMLInputElement)) {
      return;
    }

    try {
      const archivo = campo.files[0];
      const imagen = await createImageBitmap(archivo, { imageOrientation: "from-image" });
      if (campo.files?.[0] !== archivo) {
        // Mientras se leía se eligió otra foto.
        imagen.close();
        return;
      }
      foto = { campo, lienzo, acercar, imagen, zoom: 1, x: 0, y: 0 };
      acercar.value = "1";
      encuadre.hidden = false;
      dibujar();
      actualizarFichero();
    } catch {
      // Se envía la foto original y responde el servidor.
    }
  });

  document.addEventListener("input", (evento) => {
    if (evento.target === foto?.acercar) {
      ajustar(() => { foto.zoom = Number(foto.acercar.value); });
    }
  });

  document.addEventListener("click", (evento) => {
    const boton = evento.target instanceof Element ? evento.target.closest("[data-foto-mover]") : null;
    if (boton) {
      const [dx, dy] = boton.dataset.fotoMover.split(",").map(Number);
      ajustar(() => { foto.x += dx * paso; foto.y += dy * paso; });
    }
  });

  document.addEventListener("keydown", (evento) => {
    if (evento.target !== foto?.lienzo) {
      return;
    }
    const movimientos = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (evento.key in movimientos) {
      const [dx, dy] = movimientos[evento.key];
      ajustar(() => { foto.x += dx * paso; foto.y += dy * paso; });
    } else if (evento.key === "+" || evento.key === "-") {
      ajustar(() => {
        foto.acercar.value = String(Number(foto.acercar.value) + (evento.key === "+" ? 0.1 : -0.1));
        foto.zoom = Number(foto.acercar.value);
      });
    } else {
      return;
    }
    evento.preventDefault();
  });

  // Arrastre con el dedo o el ratón: el movimiento en pantalla se pasa a píxeles del lienzo, que se muestra más pequeño.
  document.addEventListener("pointerdown", (evento) => {
    if (evento.target === foto?.lienzo) {
      foto.lienzo.setPointerCapture(evento.pointerId);
      arrastre = { x: evento.clientX, y: evento.clientY };
    }
  });
  document.addEventListener("pointermove", (evento) => {
    if (arrastre && evento.target === foto?.lienzo) {
      const proporcion = lado / foto.lienzo.clientWidth;
      foto.x += (evento.clientX - arrastre.x) * proporcion;
      foto.y += (evento.clientY - arrastre.y) * proporcion;
      arrastre = { x: evento.clientX, y: evento.clientY };
      dibujar();
    }
  });
  const soltar = (evento) => {
    if (arrastre && evento.target === foto?.lienzo) {
      arrastre = null;
      actualizarFichero();
    }
  };
  document.addEventListener("pointerup", soltar);
  document.addEventListener("pointercancel", soltar);
})();

// Logo del centro (_LogoCentroCampo, script 0068): al elegir una imagen se reduce a 512 px por el lado mayor, sin recortarla ni deformarla,
// y se envía como PNG, que conserva la transparencia (si aun así pesa más de lo que admite el servidor, como JPEG sobre fondo blanco). Sale
// sin los metadatos del fichero original. Muestra la vista previa en el elemento de data-logo-vista. Es mejora progresiva: sin JavaScript,
// o si el navegador no sabe leer la imagen, se envía el fichero tal cual y el servidor solo acepta un PNG o un JPEG de 512 KB y 1024 px
// como máximo (CenterLogo).
(() => {
  const maximo = 512;
  const pesoMaximo = 480 * 1024;

  document.addEventListener("change", async (evento) => {
    const campo = evento.target;
    if (!(campo instanceof HTMLInputElement) || !campo.hasAttribute("data-logo-centro") || campo.files?.length !== 1) {
      return;
    }

    const vista = document.getElementById(campo.dataset.logoVista ?? "");
    try {
      const archivo = campo.files[0];
      const imagen = await createImageBitmap(archivo, { imageOrientation: "from-image" });
      if (campo.files?.[0] !== archivo) {
        // Mientras se leía se eligió otro fichero.
        imagen.close();
        return;
      }
      const escala = Math.min(1, maximo / Math.max(imagen.width, imagen.height));
      const lienzo = document.createElement("canvas");
      lienzo.width = Math.max(1, Math.round(imagen.width * escala));
      lienzo.height = Math.max(1, Math.round(imagen.height * escala));
      const contexto = lienzo.getContext("2d");
      contexto.drawImage(imagen, 0, 0, lienzo.width, lienzo.height);
      imagen.close();

      let datos = lienzo.toDataURL("image/png");
      let nombre = "logo.png";
      if (datos.length * 0.75 > pesoMaximo) {
        contexto.globalCompositeOperation = "destination-over";
        contexto.fillStyle = "#fff";
        contexto.fillRect(0, 0, lienzo.width, lienzo.height);
        datos = lienzo.toDataURL("image/jpeg", 0.9);
        nombre = "logo.jpg";
      }
      const bytes = Uint8Array.from(atob(datos.split(",")[1]), (caracter) => caracter.charCodeAt(0));
      const ficheros = new DataTransfer();
      ficheros.items.add(new File([bytes], nombre, { type: datos.startsWith("data:image/png") ? "image/png" : "image/jpeg" }));
      campo.files = ficheros.files;
      if (vista instanceof HTMLImageElement) {
        vista.src = datos;
        vista.hidden = false;
      }
    } catch {
      // Se envía el fichero original y responde el servidor.
    }
  });
})();
