# Directrices PWA y Mobile-First para ResidApp.Web

## 1. Contexto

ResidApp se opera como una **PWA instalable**, no como una aplicación web de escritorio tradicional:
auxiliares, enfermería y medicina la usan en tablets de planta, y las familias en su móvil. Esto no es
una decisión nueva — `docs/producto/alcance.md` ya fija "la primera versión es web adaptable/PWA", y el
propio scaffold inicial del repositorio incluye `wwwroot/manifest.json` y `wwwroot/sw.js` (vacíos,
versionados desde el primer commit) anticipando este trabajo. Este documento fija cómo se traduce esa
necesidad funcional — que médicos, enfermería y familias puedan trabajar desde sus tablets y teléfonos —
a decisiones concretas de ingeniería en `ResidApp.Web`, sin reabrir ni ampliar `docs/producto/alcance.md`.

## 2. Vistas Razor mobile-first

Toda vista nueva bajo `src/ResidApp.Web/Views/`:

- Usa Bootstrap 5 (ya presente en `wwwroot/lib/bootstrap`) con clases responsivas; nada de layouts fijos
  de escritorio sin alternativa en pantalla estrecha.
- Controles interactivos con área táctil amplia (`btn-lg`, padding suficiente) — el usuario final opera
  con el dedo, no con precisión de ratón.
- Formularios con el `type`/`inputmode` que active el teclado correcto en móvil (`type="date"`,
  `type="tel"`, `type="number"`, etc.), no siempre `type="text"`.
- Tablas de datos densas se muestran como listas de tarjetas (`.card`) en viewports estrechos (por
  ejemplo `d-none d-md-table-row` / `d-md-none`), no solo con scroll horizontal.

Las vistas ya construidas (`Residents/Create`, `Baseline/Sign`, `Baseline/Direction`, `DevAuth/Login`)
ya aplican esta directriz: controles `-lg`, `type`/`inputmode` correctos, botones en `d-grid d-md-block`
(ancho completo en móvil, ancho natural en pantallas medianas+). `Baseline/DirectionResult` es la única
vista con datos tabulares y ya alterna tabla (`d-none d-md-block`) / tarjetas (`d-md-none`) según
viewport.

## 3. Identidad PWA: manifest y service worker

- `src/ResidApp.Web/wwwroot/manifest.json` y `src/ResidApp.Web/wwwroot/sw.js` son los únicos archivos de
  identidad PWA de la aplicación. Cualquier cambio de nombre, iconos, `start_url` o `scope` pasa por
  este documento.
- `Views/Shared/_Layout.cshtml` debe enlazar el manifest (`<link rel="manifest" href="~/manifest.json">`)
  y registrar el service worker (`navigator.serviceWorker.register('/sw.js')`).
- El service worker cachea el shell de la aplicación (assets estáticos) y sirve `/Home/Offline` como
  fallback de navegación cuando no hay red. **No** cachea ni reintenta envíos de formularios clínicos —
  eso es responsabilidad de la idempotencia del punto 4, no del service worker.

Estado actual: hecho. `manifest.json` (nombre, iconos placeholder en `wwwroot/icons/icon.svg`,
`display: standalone`) y `sw.js` (shell cacheado, fallback de navegación a `/Home/Offline`, ignora todo
lo que no sea `GET`) están enlazados desde `_Layout.cshtml`. Los iconos son un placeholder genérico
(cuadrado azul con una cruz) — la marca comercial definitiva sigue sin decidir (ver `README.md`).

## 4. Idempotencia como defensa ante pérdida de cobertura

Un auxiliar en una zona con mala cobertura puede reenviar un formulario sin darse cuenta (doble toque,
timeout del móvil, reintento manual al recuperar señal). Esto ya está resuelto en el vertical
Residente/Basal y se formaliza aquí como regla general:

- Toda acción de escritura expuesta a un controlador MVC que use Dapper exige y valida un `operationId`,
  siguiendo el patrón ya construido en `CreateResident`/`SignBaseline`
  (`ResidApp.Application.UseCases`, tabla `dbo.idempotency_operations`). También cubre
  `ReadDirectionBaseline`: aunque es una lectura, escribe en `audit_events`, y sin idempotencia un
  reintento duplicaba la fila de auditoría — corregido añadiendo `OperationId` a
  `ClinicalDirectionReadInput` y envolviendo `SqlBaselineRepository.ReadAsClinicalDirectionAsync` con el
  mismo patrón.
- Un reenvío con el mismo `operationId` debe ser un no-op seguro (devuelve el resultado ya obtenido, no
  duplica el registro clínico) — ver `SqlResidentRepositoryTests`/`SqlBaselineRepositoryTests` en
  `tests/IntegrationTests/` para el patrón de test esperado.
- **Límite explícito:** esto es resiliencia de reintento sobre un envío que sí llegó a completarse (o
  que se reintenta antes de tener éxito), no una cola de trabajo offline. No habilita capturar ni
  almacenar datos clínicos mientras el dispositivo está desconectado.

## 5. Página de fallback offline — no "modo de trabajo offline"

`docs/producto/alcance.md` mantiene explícitamente **"Modo clínico offline con sincronización
posterior" fuera de alcance**, y los principios invariantes de `docs/producto/objetivos.md`
("Autorización en servidor", "Denegación por defecto") exigen validar cada actuación en el servidor —
algo que no es posible sin conexión. Por tanto:

- `/Home/Offline` (`HomeController.Offline()` + `Views/Home/Offline.cshtml`) es una pantalla
  **puramente informativa**: avisa de la pérdida de cobertura, confirma que no se ha perdido nada porque
  el envío no llegó a completarse, e invita a reintentar cuando vuelva la señal.
- No incluye formularios, no persiste datos localmente (IndexedDB, localStorage, etc.) para sincronizar
  después, y no encola escrituras clínicas.
- Si en el futuro se quisiera construir una cola local con sincronización posterior, es una ampliación
  de alcance de producto que corresponde decidir a CJ actualizando `docs/producto/alcance.md` primero —
  no una consecuencia automática de esta directriz ni una decisión de ingeniería unilateral.

## 6. Verificación

- Toda vista nueva se revisa en un viewport móvil (DevTools ≤ 480px) antes de darse por terminada.
- `manifest.json` es JSON válido y `_Layout.cshtml` lo referencia; el service worker se registra sin
  errores en la consola del navegador.
- Test manual/automatizado de "doble envío": enviar el mismo formulario dos veces con idéntico
  `operationId` no debe crear un segundo registro en SQL Server.
