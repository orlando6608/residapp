import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTypeScript from "eslint-config-next/typescript";
import d1Boundary from "./tooling/eslint/d1-boundary.mjs";

export default defineConfig([
  ...nextVitals,
  ...nextTypeScript,
  {
    files: ["**/*.{js,jsx,mjs,cjs,ts,tsx,mts,cts}"],
    plugins: { architecture: { rules: { "d1-boundary": d1Boundary } } },
    linterOptions: { noInlineConfig: true },
    rules: { "architecture/d1-boundary": "error" },
  },
  globalIgnores([".next/**", ".wrangler/**", "dist/**", "coverage/**"]),
]);
