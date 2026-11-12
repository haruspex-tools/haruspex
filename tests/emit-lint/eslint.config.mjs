// The recommended rules of ESLint and of typescript-eslint, as a project that adopts both starts with, applied to the
// modules haruspex generates. Nothing is turned off: a generated file that a team's linter would reject is a defect.
import eslint from '@eslint/js'
import { defineConfig } from 'eslint/config'
import tseslint from 'typescript-eslint'

export default defineConfig(eslint.configs.recommended, tseslint.configs.recommended)
