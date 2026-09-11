# Autorización server-side

`policy.ts` contiene contratos puros y comprobables para el primer bloque Residente/Basal. Combina:

- autenticación y cuenta activa;
- perfil activo incluido entre los perfiles asignados;
- grants relacionales que ligan un único perfil, centro, unidad, residentes y permisos;
- autorización familiar activa cuando se consulta la identidad vinculada;
- permisos expresos para alta excepcional, completar/reevaluar basal y detalle clínico de Dirección;
- finalidad explícita y una obligación estructurada de auditoría para las lecturas clínicas de Dirección.

La política acepta entradas runtime como desconocidas, valida perfiles, acciones e identificadores y mantiene denegación por defecto. No forma productos cartesianos entre ámbitos ni combina capacidades de perfiles diferentes. Los módulos de servidor importan desde `server.ts`. El cálculo continúa siendo puro; tanto el motor como su entrada llevan `server-only`. Las pruebas Node usan la condición estándar `react-server`.

`request-context.ts` conecta el puerto de sesión con la cuenta, el perfil seleccionado y los ámbitos persistidos. Tras evaluar la política devuelve un token opaco, vacío e inmutable. Un `WeakMap` privado conserva acción, perfil, ámbito, recurso y D1. Solo los tres ejecutores específicos pueden consumirlo: alta, firma del borrador autorizado y lectura auditada del recurso/finalidad autorizados. Comprueban la clase de operación también en runtime. No existe un getter D1 ni se admiten SQL, callbacks, contextos fabricados o deserializados. La revalidación de hechos permanece dentro de cada batch.

La regla `architecture/d1-boundary`, obligatoria en `pnpm lint` y `pnpm check`, deniega imports de estos internos desde cualquier archivo salvo las excepciones nominales documentadas. El servicio solo importa el ejecutor; no puede importar repositorios. Tampoco se permite reexportar internos, usar tests como puente ni desactivar la regla con comentarios. `server-only` mantiene por separado la protección cliente.

El proveedor definitivo, las rutas y las pantallas siguen pendientes. La composición por defecto deniega por ausencia de identidad. El proveedor sintético separado exige activación explícita desde el proceso y solo funciona en test o desarrollo local explícito. No lee cookies, cabeceras ni claims del cliente.

Arquitectura, decisiones y matriz de pruebas: [frontera servidor–autorización–D1](../../docs/architecture/0004-frontera-servidor-autorizacion-d1.md).

Las decisiones de denegación son internas; una futura interfaz pública no debe revelar la existencia de cuentas o recursos. Esta política aislada no demuestra que una integración futura sea segura ni apta para producción.
