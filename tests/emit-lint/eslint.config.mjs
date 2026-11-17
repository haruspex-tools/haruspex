// ESLint's recommended rules and typescript-eslint's strict and stylistic sets, as a project that adopts typescript-eslint's
// strictest rules without type information starts with, applied to the modules haruspex generates, with the two rules a
// team's own config commonly adds and the sets leave out: function properties rather than methods in a type, and a return
// type written on every function that isn't typed by where it is passed. Nothing is turned off: a generated file that a
// team's linter would reject is a defect (decision 0062).
import eslint from '@eslint/js'
import { defineConfig } from 'eslint/config'
import tseslint from 'typescript-eslint'

export default defineConfig(eslint.configs.recommended, tseslint.configs.strict, tseslint.configs.stylistic, {
  rules: {
    '@typescript-eslint/method-signature-style': 'error',
    '@typescript-eslint/explicit-function-return-type': 'error',
  },
})
