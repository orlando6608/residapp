# Wireframe funcional - Auxiliar

**Versión:** 0.2  
**Fecha de canonicalización:** 2026-09-05  
**Estado:** Cerrado; coherencia comprobada para línea base  
**Fuente canónica:** Markdown  
**Equivale funcionalmente a:** Wireframe funcional Auxiliar v0.2 cerrado  
**Referencias:** PRD v0.4 y matriz de permisos v0.2.

## 1. Alcance

Conversión a Markdown sin rediseño. Se mantienen las decisiones, códigos y flujo del PDF cerrado. Se explicita únicamente que la consulta basal es del basal vigente, resumido y de solo lectura para residentes asignados.

## 2. Reglas transversales

- Perfil Auxiliar activo, turno/asignación vigente, ámbito y permiso validados en servidor.
- `Sin cambios`, `No valorable` y `Registrar cambio` son excluyentes.
- La clasificación ordinaria/prioritaria organiza la bandeja y no diagnostica.
- Autor, perfil y fecha/hora de registro proceden del servidor.

## 3. Pantallas

### AUX-01. Pantalla principal - Mis residentes

- **Muestra:** residentes asignados y estado del cierre cotidiano.
- **Acciones:** abrir residente o registrar cambio/evento desde Inicio.

### AUX-02. Registro cotidiano del residente

- **Muestra:** identidad mínima, basal vigente resumido y acciones del turno.
- **Acciones:** Sin cambios, No valorable o Registrar cambio.

### AUX-03. Consulta del estado basal

- **Muestra:** las nueve áreas y el total de Barthel del basal vigente necesarios para el cuidado cotidiano.
- **Reglas:** solo residentes asignados; sin respuestas detalladas del Barthel, versiones históricas, borradores, aportaciones, firma, corrección ni rectificación.

### AUX-04. Acción «Sin cambios»

- **Acción:** confirmar y firmar el cierre cotidiano.
- **Resultado:** cierre sin tarea para Enfermería.

### AUX-05. Acción «No valorable»

- **Campo obligatorio:** motivo.
- **Regla:** nunca equivale a estabilidad clínica.

### AUX-06. Registrar cambio - selección de áreas

- **Acción:** seleccionar una o varias áreas.
- **Regla:** exige al menos una.

### AUX-07. Contenido de las diez áreas

- **Áreas:** alimentación/hidratación; movilidad/funcionalidad; ánimo/conducta; dolor/malestar; heces/diuresis; sueño; lesiones en piel; participación/relación social; incidencias/caídas; estado de conciencia.
- **Regla:** se conservan opciones rápidas del PDF; se utiliza “Atragantamiento”; texto opcional o libre según área.

### AUX-08. Temperatura opcional

- **Campo:** temperatura opcional con unidad definida.
- **Regla:** no bloquea el registro si no procede.

### AUX-09. Registro temporal

- **Muestra:** fecha/hora automática.
- **Regla:** no se exige hora exacta de observación; “Desde cuándo” se mantiene como punto de validación de usabilidad donde aplique.

### AUX-10. Clasificación inicial del cambio

- **Opciones:** ordinario o prioritario.
- **Prioritarios iniciales:** alteración de conciencia/estado general, caída/lesión/traumatismo, fiebre/sospecha de infección, dolor nuevo/intenso, dificultad respiratoria y déficit neurológico nuevo.

### AUX-11A. Confirmación de cambio ordinario

- **Muestra:** resumen antes de firma.
- **Resultado:** envío a bandeja ordinaria de Enfermería con autoría conservada.

### AUX-11B. Evento prioritario - aviso directo

- **Muestra:** recordatorio de aplicar el protocolo del centro.
- **Acción:** documentar el aviso directo definido para el prototipo.
- **Regla:** persona/canal/hora del aviso permanece como punto de validación sin bloquear el prototipo.

### AUX-12. Confirmación del evento prioritario

- **Muestra:** resumen del cambio y aviso.
- **Resultado:** envío a bandeja prioritaria de Enfermería; firma idempotente.

### AUX-13. Registrar cambio o evento desde Inicio

- **Acción:** seleccionar residente asignado y reutilizar `AUX-06` a `AUX-12`.
- **Regla:** no amplía ámbito por búsqueda o manipulación de identificadores.

## 4. Estados mínimos

- Pendiente, cerrado, no valorable y cambio enviado.
- Cargando, vacío real, error técnico, sin asignación y acceso denegado se distinguen.
- Ante doble pulsación se crea un único registro.

## 5. Criterios de aceptación

1. AUX-03 nunca muestra versiones basales ni acciones de edición.
2. Las tres acciones de cierre cotidiano no pueden combinarse.
3. No valorable no puede guardarse sin motivo.
4. Registrar cambio no puede guardarse sin área.
5. Un prioritario exige la confirmación del aviso directo prevista para el prototipo.

## 6. Comprobación de coherencia

No se detecta cambio material respecto al PDF v0.2 cerrado. Esta versión Markdown aclara los límites basales y de autorización sin alterar el flujo.
