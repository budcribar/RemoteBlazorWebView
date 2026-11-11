/*
 * For a detailed explanation regarding each configuration property, visit:
 * https://jestjs.io/docs/configuration
 */

const path = require('path');

const ROOT_DIR = path.resolve(__dirname);

const reporters = ['default'];
try {
  const jestJunit = require.resolve('jest-junit');
  reporters.push([
    jestJunit,
    {
      outputDirectory: path.resolve(ROOT_DIR, 'artifacts', 'log'),
      outputName: `${process.platform}.components-webjs.junit.xml`,
    },
  ]);
} catch {
  // jest-junit is optional when running tests locally without CI reporters.
}

/** @type {import('jest').Config} */

module.exports = {
  roots: ['<rootDir>/src', '<rootDir>/test'],
  testMatch: ['**/*.test.ts'],
  moduleFileExtensions: ['js', 'ts'],
  transform: {
    '^.+\\.(js|ts)$': 'babel-jest',
  },
  moduleDirectories: ['node_modules', 'src'],
  testEnvironment: 'jsdom',
  reporters,
};
