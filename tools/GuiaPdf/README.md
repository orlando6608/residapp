# Generador de PDF de la Guía por perfil

Herramienta de desarrollo, **fuera de la solución y de la aplicación** (como `PruebaDictado`). Convierte la Guía (`/Guia`) en PDF con
Chrome. Sale un PDF por perfil, otro de «Para todos» y otro con la guía completa (`/Guia/todo`). Cada página lleva «Página X de Y» y
cada índice de tareas lleva el número de página de cada ficha, para buscar en papel por el código de la ficha (`ADM-17`) y llegar a su página.

En lenguaje llano: *Playwright* es una librería que maneja un navegador desde un guion; aquí abre la Guía en Chrome y le pide «imprimir
como PDF». *Headless* es abrir el navegador sin ventana.

## Uso

Necesitas Node 20 o posterior y Google Chrome instalado (se usa el que ya tienes, sin descargar navegadores).

```powershell
# 1. La web en marcha (otra terminal), con la base local
dotnet run --project src/ResidApp.Web --launch-profile http

# 2. Una sola vez: instalar las dos dependencias de la herramienta
cd tools/GuiaPdf
npm install

# 3. Generar los PDF en tools/GuiaPdf/salida/
node generar.mjs
```

Opciones: `--base http://localhost:5203` (otra dirección, por ejemplo Azure dev), `--salida carpeta` y `--solo plataforma,todo`
(solo esos; `todo` es la guía completa).

Tarda medio minuto. Al terminar dice cuántas páginas y fichas tiene cada PDF y **sale con error (código 1) si alguna ficha
empieza en una página y acaba en otra**: la regla de la Guía es que ninguna ficha se parte.

## Cómo calcula los números de página

Los números de página de un índice no se pueden saber antes de imprimir. El guion renderiza el PDF, lee su texto con `pdfjs-dist`,
mide en qué página cae el título de cada ficha, escribe esos números en el índice y vuelve a renderizar, hasta que no cambian (como
mucho cuatro pasadas). En la guía completa hace lo mismo con el «Contenido» de la portada, usando marcadores invisibles que no quedan en el
PDF final.

## Los PDF no se suben a git

La carpeta `salida/` está ignorada: cada PDF sale de la versión de la Guía que sirve la web en ese momento, así nunca hay uno viejo en el
repositorio. Si hay que repartir papel, imprime o envía el PDF recién generado.

## Lo que no hace

- No mira el estado de revisión: el pie de cada página dice «Pendiente de revisión de punta a punta» mientras la Guía no esté sellada (`revisadoPara`).
- No comprueba la impresión en papel ni en blanco y negro: eso lo hace una persona con una impresión de prueba.
