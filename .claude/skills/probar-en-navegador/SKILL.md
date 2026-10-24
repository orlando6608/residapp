---
name: probar-en-navegador
description: Arranca ResidApp.Web en local contra la BD local (ACER-ORLANDO/ResidApp) y recorre sus pantallas en un Chrome real sin interfaz (Playwright), entrando como una persona de verdad (invitación, contraseña y código del móvil) con una cuenta de prueba recién sembrada de cualquier perfil profesional o con las cuentas de los centros de prueba; hace capturas en claro, oscuro y 375 px y comprueba desplazamiento lateral y errores de consola. También da de alta un juego de cuentas de prueba y prepara la Residencia Los Almendros para las guías de CJ, en local o en Azure dev. Úsala para ver un cambio funcionando en la app de verdad, sobre todo si la pantalla usa JavaScript, que las pruebas funcionales por HTTP no ejecutan.
---

# Probar ResidApp en un navegador real

Las pruebas funcionales (`tests/FunctionalTests`) hablan con el servidor por HTTP y no ejecutan el JavaScript de las páginas. Esta skill
recorre las pantallas en Chrome como lo haría una persona. Así salieron dos fallos de `intercambio.js` que ninguna prueba veía.

Se entra siempre como en un centro real: no hay acceso de desarrollo (se retiró el 2026-10-24). La primera vez, cada cuenta usa su
invitación, elige contraseña y prepara el código del móvil; `comun.js` lee la clave del código en la pantalla del alta y calcula los
códigos. Las credenciales que elige se guardan **fuera del repositorio**, en `~/.residapp/credenciales-<servidor>.json` (una por entorno;
otro fichero con `CREDENCIALES`), y con ellas vuelve a entrar las siguientes veces. Nunca las copies al repositorio, a un documento ni a
una captura.

## Requisitos (ya cumplidos en el equipo de Orlando)

- SQL Server local `ACER-ORLANDO`, base `ResidApp`, con todos los scripts y la semilla aplicados
  (`SQL_SERVER=ACER-ORLANDO SQL_DATABASE=ResidApp APLICAR_SEED=1 bash database/aplicar-scripts.sh`) y `sqlcmd` en el PATH. Las pruebas
  automáticas usan otra base, `ResidAppPruebas`, sin semilla.
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
   user-secrets y falla por falta de la cadena de conexión. Para otra base, pon `ConnectionStrings__ResidApp=…` delante.

   ```bash
   dotnet run --project src/ResidApp.Web --launch-profile http --no-build > "<scratchpad>/app.log" 2>&1
   ```

   Está lista cuando `curl -s -o /dev/null -w '%{http_code}' http://localhost:5203/Acceso/Entrar` devuelve `200`. Si no arranca,
   mira `app.log`.

3. **Sembrar una cuenta.** Cada llamada crea un centro nuevo «Residencia de prueba (navegador …)» con la unidad «Unidad Sur» y una
   cuenta con un solo perfil en él, le emite su invitación con `database/operaciones/invitar_cuenta.sql` e imprime dos líneas: el usuario y
   el enlace de la invitación. Perfiles: `ADMINISTRACION` (por defecto), `AUXILIAR`, `ENFERMERIA`, `MEDICINA` y `DIRECCION_CLINICA`.

   ```bash
   mapfile -t cuenta < <(bash .claude/skills/probar-en-navegador/sembrar.sh ENFERMERIA)
   ```

   El centro no tiene residentes. Si la pantalla los necesita, créalos en el propio recorrido como lo haría el perfil, o importa
   `datos/residentes-erp.csv` con una cuenta de Administración del mismo centro.

   Para mirar sin sembrar nada, usa el juego de cuentas de Orlando de los centros de prueba (`almendros-orlando-enfermeria`, etc.;
   `database/seed/centros_prueba.sql`), dado de alta con `alta-juego.js` (más abajo). Es un centro compartido: lo que hagas en él se queda.
   El juego sin `-orlando` es de CJ: no lo uses.

4. **Recorrer.** `SALIDA` es la carpeta de las capturas y descargas: usa el scratchpad, nunca el repositorio.

   ```bash
   SUJETO="${cuenta[0]}" ENLACE="${cuenta[1]}" SALIDA="<scratchpad>/navegador" node .claude/skills/probar-en-navegador/recorrido-intercambio.js
   ```

   Con una cuenta ya dada de alta basta `SUJETO`. Imprime un informe en JSON y sale con código 1 si hay errores de consola o
   desplazamiento lateral. Si falla un paso, deja `fallo.png` en `SALIDA`.

