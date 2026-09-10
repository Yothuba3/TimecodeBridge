# Web UI セットアップ

Node.js 22 以上を使用してください。依存バージョンは `package.json` に固定しています。

初回（まだ `package-lock.json` がない状態）は、`src/TimecodeBridge.Web` で次を実行します。

```sh
npm install
npm run typecheck
npm test
npm run build
```

生成された `package-lock.json` はコミットしてください。以後、lockfileがある環境とCIでは再現可能な取得を行います。

```sh
npm ci
npm run typecheck
npm test
npm run build
```

開発中の監視ビルドは `npm run dev` です。HTTPサーバーは起動せず、Hostが `dist/index.html` を読み込みます。
