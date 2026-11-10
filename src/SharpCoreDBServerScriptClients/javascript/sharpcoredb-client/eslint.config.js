// ESLint 10 reads flat config only.
//
// This project previously had no ESLint configuration at all, so `npm run lint` failed with
// "ESLint couldn't find a configuration file" and the script could not run. This starts the gate
// at the parser-only baseline - TypeScript sources are parsed, no rules are enabled - so the
// script executes and can be tightened deliberately later. Enabling a rule set here would fail
// today's sources (they use `any` deliberately at the protocol boundary), and choosing that rule
// set is a policy decision rather than a dependency bump.
const tsParser = require('@typescript-eslint/parser');

module.exports = [
  {
    files: ['src/**/*.ts'],
    languageOptions: {
      parser: tsParser,
      ecmaVersion: 2022,
      sourceType: 'module',
    },
  },
];
