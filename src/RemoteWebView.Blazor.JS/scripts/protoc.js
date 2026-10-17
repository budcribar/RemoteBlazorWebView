const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..');
const isWin = process.platform === 'win32';

function firstExisting(candidates) {
    for (const candidate of candidates) {
        if (candidate && fs.existsSync(candidate))
            return candidate;
    }
    return null;
}

function protocOnPath() {
    const entries = (process.env.PATH || '').split(path.delimiter);
    for (const entry of entries) {
        if (!entry || entry.includes(`${path.sep}node_modules${path.sep}`))
            continue;
        const candidate = path.join(entry, isWin ? 'protoc.exe' : 'protoc');
        if (fs.existsSync(candidate))
            return candidate;
    }
    return null;
}

// The npm "protoc" package shim has a CRLF shebang and shadows a real compiler.
const protoc = firstExisting([
    isWin ? path.join(root, 'protoc', 'bin', 'protoc.exe') : null,
    path.join(root, 'protoc', 'bin', 'protoc'),
    protocOnPath(),
]) || 'protoc';

const tsPlugin = firstExisting([
    path.join(root, 'node_modules', 'ts-protoc-gen', 'bin', 'protoc-gen-ts'),
    path.join(root, 'node_modules', '.bin', isWin ? 'protoc-gen-ts.cmd' : 'protoc-gen-ts'),
]);
const grpcPlugin = firstExisting([
    path.join(root, 'node_modules', 'protoc-gen-grpc-web', 'bin', isWin ? 'protoc-gen-grpc-web.exe' : 'protoc-gen-grpc-web'),
    path.join(root, 'node_modules', 'protoc-gen-grpc-web', 'bin', 'protoc-gen-grpc-web'),
    path.join(root, 'node_modules', 'protoc-gen-grpc-web', 'bin', 'protoc-gen-grpc-web.exe'),
]);

if (!tsPlugin || !grpcPlugin) {
    console.error('protoc plugins were not found. Run yarn install in src/RemoteWebView.Blazor.JS.');
    process.exit(1);
}

const generated = path.join(root, 'src', 'generated');
fs.mkdirSync(path.join(generated, 'ts'), { recursive: true });

const args = [
    `--plugin=protoc-gen-ts=${tsPlugin}`,
    `--plugin=protoc-gen-grpc-web=${grpcPlugin}`,
    `--js_out=import_style=commonjs,binary:${generated}`,
    `--grpc-web_out=import_style=typescript,mode=grpcwebtext:${generated}`,
    `--ts_out=${path.join(generated, 'ts')}`,
    `-I${path.join(root, '..', 'Protos')}`,
    `-I${path.join(root, 'protoc', 'include')}`,
    path.join(root, '..', 'Protos', 'webview.proto'),
];

const result = spawnSync(protoc, args, { stdio: 'inherit', cwd: root, shell: false });
if (result.error) {
    console.error(result.error.message);
    process.exit(1);
}
process.exit(result.status ?? 1);
