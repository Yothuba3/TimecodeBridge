# サードパーティ ソフトウェアの表示(TimecodeBridge v3)

## libltc

TimecodeBridge v3 は LTC(Linear Timecode)の復号に **libltc** を使用しています。

- 著作権: Copyright (C) 2006-2022 Robin Gareus <robin@gareus.org>, Copyright (C) 2008-2009 Jan Weiß <jan@geheimwerk.de>
- ライセンス: GNU Lesser General Public License v3.0 以降(LGPL-3.0-or-later)。全文は同梱の `libltc-COPYING.txt` を参照
- ソースコード: https://github.com/x42/libltc (使用バージョン 1.3.2、取得元とハッシュは `native/libltc/VERSION`)
- 本アプリは libltc を改変せず、動的ライブラリ(macOS: `libltc.dylib` / Windows: `libltc.dll`)として同梱し、実行時にリンクしています。
  同梱のライブラリファイルを同じ API を持つ別ビルドに差し替えることができます。ビルド手順は `native/libltc/` にあります。

## その他

- Avalonia UI(MIT)、Avalonia.Controls.WebView(MIT)、Preact(MIT)、NAudio(MIT)、Microsoft.Extensions.DependencyInjection(MIT)。