5. **Mira las capturas** con Read. Un código de salida 0 no basta: comprueba que la pantalla muestra lo que debe.

6. **Para la app** al terminar (detén la tarea en segundo plano).

## Dar de alta un juego de cuentas y preparar Los Almendros

Tras recrear una base (local o Azure dev) con la semilla, las cuentas existen sin contraseña. Para el juego de Orlando (nunca el de CJ, que
da de alta él mismo con las hojas que le entrega Orlando):

1. La invitación de `plataforma-orlando`, con el administrador de esa base (en Azure dev, el administrador de Azure SQL):

   ```bash
   sqlcmd -S <servidor> -d <base> <autenticación> -b -I -f 65001 -v CUENTA="plataforma-orlando" -i database/operaciones/invitar_cuenta.sql
   ```

   Imprime la ruta `/Acceso/Invitacion/…`: ponle delante la dirección de la app. El `sqlcmd` de Windows no admite `:` ni `/` en los
   valores de `-v` y no abre rutas absolutas de Git Bash: ejecútalo desde la raíz del repositorio con la ruta relativa.

2. `alta-juego.js` hace el resto por el camino real: Plataforma entra con esa invitación, invita a las Administraciones de Los Almendros y
   Los Olmos y a `varios-centros-orlando`, y cada Administración invita a su personal y a su familiar. Se salta las cuentas que ya tienen
   credenciales.

   ```bash
   JUEGO=orlando ENLACE="<dirección><ruta>" BASE_URL="<dirección>" node .claude/skills/probar-en-navegador/alta-juego.js
   ```

3. `preparar-almendros.js` deja hecho por la aplicación lo que no se puede sembrar y esperan las guías de CJ: el basal firmado de Antonio y
   de Pilar, la foto de Manuel y el historial de Pilar (un evento cerrado y una derivación a Urgencias con su informe firmado). Comprueba
   antes qué está hecho y no lo repite.

   ```bash
   JUEGO=orlando BASE_URL="<dirección>" SALIDA="<scratchpad>/preparar" node .claude/skills/probar-en-navegador/preparar-almendros.js
   ```

Para que Orlando entre a mano con esas cuentas, la clave del código del móvil de cada una está en el fichero de credenciales: se añade a su
aplicación de autenticación escribiéndola. Las cuentas de Plataforma entran solo con passkey en cuanto tienen una: estos guiones no se la
registran.

## Escribir un recorrido para otra pantalla

Copia `recorrido-intercambio.js` al scratchpad, o junto a él si merece quedarse, y usa `comun.js`:

- `abrirChrome()` y `contexto(browser, aspecto, opciones)`. Los aspectos son `claro`, `oscuro`, `movil` (375 px) y `movil-oscuro`.
- `entrar(ctx, sujeto, enlace)`: abre una página, entra con la cuenta (por defecto `SUJETO` y `ENLACE`) y apunta los errores de consola en
  `errores`. Sin credenciales guardadas y con enlace, la da de alta (`darDeAlta`).
- `captura(page, nombre)`, a página completa, y `desborde(page)`, en píxeles (0 = cabe en el ancho).

Lecciones de los recorridos anteriores:

- Los formularios que envían por `fetch` (por ejemplo `intercambio.js`) no navegan. Espera a un selector del resultado
  (`waitForSelector`), no a `waitForNavigation`.
- `change` de un campo de texto salta al pulsar el botón siguiente. Prueba siempre rellenar un campo de texto y pulsar el botón a
  continuación, como haría una persona.
- Con un solo perfil, entrar lleva directamente a Inicio. Con varios, hay que pasar por `/ProfileScope/Select`.
- El mismo código del móvil no se escribe dos veces seguidas: `comun.js` espera al siguiente (cambia cada 30 segundos), así que entrar
  varias veces con la misma cuenta puede tardar.
- Sin sesión, toda pantalla salvo las públicas redirige al login, y la sesión caduca a los 30 minutos sin uso: un recorrido largo con
  pausas tiene que volver a entrar.
- Comprueba el foco tras cada paso (`document.activeElement?.id`): la accesibilidad (WCAG 2.2 AA) lo exige.

## Lo que queda en la BD

Cada siembra deja un centro y una cuenta `prueba-navegador-<perfil>-<fecha>` en `ACER-ORLANDO/ResidApp`, y cada recorrido deja sus datos
y su auditoría. No se borran: la auditoría es inmutable. `sembrar.sh` se niega a ejecutarse contra `*.database.windows.net`.
