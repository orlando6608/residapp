// Passkeys (ADR 0007, incremento I4): entrar con una passkey y registrar una nueva. El servidor da las opciones de WebAuthn, el dispositivo
// pide la huella, la cara o el PIN, y el servidor valida la credencial que vuelve. Aquí no se decide nada de seguridad.
(() => {
  const soportado = () => !!(window.PublicKeyCredential && PublicKeyCredential.parseCreationOptionsFromJSON
    && PublicKeyCredential.parseRequestOptionsFromJSON && navigator.credentials);

  const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? "";

  const enviar = (url, cuerpo) => {
    const opciones = { method: "POST", credentials: "same-origin", headers: { RequestVerificationToken: token() } };
    if (cuerpo instanceof FormData) {
      opciones.body = cuerpo;
    } else if (cuerpo) {
      opciones.headers["Content-Type"] = "application/json";
      opciones.body = JSON.stringify(cuerpo);
    }
    return fetch(url, opciones);
  };

  const mostrarError = (caja, texto) => {
    caja.textContent = texto;
    caja.hidden = false;
  };

  // Lo que dice el navegador cuando la persona cancela o el dispositivo no tiene nada que ofrecer.
  const textoDeError = (error, porDefecto) =>
    error && error.name === "NotAllowedError" ? "Se ha cancelado o ha pasado demasiado tiempo. Vuelve a intentarlo." : porDefecto;

  const botonEntrar = document.getElementById("entrar-passkey");
  if (botonEntrar) {
    const caja = document.getElementById("passkey-entrada-error");
    if (!soportado()) {
      botonEntrar.hidden = true;
    }

    botonEntrar.addEventListener("click", async () => {
      caja.hidden = true;
      botonEntrar.disabled = true;
      try {
        const opciones = await enviar(botonEntrar.dataset.urlOpciones);
        if (!opciones.ok) {
          throw new Error("opciones");
        }
        const credencial = await navigator.credentials.get({
          publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(await opciones.json()),
        });
        const respuesta = await enviar(botonEntrar.dataset.urlEntrar, {
          credentialJson: JSON.stringify(credencial.toJSON()),
          returnUrl: botonEntrar.dataset.returnUrl || null,
        });
        const datos = await respuesta.json();
        if (!respuesta.ok) {
          mostrarError(caja, datos.error || "No se ha podido entrar con esa passkey.");
          return;
        }
        window.location.assign(datos.redirect);
      } catch (error) {
        mostrarError(caja, textoDeError(error, "No se ha podido entrar con una passkey. Entra con tu usuario y contraseña."));
      } finally {
        botonEntrar.disabled = false;
      }
    });
  }

  const formularioAlta = document.getElementById("passkey-alta");
  if (formularioAlta) {
    const caja = document.getElementById("passkey-alta-error");
    if (!soportado()) {
      mostrarError(caja, "Este navegador no admite passkeys. Prueba con uno más reciente.");
      formularioAlta.hidden = true;
    }

    formularioAlta.addEventListener("submit", async (evento) => {
      evento.preventDefault();
      caja.hidden = true;
      const boton = formularioAlta.querySelector('button[type="submit"]');
      boton.disabled = true;
      try {
        const opciones = await enviar(formularioAlta.action, new FormData(formularioAlta));
        if (!opciones.ok) {
          const datos = await opciones.json().catch(() => ({}));
          mostrarError(caja, datos.error || "No se ha podido empezar el registro.");
          return;
        }
        const credencial = await navigator.credentials.create({
          publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(await opciones.json()),
        });
        const respuesta = await enviar(formularioAlta.dataset.urlAlta, {
          credentialJson: JSON.stringify(credencial.toJSON()),
          nombre: formularioAlta.elements.nombre.value,
        });
        if (!respuesta.ok) {
          const datos = await respuesta.json().catch(() => ({}));
          mostrarError(caja, datos.error || "No se ha podido registrar la passkey.");
          return;
        }
        window.location.reload();
      } catch (error) {
        mostrarError(caja, textoDeError(error, "No se ha podido crear la passkey en este dispositivo."));
      } finally {
        boton.disabled = false;
      }
    });
  }
})();
