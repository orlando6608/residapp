import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";

const functionalBaselineSources = [
  "docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md",
  "docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md",
  "docs/product/wireframes/2026-09-05-wireframe-funcional-auxiliar-v0.2.md",
  "docs/product/wireframes/2026-09-06-wireframe-funcional-enfermeria-v0.3.md",
  "docs/product/wireframes/2026-09-06-wireframe-funcional-medicina-v0.3.md",
  "docs/product/wireframes/2026-09-05-wireframe-funcional-portal-familiar-v0.2.md",
  "docs/product/wireframes/2026-09-06-wireframe-funcional-administracion-v0.3.md",
  "docs/product/wireframes/2026-09-05-wireframe-funcional-direccion-coordinacion-clinica-v0.1.md",
  "docs/product/traceability/2026-09-06-matriz-trazabilidad-funcional-v0.2.md",
] as const;

const governanceSources = [
  "AGENTS.md",
  "docs/product/baselines/2026-09-06-declaracion-linea-base-funcional-v1.1.md",
  "docs/product/decisions/2026-09-06-cierre-d1-p01-d1-p08-v1.0.md",
] as const;

const historicalSources = [
  "docs/product/baselines/2026-09-05-declaracion-linea-base-funcional-v1.0.md",
  "docs/product/2026-09-05-PRD-plataforma-contacto-familias-v0.4-consolidado.md",
  "docs/product/permissions/2026-09-05-matriz-permisos-seis-perfiles-v0.2.md",
  "docs/2026-09-02_PRD_Plataforma_contacto_familias_v0.3_consolidado.pdf",
  "docs/2026-09-02_Matriz_permisos_seis_perfiles_v0.1.pdf",
  "docs/2026-09-02_Wireframe_funcional_Auxiliar_v0.2_cerrado.pdf",
  "docs/Wireframe_funcional_Enfermeria_v0.1.docx",
  "docs/Wireframe_funcional_Medicina_v0.1_2026-08-23.pdf",
  "docs/2026-08-27_Wireframe_funcional_Portal_Familiar_v0.1.pdf",
  "docs/2026-08-29_Wireframe_funcional_Administracion_v0.1.pdf",
  "docs/2026-09-01_Wireframe_funcional_Direccion_Coordinacion_Clinica_v0.1.pdf",
] as const;

async function assertSourcesExist(sources: readonly string[]) {
  await Promise.all(sources.map((source) =>
    assert.doesNotReject(access(source), `No se encuentra la fuente documental: ${source}`),
  ));
}

test("los nueve artefactos de la línea base v1.1 permanecen disponibles", async () => {
  await assertSourcesExist(functionalBaselineSources);
});

test("la declaración, decisión e instrucciones vigentes permanecen disponibles", async () => {
  await assertSourcesExist(governanceSources);
});

test("la línea base v1 y los documentos sustituidos permanecen como evidencia histórica", async () => {
  await assertSourcesExist(historicalSources);
});

test("los puntos de entrada declaran la línea base vigente", async () => {
  const entryPoints = await Promise.all(
    ["README.md", "docs/README.md", "AGENTS.md"].map((path) => readFile(path, "utf8")),
  );

  for (const content of entryPoints) {
    assert.match(content, /LBF-CONNECT-2026-09-06-V1\.1/);
    assert.match(content, /PRD v0\.5/);
    assert.match(content, /matriz(?: de permisos)?(?: de seis perfiles)? v0\.2\.1/i);
    assert.doesNotMatch(content, /PRD v0\.4 vigente/i);
  }
});

test("las cuatro reglas aprobadas son trazables", async () => {
  const [decision, prd, contract, trace] = await Promise.all([
    readFile("docs/product/decisions/2026-09-06-cierre-d1-p01-d1-p08-v1.0.md", "utf8"),
    readFile("docs/product/2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md", "utf8"),
    readFile("docs/architecture/0002-contrato-datos-residente-basal.md", "utf8"),
    readFile("docs/product/traceability/2026-09-06-matriz-trazabilidad-funcional-v0.2.md", "utf8"),
  ]);

  for (const content of [decision, prd, contract, trace]) {
    assert.match(content, /male/);
    assert.match(content, /female/);
    assert.match(content, /other/);
    assert.match(content, /unknown/);
    assert.match(content, /NINGUNA/);
    assert.match(content, /NO_DOCUMENTADO/);
    assert.match(content, /ENTERAL/i);
    assert.match(content, /OTRO/);
  }
});

test("el ADR registra 0001 integrada, validada localmente e inmutable", async () => {
  const adr = await readFile("docs/architecture/0003-aprobacion-d1-drizzle.md", "utf8");

  assert.match(adr, /PR #1/);
  assert.match(adr, /cc7e028b8fb4aa5ed881df9d3f8c57a773e541c9/);
  assert.match(adr, /39B05B14AB2E9A31C2396A2D0665A36207D735C4921FD752B97C8C408AFA2EE8/);
  assert.match(adr, /29 tablas, 56 índices y 56 triggers/);
  assert.match(adr, /28\/28 pruebas de persistencia/);
  assert.match(adr, /138\/138 pruebas generales/);
  assert.match(adr, /D1 local desechable/i);
  assert.match(adr, /`0002` o una migración posterior/i);
  assert.match(adr, /aplicación remota.*prohibida/i);
  assert.match(adr, /D1-P04/);
  assert.match(adr, /D1-P05/);
  assert.match(adr, /D1-P06/);
  assert.match(adr, /datos exclusivamente ficticios/i);
});
