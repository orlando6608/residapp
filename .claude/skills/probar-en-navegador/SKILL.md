---
name: probar-en-navegador
description: Arranca ResidApp.Web en local contra la BD local (ACER-ORLANDO/ResidApp) y recorre sus pantallas en un Chrome real sin interfaz (Playwright), con una cuenta de prueba recién sembrada de cualquier perfil profesional; hace capturas en claro, oscuro y 375 px y comprueba desplazamiento lateral y errores de consola. Úsala para ver un cambio funcionando en la app de verdad, sobre todo si la pantalla usa JavaScript, que las pruebas funcionales por HTTP no ejecutan.
---

# Probar ResidApp en un navegador real

Las pruebas funcionales (`tests/FunctionalTests`) hablan con el servidor por HTTP y no ejecutan el JavaScript de las páginas. Esta skill
recorre las pantallas en Chrome como lo haría una persona. Así salieron dos fallos de `intercambio.js` que ninguna prueba veía.

Todo se hace en local, con datos ficticios. Nunca contra Azure ni con datos reales.

## Requisitos (ya cumplidos en el equipo de Orlando)

- SQL Server local `ACER-ORLANDO`, base `ResidApp`, con todos los scripts aplicados
  (`SQL_SERVER=ACER-ORLANDO SQL_DATABASE=ResidApp bash database/aplicar-scripts.sh`) y `sqlcmd` en el PATH.
- La cadena de conexión en los user-secrets de `src/ResidApp.Web` (`ConnectionStrings:ResidApp`).
- Node 20 o posterior y Google Chrome instalado: se usa `channel: "chrome"`, sin descargar navegadores de Playwright.
- `playwright-core` en la caché de npx. `comun.js` lo busca solo en `%LOCALAPPDATA%\npm-cache\_npx\*\node_modules\playwright-core`.
  Si no lo encuentra, ejecuta una vez `npx -y playwright-core@1.63.0 --version`, o pasa la ruta en `PLAYWRIGHT_CORE`.

## Pasos

Desde la raíz del repositorio, en Bash:

1. **Compilar con la app parada.** Si está en marcha, los DLL están bloqueados y la compilación falla:

   ```bash
   dotnet build src/ResidApp.Web -v q -nologo
   ```

2. **Arrancar la app en segundo plano** (`run_in_background`), con el perfil `http`. Con `--no-launch-profile` no carga los
   user-secrets y falla por falta de la cadena de conexión.

   ```bash
   dotnet run --project src/ResidApp.Web --launch-profile http --no-build > "<scratchpad>/app.log" 2>&1
   ```

   Está lista cuando `curl -s -o /dev/null -w '%{http_code}' http://localhost:5203/DevAuth/Login` devuelve `200`. Si no arranca,
   mira `app.log`.

3. **Sembrar una cuenta.** Cada llamada crea un centro nuevo «Residencia de prueba (navegador …)» con la unidad «Unidad Sur» y una
   cuenta con un solo perfil en él, e imprime su sujeto. Perfiles: `ADMINISTRACION` (por defecto), `AUXILIAR`, `ENFERMERIA`, `MEDICINA` y
   `DIRECCION_CLINICA`.

   ```bash
   sujeto=$(bash .claude/skills/probar-en-navegador/sembrar.sh ENFERMERIA)
   ```

   El centro no tiene residentes. Si la pantalla los necesita, créalos en el propio recorrido como lo haría el perfil, o importa
   `datos/residentes-erp.csv` con una cuenta de Administración del mismo centro.

4. **Recorrer.** `SALIDA` es la carpeta de las capturas y descargas: usa el scratchpad, nunca el repositorio.

   ```bash
   SUJETO="$sujeto" SALIDA="<scratchpad>/navegador" node .claude/skills/probar-en-navegador/recorrido-intercambio.js
   ```

   Imprime un informe en JSON y sale con código 1 si hay errores de consola o desplazamiento lateral. Si falla un paso, deja
   `fallo.png` en `SALIDA`.

5. **Mira las capturas** con Read. Un código de salida 0 no basta: comprueba que la pantalla muestra lo que debe.

6. **Para la app** al terminar (detén la tarea en segundo plano).

## Escribir un recorrido para otra pantalla

Copia `recorrido-intercambio.js` al scratchpad, o junto a él si merece quedarse, y usa `comun.js`:

- `abrirChrome()` y `contexto(browser, aspecto, opciones)`. Los aspectos son `claro`, `oscuro`, `movil` (375 px) y `movil-oscuro`.
- `entrar(ctx)`: abre una página, entra con `SUJETO` y apunta los errores de consola en `errores`.
- `captura(page, nombre)`, a página completa, y `desborde(page)`, en píxeles (0 = cabe en el ancho).

Lecciones de los recorridos anteriores:

- Los formularios que envían por `fetch` (por ejemplo `intercambio.js`) no navegan. Espera a un selector del resultado
  (`waitForSelector`), no a `waitForNavigation`.
- `change` de un campo de texto salta al pulsar el botón siguiente. Prueba siempre rellenar un campo de texto y pulsar el botón a
  continuación, como haría una persona.
- Con un solo perfil, `/DevAuth/Login` lleva directamente a Inicio. Con varios, hay que pasar por `/ProfileScope/Select`.
- Comprueba el foco tras cada paso (`document.activeElement?.id`): la accesibilidad (WCAG 2.2 AA) lo exige.

## Lo que queda en la BD local

Cada siembra deja un centro y una cuenta `prueba-navegador-<perfil>-<fecha>` en `ACER-ORLANDO/ResidApp`, y cada recorrido deja sus datos
y su auditoría. No se borran: la auditoría es inmutable y solo es la BD local. `sembrar.sh` se niega a ejecutarse contra
`*.database.windows.net`.
