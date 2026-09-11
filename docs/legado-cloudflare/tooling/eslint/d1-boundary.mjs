import { dirname, isAbsolute, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const canonical = (file) => relative(root, file).replaceAll("\\", "/").toLowerCase().replace(/\.(?:[cm]?[jt]sx?)$/u, "").replace(/\/index$/u, "");

// Excepciones por archivo y destino; ningún directorio de aplicación o tests queda abierto.
const owners = new Map([
  ["lib/authorization/request-context", ["lib/application/resident-baseline-service.ts", "tests/server-auth-d1-boundary.test.ts"]],
  ["db/repositories/authorized-d1", ["lib/authorization/request-context.ts"]],
  ["db/repositories/authorization-subject-repository", ["lib/authorization/request-context.ts", "db/repositories/authorized-d1.ts"]],
  ...["resident", "baseline", "audit"].map((name) => [
    `db/repositories/${name}-repository`, ["lib/authorization/request-context.ts", "tests/db-persistence.test.ts"],
  ]),
  ["lib/session/synthetic-session-provider", ["tests/server-auth-d1-boundary.test.ts"]],
]);

function localTarget(source, filename) {
  const specifier = source.replace(/[?#].*$/u, "");
  if (specifier.startsWith("file:")) return canonical(fileURLToPath(specifier));
  if (specifier.startsWith("@/")) return canonical(resolve(root, specifier.slice(2)));
  if (specifier.startsWith(".")) return canonical(resolve(dirname(filename), specifier));
  if (isAbsolute(specifier)) return canonical(resolve(specifier));
  // También deniega estos nombres sin alias, aunque no resolvieran en el bundler.
  if (/^(?:db|lib|tests)\//u.test(specifier)) return canonical(resolve(root, specifier));
  return null;
}

const d1Boundary = {
  meta: {
    type: "problem",
    schema: [],
    messages: {
      restricted: "Frontera D1: {{source}} no está permitido desde este módulo. Utiliza el servicio de aplicación.",
      dynamic: "Frontera D1: el destino de import/require debe ser un literal verificable; los cargadores glob no están permitidos.",
      reexport: "Frontera D1: no se pueden reexportar módulos internos protegidos.",
      typesOnly: "Frontera D1: el contrato público d1.ts solo puede declarar tipos e interfaces; no puede exponer ejecución.",
    },
  },
  create(context) {
    const importer = relative(root, context.filename).replaceAll("\\", "/");
    function check(node, source, reexport = false) {
      if (!source || source.type !== "Literal" || typeof source.value !== "string") {
        context.report({ node, messageId: "dynamic" });
        return;
      }
      const target = localTarget(source.value, context.filename);
      if (!target) return;
      const typeOnly = node.type === "ImportDeclaration" && (node.importKind === "type" ||
        (node.specifiers.length > 0 && node.specifiers.every((specifier) => specifier.importKind === "type")));
      const protectedModule = target === "db/client" ||
        (target.startsWith("db/repositories/") && !(target === "db/repositories/d1" && typeOnly)) ||
        target === "db/repositories" || target === "lib/authorization/request-context" ||
        target === "lib/session/synthetic-session-provider";
      // Un transporte tampoco puede utilizar una fixture/test como puente hacia D1.
      const testBridge = target.startsWith("tests/") && !importer.startsWith("tests/");
      if (protectedModule && reexport) context.report({ node, messageId: "reexport" });
      else if (testBridge || (protectedModule && !owners.get(target)?.includes(importer))) {
        context.report({ node, messageId: "restricted", data: { source: source.value } });
      }
    }
    return {
      Program(node) {
        if (importer !== "db/repositories/d1.ts") return;
        for (const statement of node.body) {
          const declaration = statement.type === "ExportNamedDeclaration" ? statement.declaration : statement;
          if (!declaration || !["TSInterfaceDeclaration", "TSTypeAliasDeclaration"].includes(declaration.type)) {
            context.report({ node: statement, messageId: "typesOnly" });
          }
        }
      },
      ImportDeclaration: (node) => check(node, node.source),
      ExportNamedDeclaration: (node) => { if (node.source) check(node, node.source, true); },
      ExportAllDeclaration: (node) => check(node, node.source, true),
      ImportExpression: (node) => check(node, node.source),
      TSImportType: (node) => check(node, node.argument),
      TSExternalModuleReference: (node) => check(node, node.expression),
      CallExpression(node) {
        const callee = node.callee;
        if (callee.type === "Identifier" && callee.name === "require") check(node, node.arguments[0]);
        if (callee.type === "MemberExpression") {
          const name = callee.computed ? callee.property.value : callee.property.name;
          if (name === "require" || (callee.object.type === "Identifier" && callee.object.name === "require" && name === "resolve")) {
            check(node, node.arguments[0]);
          }
          if (name === "glob" || name === "globEager") context.report({ node, messageId: "dynamic" });
        }
      },
    };
  },
};

export default d1Boundary;
