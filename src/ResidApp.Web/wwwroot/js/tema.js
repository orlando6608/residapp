// Tema claro, oscuro o automático (docs/bocetos-pantallas/guia-diseno-sistema-visual.md, «Modo oscuro»).
// Se carga en <head> para fijar data-bs-theme antes de pintar la página. La preferencia es de interfaz y de cada
// dispositivo: no guarda ningún dato clínico (docs/decisiones-arquitectura/directrices-pwa-movil.md, §5).
(() => {
  const clave = "residapp-tema";
  const oscuroDelSistema = window.matchMedia("(prefers-color-scheme: dark)");
  const colorDeBarra = { light: "#0A58CA", dark: "#131B26" };

  let preferencia = "auto";
  try {
    const guardada = localStorage.getItem(clave);
    if (guardada === "claro" || guardada === "oscuro") {
      preferencia = guardada;
    }
  } catch {
    // Sin almacenamiento (modo privado, bloqueado): se queda en automático.
  }

  const fijarTema = (tema) => {
    document.documentElement.setAttribute("data-bs-theme", tema);
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", colorDeBarra[tema]);
  };

  const aplicar = () => {
    fijarTema(preferencia === "oscuro" || (preferencia === "auto" && oscuroDelSistema.matches) ? "dark" : "light");
    document.querySelectorAll("[data-tema-opcion]").forEach((opcion) => {
      const elegida = opcion.dataset.temaOpcion === preferencia;
      opcion.setAttribute("aria-pressed", String(elegida));
      opcion.classList.toggle("active", elegida);
    });
  };

  aplicar();
  oscuroDelSistema.addEventListener("change", aplicar);

  // Al imprimir o guardar como PDF, la página sale siempre en claro.
  window.addEventListener("beforeprint", () => fijarTema("light"));
  window.addEventListener("afterprint", aplicar);

  document.addEventListener("DOMContentLoaded", () => {
    aplicar();
    document.querySelectorAll("[data-tema-opcion]").forEach((opcion) =>
      opcion.addEventListener("click", () => {
        preferencia = opcion.dataset.temaOpcion;
        try {
          localStorage.setItem(clave, preferencia);
        } catch {
          // Sin almacenamiento: el tema elegido vale solo para esta página.
        }
        aplicar();
      }));
  });
})();
