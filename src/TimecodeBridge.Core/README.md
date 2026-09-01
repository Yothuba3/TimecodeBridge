# TimecodeBridge.Core

## 概要

TimecodeBridge.Coreは、TimecodeBridgeアプリケーションのプラットフォーム非依存コアロジックを含むクラスライブラリです。

## 目的

- Windows版とmacOS版で共有可能なビジネスロジックの提供
- プラットフォーム固有実装（UI、オーディオAPI）からの分離
- クロスプラットフォーム対応の基盤構築

## 技術スタック

- **.NET 8.0** - クロスプラットフォームターゲット
- **CommunityToolkit.Mvvm 8.4.0** - MVVMパターン実装
- **BuildSoft.OscCore 1.2.1.1** - OSC通信
- **System.Text.Json** - JSON永続化

## プロジェクト構造

```
TimecodeBridge.Core/
├── Models/                  # データモデル
│   ├── TimecodeValue.cs
│   ├── ProjectData.cs
│   ├── Cue.cs
│   └── ...
├── Services/                # ビジネスロジック
│   ├── Interfaces/          # サービスインターフェース
│   │   ├── ITimecodeEngine.cs
│   │   ├── ICueManager.cs
│   │   └── ...
│   ├── TimecodeEngine.cs
│   ├── CueManager.cs
│   ├── ProjectService.cs
│   └── ...
└── Native/                  # ネイティブライブラリP/Invoke
    └── LtcFrameHelper.cs
```

## 設計原則

1. **プラットフォーム非依存**: UIフレームワーク（WPF、Avalonia）やOS固有APIへの直接依存を避ける
2. **Interface-First**: 全サービスをインターフェース経由で公開
3. **依存性逆転**: プラットフォーム固有実装はインターフェースを実装し、DI Containerで注入
4. **MVVM準拠**: ViewModelとModelの分離、データバインディング対応

## 依存関係

- **外部依存なし**: プラットフォーム固有パッケージ（NAudio、WPF、Avaloniaなど）は含まない
- **純粋なビジネスロジック**: タイムコード処理、キュー管理、OSC送信、プロジェクト管理

## 使用方法

### Windows版での参照

```xml
<ProjectReference Include="..\TimecodeBridge.Core\TimecodeBridge.Core.csproj" />
```

### macOS版での参照

```xml
<ProjectReference Include="..\TimecodeBridge.Core\TimecodeBridge.Core.csproj" />
```

## 関連ドキュメント

- [Technical Design Document](../../.kiro/specs/mac-app/design.md)
- [Requirements Document](../../.kiro/specs/mac-app/requirements.md)
- [Implementation Tasks](../../.kiro/specs/mac-app/tasks.md)
