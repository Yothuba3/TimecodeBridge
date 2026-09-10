# libltc(LTC 復号ライブラリ)の同梱ビルド

TimecodeBridge v3 は LTC の復号に [libltc](https://x42.github.io/libltc/) 1.3.2(LGPL-3.0-or-later)を使う。
ライブラリは改変せず、動的リンク(dylib / dll)でアプリに同梱する。取得元とハッシュは `VERSION` に固定している。

- macOS(Apple Silicon 専用): `./build-macos.sh <出力ディレクトリ>` — ソース取得・sha256 検証・arm64 ビルド・`@rpath` 化・ad-hoc 署名・検証を一括で行う。
- Windows: `build-windows.cmd <出力ディレクトリ>` — Developer Command Prompt(vcvars64)で実行。ヘッダにエクスポート宣言がないため `libltc.def` で公開関数を列挙している。
- 出力には `libltc-COPYING.txt`(LGPL 全文)を含める。アプリの「バージョン情報」から著作権表示・ライセンス・ソース入手先へ到達できるようにする。
