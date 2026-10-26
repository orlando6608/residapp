// Importar y exportar por ficheros (Administración, ADR 0010). El fichero no se guarda en el servidor: se queda en el campo del
// formulario y se vuelve a enviar en cada paso (analizar, vista previa, importar, guardar la plantilla) con fetch. Cada respuesta es un
// fragmento de HTML que se pinta en el formulario: el emparejamiento de columnas o, con data-destino="informe", la vista previa, el
// resultado o el aviso de error. Cambiar el fichero o lo que se importa borra lo analizado; cambiar una columna o un valor de sexo
// (data-reanaliza) lo vuelve a analizar, y cualquier cambio en un desplegable borra la vista previa, que ya no valdría. Marcar o desmarcar
// «Importar de todos modos» en un posible duplicado (data-confirmar) recalcula la vista previa. Si el servidor
// redirige (al guardar una plantilla de exportación), se sigue la redirección.
(() => {
  document.querySelectorAll("form[data-intercambio]").forEach((formulario) => {
    const emparejamiento = formulario.querySelector("[data-intercambio-emparejamiento]");
    const informe = formulario.querySelector("[data-intercambio-informe]");
    const estado = formulario.querySelector("[data-intercambio-estado]");
    let enCurso = false;

    const enviar = async (url, destino, foco) => {
      if (enCurso) {
        return;
      }

      // El formulario se bloquea (inert) mientras dura la petición: un cambio hecho entretanto se perdería al pintar la respuesta.
      const datos = new FormData(formulario);
      enCurso = true;
      formulario.inert = true;
      formulario.setAttribute("aria-busy", "true");
      estado.textContent = "Procesando el fichero…";
      try {
        const respuesta = await fetch(url, { method: "POST", body: datos });
        if (respuesta.redirected) {
          window.location.assign(respuesta.url);
          return;
        }

        destino.innerHTML = await respuesta.text();
        estado.textContent = "";
      } catch {
        estado.textContent = "No se ha podido enviar el fichero. Comprueba la conexión y vuelve a intentarlo.";
      } finally {
        enCurso = false;
        formulario.inert = false;
        formulario.removeAttribute("aria-busy");
      }

      // Tras un botón, el foco va al título (o al aviso) de lo pintado; tras cambiar un desplegable, vuelve al mismo desplegable.
      (foco ? document.getElementById(foco) : destino.querySelector("[tabindex='-1']"))?.focus();
    };

    formulario.addEventListener("submit", (evento) => {
      evento.preventDefault();
      const boton = evento.submitter;
      const aInforme = boton?.dataset.destino === "informe";
      if (!aInforme) {
        informe.innerHTML = "";
      }

      enviar(boton?.hasAttribute("formaction") ? boton.formAction : formulario.action, aInforme ? informe : emparejamiento, null);
    });

    formulario.addEventListener("change", (evento) => {
      const campo = evento.target;
      if (campo.hasAttribute("data-intercambio-reinicio")) {
        emparejamiento.innerHTML = "";
        informe.innerHTML = "";
      } else if (campo.hasAttribute("data-confirmar")) {
        // «Importar de todos modos» de un posible duplicado: la vista previa se recalcula con la fila confirmada (o no) y el foco vuelve a la casilla.
        // Cada fila tiene dos casillas (la lista del móvil y la tabla; una está oculta y el formulario envía las dos): se igualan antes de enviar.
        formulario.querySelectorAll(`[data-confirmar][value="${campo.value}"]`).forEach((otra) => (otra.checked = campo.checked));
        enviar(campo.dataset.vistaPrevia, informe, campo.id);
      } else if (emparejamiento.contains(campo) && campo instanceof HTMLSelectElement) {
        // Solo los desplegables cambian lo que se importa. El nombre de la plantilla no: su «change» llega al pulsar «Importar» y
        // borraría el botón antes del clic.
        informe.innerHTML = "";
        if (campo.hasAttribute("data-reanaliza")) {
          enviar(formulario.action, emparejamiento, campo.id);
        }
      }
    });
  });
})();
