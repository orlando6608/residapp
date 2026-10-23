// Dictado por voz de «Registrar cambio» (ADR 0009 y 0011). Graba la voz de la auxiliar, la envía al servidor (que la transcribe y estructura con
// Mistral, sin guardar nada) y recibe una SUGERENCIA de áreas, opciones y notas. Aquí no se decide nada clínico: la sugerencia solo marca
// casillas y rellena notas, y cada opción sugerida se revisa una a una («Está bien» o «Quitar») antes de poder pulsar «Continuar».
// Sin micrófono, sin JavaScript o ante cualquier fallo, el formulario se rellena a mano como siempre.
(() => {
  const tarjeta = document.getElementById("dictado");
  if (!tarjeta || !navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
    return;
  }

  const formulario = tarjeta.closest("form");
  const boton = document.getElementById("dictado-boton");
  const textoBoton = boton.querySelector("[data-texto]");
  const tiempo = document.getElementById("dictado-tiempo");
  const estado = document.getElementById("dictado-estado");
  const entendido = document.getElementById("dictado-entendido");
  const transcripcion = document.getElementById("dictado-transcripcion");
  const revision = document.getElementById("dictado-revision");
  const revisionTitulo = document.getElementById("dictado-revision-titulo");
  const lista = document.getElementById("dictado-revision-lista");
  const plantillaMarca = document.getElementById("dictado-marca");
  const plantillaFila = document.getElementById("dictado-revision-fila");
  const continuar = document.getElementById("registrar-continuar");
  const aviso = document.getElementById("dictado-bloqueo");
  const maxSegundos = Number(tarjeta.dataset.maxSegundos) || 120;

  // Lo que está pendiente de revisar: las opciones sugeridas (una fila cada una) y las notas sugeridas que nadie ha tocado.
  const opcionesPendientes = new Map(); // input -> fila
  const notasSugeridas = new Set(); // textarea

  let grabadora = null;
  let flujo = null;
  let trozos = [];
  let inicio = 0;
  let reloj = 0;
  let tope = 0;

  const token = () => formulario.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";
  const decir = (texto) => { estado.textContent = texto; };

  // ---- grabar ----

  const tipoDeAudio = () =>
    ["audio/webm;codecs=opus", "audio/webm", "audio/mp4", "audio/ogg;codecs=opus"].find((t) => MediaRecorder.isTypeSupported(t)) ?? "";

  const ponerBoton = (modo) => {
    // «Procesando» no usa disabled: el foco se mantiene y el lector de pantalla lo anuncia.
    boton.setAttribute("aria-pressed", modo === "grabando" ? "true" : "false");
    boton.setAttribute("aria-disabled", modo === "procesando" ? "true" : "false");
    boton.classList.toggle("btn-outline-primary", modo !== "grabando");
    boton.classList.toggle("btn-danger", modo === "grabando");
    textoBoton.textContent = modo === "grabando" ? "Parar" : modo === "procesando" ? "Procesando…" : "Grabar";
  };

  const formatoTiempo = (segundos) => `${Math.floor(segundos / 60)}:${String(segundos % 60).padStart(2, "0")}`;

  const soltarMicro = () => {
    clearInterval(reloj);
    clearTimeout(tope);
    flujo?.getTracks().forEach((pista) => pista.stop());
    flujo = null;
    tiempo.textContent = "";
  };

  const empezar = async () => {
    try {
      flujo = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (error) {
      decir(error?.name === "NotFoundError"
        ? "No se encuentra ningún micrófono. Puedes rellenar el formulario a mano."
        : "No hay permiso para usar el micrófono. Puedes rellenarlo a mano o dar el permiso en el navegador.");
      return;
    }
    const tipo = tipoDeAudio();
    trozos = [];
    grabadora = new MediaRecorder(flujo, tipo ? { mimeType: tipo } : undefined);
    grabadora.addEventListener("dataavailable", (e) => e.data.size > 0 && trozos.push(e.data));
    grabadora.addEventListener("stop", () => enviar(new Blob(trozos, { type: grabadora.mimeType || tipo })));
    grabadora.start();
    inicio = Date.now();
    ponerBoton("grabando");
    decir("Grabando. Cuenta lo que ha pasado y pulsa Parar. No digas nombres.");
    reloj = setInterval(() => { tiempo.textContent = formatoTiempo(Math.floor((Date.now() - inicio) / 1000)); }, 500);
    tope = setTimeout(() => { if (grabadora?.state === "recording") { parar(`Se ha parado solo a los ${maxSegundos / 60} minutos.`); } }, maxSegundos * 1000);
  };

  const parar = (motivo) => {
    if (grabadora?.state !== "recording") {
      return;
    }
    soltarMicro();
    ponerBoton("procesando");
    decir(motivo ? `${motivo} Procesando el dictado…` : "Procesando el dictado…");
    grabadora.stop();
  };

  // ---- enviar y recibir ----

  const extension = (tipo) => (tipo.includes("mp4") ? "mp4" : tipo.includes("ogg") ? "ogg" : "webm");

  const mensajeDe = async (respuesta) => {
    if (respuesta.status === 429) {
      return "Has usado el dictado muchas veces seguidas. Espera un minuto o rellena el formulario a mano.";
    }
    if (respuesta.status === 403) {
      return "No puedes usar el dictado con este residente.";
    }
    if (respuesta.status === 404) {
      return "El dictado no está activado. Rellena el formulario a mano.";
    }
    if (respuesta.status === 400) {
      return "No se ha podido usar ese audio. Prueba de nuevo o rellena el formulario a mano.";
    }
    try {
      return (await respuesta.json()).mensaje ?? "El dictado no está disponible ahora. Puedes rellenar el formulario a mano.";
    } catch {
      return "El dictado no está disponible ahora. Puedes rellenar el formulario a mano.";
    }
  };

  const enviar = async (audio) => {
    if (audio.size === 0) {
      ponerBoton("reposo");
      decir("No se ha grabado nada. Vuelve a intentarlo.");
      return;
    }
    const datos = new FormData();
    datos.append("residenteId", tarjeta.dataset.residente);
    datos.append("operacionId", crypto.randomUUID());
    datos.append("audio", audio, `dictado.${extension(audio.type)}`);
    try {
      const respuesta = await fetch(tarjeta.dataset.url, {
        method: "POST", credentials: "same-origin", headers: { RequestVerificationToken: token() }, body: datos,
      });
      if (!respuesta.ok) {
        decir(await mensajeDe(respuesta));
        return;
      }
      aplicar(await respuesta.json());
    } catch {
      decir("No se ha podido enviar el dictado. Comprueba la conexión o rellena el formulario a mano.");
    } finally {
      ponerBoton("reposo");
    }
  };

  // ---- marcar y revisar ----

  const leyendaDe = (campo) => campo.closest("fieldset")?.querySelector("legend");

  /** La etiqueta «Sugerido por voz» junto al nombre del área mientras quede algo sin revisar en ella. */
  const actualizarMarca = (fieldset) => {
    const leyenda = fieldset?.querySelector("legend");
    if (!leyenda) {
      return;
    }
    const pendiente = [...opcionesPendientes.keys()].some((i) => i.closest("fieldset") === fieldset)
      || [...notasSugeridas].some((n) => n.closest("fieldset") === fieldset);
    const marca = leyenda.querySelector("[data-marca-voz]");
    if (pendiente && !marca) {
      leyenda.append(plantillaMarca.content.cloneNode(true));
    } else if (!pendiente && marca) {
      marca.remove();
    }
  };

  const actualizarBloqueo = () => {
    const pendientes = opcionesPendientes.size;
    revision.hidden = pendientes === 0;
    aviso.hidden = pendientes === 0;
    if (pendientes > 0) {
      continuar.setAttribute("aria-disabled", "true");
      continuar.setAttribute("data-bloqueado-dictado", "");
      continuar.setAttribute("aria-describedby", "dictado-bloqueo");
    } else {
      continuar.removeAttribute("data-bloqueado-dictado");
      continuar.removeAttribute("aria-describedby");
      if (!continuar.hasAttribute("data-enviando")) {
        continuar.removeAttribute("aria-disabled");
      }
    }
  };

  const resolver = (input, mensaje) => {
    const fila = opcionesPendientes.get(input);
    if (!fila) {
      return;
    }
    const siguiente = fila.nextElementSibling?.querySelector("button") ?? fila.previousElementSibling?.querySelector("button");
    opcionesPendientes.delete(input);
    fila.remove();
    actualizarMarca(input.closest("fieldset"));
    actualizarBloqueo();
    if (mensaje) {
      decir(opcionesPendientes.size === 0 ? `${mensaje} Ya has revisado todo: puedes continuar.` : `${mensaje} Quedan ${opcionesPendientes.size} por revisar.`);
    }
    // El foco no se pierde al desaparecer la fila: pasa a la siguiente sugerencia o, si no quedan, al botón Grabar.
    (siguiente ?? boton).focus();
  };

  const anadirOpcion = (input) => {
    if (opcionesPendientes.has(input)) {
      return;
    }
    const nombre = document.querySelector(`label[for="${input.id}"]`)?.textContent.trim() ?? "";
    const area = leyendaDe(input)?.firstChild?.textContent.trim() ?? "";
    const fila = plantillaFila.content.firstElementChild.cloneNode(true);
    fila.querySelector("[data-opcion]").textContent = nombre;
    fila.querySelector("[data-area]").textContent = area ? `(${area})` : "";
    fila.querySelector('[data-accion="confirmar"]').setAttribute("aria-label", `Está bien: ${nombre}`);
    fila.querySelector('[data-accion="quitar"]').setAttribute("aria-label", `Quitar: ${nombre}`);
    fila.querySelector('[data-accion="confirmar"]').addEventListener("click", () => resolver(input, `${nombre}: confirmado.`));
    fila.querySelector('[data-accion="quitar"]').addEventListener("click", () => {
      input.checked = false;
      resolver(input, `${nombre}: quitado.`);
    });
    opcionesPendientes.set(input, fila);
    lista.append(fila);
    actualizarMarca(input.closest("fieldset"));
    // Tocar la casilla a mano (marcarla o desmarcarla) también es revisarla.
    input.addEventListener("change", () => resolver(input, `${nombre}: cambiado a mano.`), { once: true });
  };

  const anadirNota = (area, texto) => {
    const campo = document.getElementById(`area-${area}`);
    if (!campo || !texto) {
      return false;
    }
    const actual = campo.value.trim();
    if (actual.includes(texto)) {
      return false;
    }
    campo.value = (actual ? `${actual}\n${texto}` : texto).slice(0, campo.maxLength > 0 ? campo.maxLength : undefined);
    campo.closest("details")?.setAttribute("open", "");
    if (!notasSugeridas.has(campo)) {
      notasSugeridas.add(campo);
      // Tocar la nota o editarla quita la marca: ya la ha visto o el texto ya es de la persona. (Solo el foco por teclado no cuenta, para que
      // pasar por el campo con el tabulador no la borre sin haberla leído.)
      const tocada = () => {
        notasSugeridas.delete(campo);
        actualizarMarca(campo.closest("fieldset"));
        campo.removeEventListener("input", tocada);
        campo.removeEventListener("pointerdown", tocada);
      };
      campo.addEventListener("input", tocada);
      campo.addEventListener("pointerdown", tocada);
    }
    actualizarMarca(campo.closest("fieldset"));
    return true;
  };

  const aplicar = (respuesta) => {
    transcripcion.textContent = respuesta.transcripcion ?? "";
    entendido.hidden = !respuesta.transcripcion;

    let cambios = 0;
    for (const area of respuesta.areas ?? []) {
      for (const opcion of area.opciones ?? []) {
        const input = document.getElementById(`opcion-${area.area}-${opcion}`);
        if (input && !input.checked) {
          input.checked = true;
          anadirOpcion(input);
          cambios += 1;
        }
      }
      if (anadirNota(area.area, area.texto)) {
        cambios += 1;
      }
    }

    actualizarBloqueo();
    if (cambios === 0) {
      decir("No se ha entendido nada que se pueda anotar. Puedes volver a grabar o rellenar el formulario a mano.");
      return;
    }
    decir(opcionesPendientes.size > 0
      ? `Se ha rellenado el formulario con una sugerencia. Revisa las ${opcionesPendientes.size} opciones marcadas, una a una, antes de continuar.`
      : "Se han añadido notas sugeridas. Revísalas y edítalas si hace falta.");
    if (opcionesPendientes.size > 0) {
      revisionTitulo.focus();
    }
  };

  // ---- el modo estricto: sin revisar lo sugerido no se continúa ----

  formulario.addEventListener("submit", (evento) => {
    if (opcionesPendientes.size === 0) {
      return;
    }
    evento.preventDefault();
    decir(`Revisa las ${opcionesPendientes.size} opciones sugeridas por voz (Está bien o Quitar) antes de continuar.`);
    lista.querySelector("button")?.focus();
  });

  boton.addEventListener("click", () => {
    if (boton.getAttribute("aria-disabled") === "true") {
      return;
    }
    if (grabadora?.state === "recording") {
      parar();
    } else {
      empezar();
    }
  });

  window.addEventListener("pagehide", soltarMicro);
  tarjeta.hidden = false;
})();
