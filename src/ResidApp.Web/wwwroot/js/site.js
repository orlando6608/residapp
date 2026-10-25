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

// Encuadre de una imagen antes de guardarla, para la foto del residente (_FotoResidenteEditar) y para el logo del centro
// (_LogoCentroCampo, script 0068). Al elegir la imagen se muestra el encuadre (un lienzo), donde se mueve arrastrándola, con los botones
// de flecha o con las flechas del teclado, y se acerca con el deslizador. Lo que se ve en el lienzo es lo que se envía: sale derecha (al
// dibujarla se aplica la orientación de la cámara) y sin los metadatos del fichero original, posición GPS incluida.
//   - Foto: un cuadrado de 480 px que la imagen cubre siempre; se envía como JPEG.
//   - Logo: un recuadro apaisado de 768×256 px (3:1) donde la imagen cabe entera al principio y se puede alejar; los márgenes quedan
//     transparentes y se envía como PNG (si aun así pesa más de lo que admite el servidor, como JPEG sobre fondo blanco).
// Es mejora progresiva: sin JavaScript, o si el navegador no sabe leer la imagen, se envía el fichero tal cual y el servidor aplica sus
// propios límites (ResidentPhoto, CenterLogo).
(() => {
  const encuadre = ({ prefijo, campo: atributoCampo, cubrir, codificar }) => {
    const paso = 16;
    let actual = null;
    let arrastre = null;

    // El desplazamiento (x, y) es el del centro de la imagen respecto al centro del lienzo, en píxeles del lienzo; se limita para que la
    // imagen no salga del lienzo (si lo cubre, para que no queden bordes vacíos).
    const dibujar = () => {
      const { lienzo, imagen } = actual;
      const escala = (cubrir ? Math.max : Math.min)(lienzo.width / imagen.width, lienzo.height / imagen.height) * actual.zoom;
      const ancho = imagen.width * escala;
      const alto = imagen.height * escala;
      const limiteX = Math.abs(ancho - lienzo.width) / 2;
      const limiteY = Math.abs(alto - lienzo.height) / 2;
      actual.x = Math.max(-limiteX, Math.min(limiteX, actual.x));
      actual.y = Math.max(-limiteY, Math.min(limiteY, actual.y));
      const contexto = lienzo.getContext("2d");
      contexto.clearRect(0, 0, lienzo.width, lienzo.height);
      contexto.drawImage(imagen, (lienzo.width - ancho) / 2 + actual.x, (lienzo.height - alto) / 2 + actual.y, ancho, alto);
    };

    // Síncrono (toDataURL, no toBlob): al pulsar «Guardar» justo después de mover, el fichero ya es el del último encuadre.
    // Cambiar files no vuelve a lanzar «change».
    const actualizarFichero = () => {
      const { datos, nombre } = codificar(actual.lienzo);
      const bytes = Uint8Array.from(atob(datos.split(",")[1]), (caracter) => caracter.charCodeAt(0));
      const ficheros = new DataTransfer();
      ficheros.items.add(new File([bytes], nombre, { type: datos.startsWith("data:image/png") ? "image/png" : "image/jpeg" }));
      actual.campo.files = ficheros.files;
    };

    const ajustar = (cambio) => {
      if (!actual) {
        return;
      }
      cambio();
      dibujar();
      actualizarFichero();
    };

    document.addEventListener("change", async (evento) => {
      const campo = evento.target;
      if (!(campo instanceof HTMLInputElement) || !campo.hasAttribute(atributoCampo)) {
        return;
      }

      // El encuadre de una imagen anterior no puede quedar a la vista con otro fichero en el campo.
      const bloque = document.getElementById(campo.dataset[`${prefijo}Encuadre`] ?? "");
      const lienzo = bloque?.querySelector(`[data-${prefijo}-lienzo]`);
      const acercar = bloque?.querySelector(`[data-${prefijo}-acercar]`);
      // La vista previa del logo guardado deja paso al lienzo mientras haya un fichero elegido.
      const vista = document.getElementById(campo.dataset[`${prefijo}Vista`] ?? "");
      actual?.imagen.close();
      actual = null;
      arrastre = null;
      if (bloque) {
        bloque.hidden = true;
      }
      if (vista) {
        vista.hidden = !vista.hasAttribute("src");
      }
      if (campo.files?.length !== 1 || !(lienzo instanceof HTMLCanvasElement) || !(acercar instanceof HTMLInputElement)) {
        return;
      }

      try {
        const archivo = campo.files[0];
        const imagen = await createImageBitmap(archivo, { imageOrientation: "from-image" });
        if (campo.files?.[0] !== archivo) {
          // Mientras se leía se eligió otra imagen.
          imagen.close();
          return;
        }
        actual = { campo, lienzo, acercar, imagen, zoom: 1, x: 0, y: 0 };
        acercar.value = "1";
        bloque.hidden = false;
        if (vista) {
          vista.hidden = true;
        }
        dibujar();
        actualizarFichero();
      } catch {
        // Se envía la imagen original y responde el servidor.
      }
    });

    document.addEventListener("input", (evento) => {
      if (evento.target === actual?.acercar) {
        ajustar(() => { actual.zoom = Number(actual.acercar.value); });
      }
    });

    document.addEventListener("click", (evento) => {
      const boton = evento.target instanceof Element ? evento.target.closest(`[data-${prefijo}-mover]`) : null;
      if (boton) {
        const [dx, dy] = boton.dataset[`${prefijo}Mover`].split(",").map(Number);
        ajustar(() => { actual.x += dx * paso; actual.y += dy * paso; });
      }
    });

    document.addEventListener("keydown", (evento) => {
      if (evento.target !== actual?.lienzo) {
        return;
      }
      const movimientos = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
      if (evento.key in movimientos) {
        const [dx, dy] = movimientos[evento.key];
        ajustar(() => { actual.x += dx * paso; actual.y += dy * paso; });
      } else if (evento.key === "+" || evento.key === "-") {
        ajustar(() => {
          actual.acercar.value = String(Number(actual.acercar.value) + (evento.key === "+" ? 0.1 : -0.1));
          actual.zoom = Number(actual.acercar.value);
        });
      } else {
        return;
      }
      evento.preventDefault();
    });

    // Arrastre con el dedo o el ratón: el movimiento en pantalla se pasa a píxeles del lienzo, que se muestra más pequeño.
    document.addEventListener("pointerdown", (evento) => {
      if (evento.target === actual?.lienzo) {
        actual.lienzo.setPointerCapture(evento.pointerId);
        arrastre = { x: evento.clientX, y: evento.clientY };
      }
    });
    document.addEventListener("pointermove", (evento) => {
      if (arrastre && evento.target === actual?.lienzo) {
        const proporcion = actual.lienzo.width / actual.lienzo.clientWidth;
        actual.x += (evento.clientX - arrastre.x) * proporcion;
        actual.y += (evento.clientY - arrastre.y) * proporcion;
        arrastre = { x: evento.clientX, y: evento.clientY };
        dibujar();
      }
    });
    const soltar = (evento) => {
      if (arrastre && evento.target === actual?.lienzo) {
        arrastre = null;
        actualizarFichero();
      }
    };
    document.addEventListener("pointerup", soltar);
    document.addEventListener("pointercancel", soltar);
  };

  encuadre({
    prefijo: "foto",
    campo: "data-foto-residente",
    cubrir: true,
    codificar: (lienzo) => ({ datos: lienzo.toDataURL("image/jpeg", 0.85), nombre: "foto.jpg" }),
  });

  encuadre({
    prefijo: "logo",
    campo: "data-logo-centro",
    cubrir: false,
    codificar: (lienzo) => {
      const datos = lienzo.toDataURL("image/png");
      if (datos.length * 0.75 <= 480 * 1024) {
        return { datos, nombre: "logo.png" };
      }
      const contexto = lienzo.getContext("2d");
      contexto.globalCompositeOperation = "destination-over";
      contexto.fillStyle = "#fff";
      contexto.fillRect(0, 0, lienzo.width, lienzo.height);
      contexto.globalCompositeOperation = "source-over";
      return { datos: lienzo.toDataURL("image/jpeg", 0.9), nombre: "logo.jpg" };
    },
  });
})();
