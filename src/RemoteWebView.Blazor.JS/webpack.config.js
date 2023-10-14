const path = require('path');
const webpack = require('webpack');
const TerserJsPlugin = require("terser-webpack-plugin");
const { DuplicatesPlugin } = require("inspectpack/plugin");

module.exports = (env, args) => ({
    resolve: {
        extensions: ['.ts', '.js'],
    },
    devtool: false,
    module: {
        rules: [{ test: /\.ts?$/, loader: 'ts-loader' }]
    },
    entry: {
        'remote.blazor.desktop': './src/Boot.Desktop.ts'
    },
    output: { path: path.join(__dirname, '/dist'), filename: '[name].js' },
    optimization: {
        sideEffects: true,
        concatenateModules: true,
        providedExports: true,
        usedExports: true,
        innerGraph: true,
        minimize: true,
        minimizer: [new TerserJsPlugin({
            terserOptions: {
                ecma: 2019,
                compress: {
                    passes: 3
                },
                mangle: {
                },
                module: false,
                format: {
                    ecma: 2019
                },
                keep_classnames: false,
                keep_fnames: false,
                toplevel: true
            }
        })]
    },
        plugins: Array.prototype.concat.apply([
            new webpack.DefinePlugin({
                'process.env.NODE_DEBUG': false,
                'Platform.isNode': false
            }),
            new DuplicatesPlugin({
                emitErrors: false,
                emitHandler: undefined,
                ignoredPackages: undefined,
                verbose: false
            }),

        ], args.mode !== 'development' ? [] : [
            // In most cases we want to use external source map files
            new webpack.SourceMapDevToolPlugin({
                filename: '[name].js.map',
                exclude: 'remote.blazor.desktop.js',
            }),
            // ... but for blazor.webview.js, it has to be internal, due to https://github.com/MicrosoftEdge/WebView2Feedback/issues/961
            new webpack.SourceMapDevToolPlugin({
                include: 'remote.blazor.desktop.js',
            }),
        ]),
    stats: {
        warnings: true,
        errors: true,
        performance: true,
        optimizationBailout: true
    }
 
});
