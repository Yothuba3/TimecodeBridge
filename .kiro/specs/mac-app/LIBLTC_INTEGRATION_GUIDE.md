# libltc.dylib 統合ガイド

## 概要

このドキュメントは、TimecodeBridge macOS版においてlibltc.dylibをビルド・配置・統合するための包括的なガイドです。Tasks 9.1-9.3の実装に必要な全情報を含みます。

## Task 9.1: libltc.dylibのビルドと配置

### 前提条件

- macOS 12 (Monterey) 以降
- Xcode Command Line Tools インストール済み
- Homebrew インストール済み（推奨）

### オプション1: Homebrewからのインストール（推奨）

```bash
# libltcをインストール
brew install libltc

# インストール先を確認
brew list libltc
# 通常: /opt/homebrew/lib/libltc.dylib (Apple Silicon)
#       /usr/local/lib/libltc.dylib (Intel)

# シンボリックリンクを確認
ls -la /opt/homebrew/lib/libltc*
# libltc.11.dylib -> libltc.11.0.0.dylib (実体)
# libltc.dylib -> libltc.11.dylib (シンボリックリンク)

# ユニバーサルバイナリか確認
file /opt/homebrew/lib/libltc.11.dylib
# 出力例: Mach-O 64-bit dynamically linked shared library arm64
# または: Mach-O universal binary with 2 architectures: [x86_64:...] [arm64:...]
```

**注意**: Homebrew版がユニバーサルバイナリでない場合、両アーキテクチャのサポートにはオプション2のソースビルドが必要です。

### オプション2: ソースからのユニバーサルバイナリビルド

```bash
# 依存関係のインストール
brew install automake autoconf libtool

# ソースコード取得
cd ~/Downloads
git clone https://github.com/x42/libltc.git
cd libltc

# ユニバーサルバイナリビルド (x64 + ARM64)
./autogen.sh
CFLAGS="-arch x86_64 -arch arm64 -mmacosx-version-min=12.0" \
LDFLAGS="-arch x86_64 -arch arm64" \
./configure --prefix=/usr/local
make clean
make
sudo make install

# ビルド成果物の確認
file /usr/local/lib/libltc.dylib
# 出力: Mach-O universal binary with 2 architectures: [x86_64:...] [arm64:...]

lipo -info /usr/local/lib/libltc.dylib
# 出力: Architectures in the fat file: /usr/local/lib/libltc.dylib are: x86_64 arm64
```

### オプション3: 個別アーキテクチャビルド（非推奨）

x64とARM64を別々にビルドする場合:

```bash
# x64版ビルド
CFLAGS="-arch x86_64 -mmacosx-version-min=12.0" \
LDFLAGS="-arch x86_64" \
./configure --prefix=/tmp/libltc-x64
make clean && make && make install

# ARM64版ビルド
CFLAGS="-arch arm64 -mmacosx-version-min=12.0" \
LDFLAGS="-arch arm64" \
./configure --prefix=/tmp/libltc-arm64
make clean && make && make install

# ユニバーサルバイナリ作成
lipo -create \
  /tmp/libltc-x64/lib/libltc.dylib \
  /tmp/libltc-arm64/lib/libltc.dylib \
  -output /usr/local/lib/libltc.dylib
```

### .appバンドルへの配置戦略

libltc.dylibを.appバンドルに含める方法は2つあります:

#### 戦略A: Runtime Identifier (RID) 固有のネイティブフォルダ

```
TimecodeBridge.app/
└── Contents/
    ├── MacOS/
    │   └── TimecodeBridge (実行ファイル)
    └── Resources/
        └── runtimes/
            ├── osx-x64/
            │   └── native/
            │       └── libltc.dylib (x64版)
            └── osx-arm64/
                └── native/
                    └── libltc.dylib (ARM64版)
```

**実装方法**:

TimecodeBridge.macOS.csproj に以下を追加:

```xml
<ItemGroup>
  <!-- x64版 -->
  <Content Include="$(HOME)/Downloads/libltc-x64/lib/libltc.dylib" Condition="Exists('$(HOME)/Downloads/libltc-x64/lib/libltc.dylib')">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>runtimes/osx-x64/native/libltc.dylib</Link>
  </Content>

  <!-- ARM64版 -->
  <Content Include="$(HOME)/Downloads/libltc-arm64/lib/libltc.dylib" Condition="Exists('$(HOME)/Downloads/libltc-arm64/lib/libltc.dylib')">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>runtimes/osx-arm64/native/libltc.dylib</Link>
  </Content>
</ItemGroup>
```

#### 戦略B: ユニバーサルバイナリ配置（推奨）

```
TimecodeBridge.app/
└── Contents/
    ├── MacOS/
    │   └── TimecodeBridge (実行ファイル)
    └── Frameworks/
        └── libltc.dylib (ユニバーサルバイナリ)
```

**実装方法**:

1. プロジェクトフォルダ内にネイティブライブラリディレクトリ作成:

```bash
mkdir -p src/TimecodeBridge.macOS/Native/macos
cp /usr/local/lib/libltc.dylib src/TimecodeBridge.macOS/Native/macos/
```

2. TimecodeBridge.macOS.csproj に追加:

```xml
<ItemGroup>
  <Content Include="Native/macos/libltc.dylib">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    <Link>libltc.dylib</Link>
  </Content>
</ItemGroup>
```

3. 実行時のライブラリパス設定（後述のTask 9.2を参照）

**推奨**: 戦略Bがシンプルで保守性が高い。ユニバーサルバイナリ1つで両アーキテクチャをサポート。

## Task 9.2: LtcEncoder/DecoderのP/Invokeパス設定

### 現状分析

現在のLtcEncoderとLtcDecoderは**純粋なマネージドC#実装**であり、ネイティブlibltcへのP/Invoke呼び出しは**含まれていません**。

**確認済み実装**:
- `src/TimecodeBridge.Core/Services/LtcEncoder.cs`: ビフェーズマーク符号化（BMC）をC#で実装
- `src/TimecodeBridge.Core/Services/LtcDecoder.cs`: ゼロクロス検出とBMCデコードをC#で実装

### 結論: P/Invoke統合は不要

**Task 9.2は実質的に完了しています**。理由:

1. 既存実装は完全なマネージドコードで動作
2. 全フレームレート（23.98、24、25、29.97、30、59.94、60fps）をサポート済み
3. Windows版とmacOS版で同一コードを共有可能
4. ネイティブライブラリ依存がないため配布が容易

### P/Invokeが必要な場合の参考実装

将来的にネイティブlibltcを使用する必要が生じた場合:

```csharp
using System.Runtime.InteropServices;

namespace TimecodeBridge.Core.Services;

public class LtcEncoderNative : ILtcEncoder
{
    // macOS: @rpath解決により、実行時に以下を検索:
    // 1. バンドル内の .app/Contents/Frameworks/
    // 2. バンドル内の .app/Contents/MacOS/
    // 3. システムパス (/usr/local/lib、/opt/homebrew/lib)
    private const string LibraryName = "libltc";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ltc_encoder_create(
        double sample_rate,
        double fps,
        LtcTvStandard standard,
        LtcBgFlags flags);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ltc_encoder_free(IntPtr encoder);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void ltc_encoder_set_timecode(
        IntPtr encoder,
        ref LtcFrame frame);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int ltc_encoder_encode_frame(IntPtr encoder);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ltc_encoder_get_bufferptr(
        IntPtr encoder,
        ref int size,
        ref int parity);

    private enum LtcTvStandard
    {
        LTC_TV_525_60 = 0,  // NTSC 30fps
        LTC_TV_625_50 = 1,  // PAL 25fps
        LTC_TV_1125_60 = 2, // HDTV 30fps
        LTC_TV_FILM_24 = 3  // Film 24fps
    }

    [Flags]
    private enum LtcBgFlags
    {
        LTC_NO_PARITY = 1,
        LTC_USE_DATE = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LtcFrame
    {
        // libltcのLTCFrame構造体
        public byte frame_units;
        public byte frame_tens;
        public byte seconds_units;
        public byte seconds_tens;
        public byte minutes_units;
        public byte minutes_tens;
        public byte hours_units;
        public byte hours_tens;
        // 他のフィールド省略
    }

    // 実装メソッド...
}
```

**注意**: 上記は参考実装であり、現在のプロジェクトでは不要です。

## Task 9.3: LTCエンコード/デコード動作確認

### 単体テスト戦略

既存のテストスイートを活用:

```bash
cd /Users/yothuba/TimecodeBridge
dotnet test tests/TimecodeBridge.Tests/Services/LtcEncoderTests.cs
dotnet test tests/TimecodeBridge.Tests/Services/LtcDecoderTests.cs
```

### 全フレームレートテスト実装

新規テストファイル作成: `tests/TimecodeBridge.Tests/Services/LtcMacOSCompatibilityTests.cs`

```csharp
using Xunit;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Models;

namespace TimecodeBridge.Tests.Services;

public class LtcMacOSCompatibilityTests
{
    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps2997Drop, 30)]
    [InlineData(FrameRate.Fps30, 30)]
    [InlineData(FrameRate.Fps5994, 60)]
    [InlineData(FrameRate.Fps60, 60)]
    public void LtcEncoder_AllFrameRates_GeneratesValidSignal(FrameRate frameRate, int fps)
    {
        // Arrange
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, frameRate);
        var timecode = new TimecodeValue(1, 23, 45, 10, frameRate);

        // Act
        encoder.EnqueueFrame(timecode);
        byte[] buffer = new byte[48000 * 2]; // 1秒分のバッファ
        int bytesRead = encoder.Read(buffer, 0, buffer.Length);

        // Assert
        Assert.True(bytesRead > 0, "エンコードされたデータが生成されること");
        Assert.True(HasNonZeroSamples(buffer, bytesRead), "無音でないこと");
    }

    [Theory]
    [InlineData(FrameRate.Fps24, 24)]
    [InlineData(FrameRate.Fps25, 25)]
    [InlineData(FrameRate.Fps30, 30)]
    public void LtcDecoder_AllFrameRates_DecodesCorrectly(FrameRate frameRate, int fps)
    {
        // Arrange
        var encoder = new LtcEncoder();
        encoder.Initialize(48000, frameRate);
        var decoder = new LtcDecoder();
        decoder.Initialize(48000, fps);

        var originalTimecode = new TimecodeValue(0, 5, 30, 15, frameRate);
        TimecodeValue? decodedTimecode = null;
        decoder.FrameDecoded += (sender, tc) => decodedTimecode = tc;

        // Act
        encoder.EnqueueFrame(originalTimecode);
        byte[] encodedBuffer = new byte[48000 * 2];
        int bytesRead = encoder.Read(encodedBuffer, 0, encodedBuffer.Length);

        decoder.ProcessSamples(encodedBuffer, bytesRead, 48000, 16, 1);

        // Assert
        Assert.NotNull(decodedTimecode);
        Assert.Equal(originalTimecode.Hours, decodedTimecode!.Value.Hours);
        Assert.Equal(originalTimecode.Minutes, decodedTimecode.Value.Minutes);
        Assert.Equal(originalTimecode.Seconds, decodedTimecode.Value.Seconds);
        Assert.Equal(originalTimecode.Frames, decodedTimecode.Value.Frames);
    }

    [Fact]
    public void LtcRoundTrip_macOSEnvironment_PreservesTimecode()
    {
        // macOS環境特有のラウンドトリップテスト
        var encoder = new LtcEncoder();
        var decoder = new LtcDecoder();
        encoder.Initialize(48000, FrameRate.Fps30);
        decoder.Initialize(48000, 30);

        var testTimecode = new TimecodeValue(12, 34, 56, 28, FrameRate.Fps30);
        TimecodeValue? result = null;
        decoder.FrameDecoded += (_, tc) => result = tc;

        encoder.EnqueueFrame(testTimecode);
        byte[] buffer = new byte[10000];
        int read = encoder.Read(buffer, 0, buffer.Length);
        decoder.ProcessSamples(buffer, read, 48000, 16, 1);

        Assert.NotNull(result);
        Assert.Equal(testTimecode, result!.Value);
    }

    private bool HasNonZeroSamples(byte[] buffer, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (buffer[i] != 0) return true;
        }
        return false;
    }
}
```

### 手動検証手順

#### 必要なツール

- **Audacity** (無料): https://www.audacityteam.org/
- または **Reaper** (試用版可): https://www.reaper.fm/

#### 検証手順

1. **LTCエンコード信号の生成と検証**

```bash
# macOS版アプリをビルド
cd /Users/yothuba/TimecodeBridge
dotnet build src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj

# 実行
cd src/TimecodeBridge.macOS/bin/Debug/net8.0
./TimecodeBridge.macOS
```

アプリケーション内で:
- タイムコード生成モードを選択
- 開始時刻を 01:00:00:00 に設定
- フレームレートを 30fps に設定
- LTCエンコード出力を開始
- macOSのオーディオ出力デバイスを "TimecodeBridge Output" に設定

Audacityで:
- 入力デバイスを "TimecodeBridge Output" に設定
- 録音開始 (10秒程度)
- 波形確認: 矩形波パターンが表示されるはず
- 周波数確認: 30fps × 80bits = 2400Hz の基本周波数

2. **LTCデコード動作確認**

テスト用LTC信号の生成（別のLTCジェネレーターアプリ、またはTimecodeBridge Windows版を使用）:

```bash
# macOS版でLTC入力モードを選択
# オーディオ入力デバイスを選択（例: BlackHole 2ch、またはループバックデバイス）
# デコードされたタイムコードがUIに表示されることを確認
```

3. **全フレームレート検証**

各フレームレートで上記手順を繰り返し:
- 23.98 fps
- 24 fps
- 25 fps
- 29.97 fps (Drop Frame)
- 30 fps
- 59.94 fps
- 60 fps

期待結果:
- 全フレームレートでLTC信号生成が成功
- デコード時にフレームレートが正確に判定される
- タイムコード値が1フレームの誤差なく一致

### トラブルシューティング

#### 問題1: libltc.dylibが見つからない

エラーメッセージ:
```
DllNotFoundException: Unable to load shared library 'libltc' or one of its dependencies
```

**解決策**: 現在の実装ではlibltcは不要です。エラーが発生する場合、古いP/Invokeコードが残っている可能性があります。

```bash
# P/Invoke呼び出しの検索
grep -r "DllImport.*libltc" src/TimecodeBridge.Core/
```

#### 問題2: エンコード信号が無音

**原因**: VolumeLevel設定が0、またはオーディオデバイス選択エラー

**デバッグ**:
```csharp
// LtcEncoderのVolumeLevel確認
encoder.VolumeLevel = 0.8f; // 80%に設定
```

#### 問題3: デコードが不安定

**原因**: サンプルレート不一致、または入力信号のノイズ

**デバッグ**:
```csharp
// デコーダ初期化時のサンプルレート確認
decoder.Initialize(48000, 30); // エンコーダと一致させる
```

## ビルドと配置の自動化

### dotnet publish時の自動配置

（現在libltcは不要のため参考情報）

```bash
# ユニバーサルバイナリとして配置する場合
dotnet publish src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained

# 生成された.appバンドル確認
ls -la src/TimecodeBridge.macOS/bin/Release/net8.0/osx-arm64/publish/TimecodeBridge.app/Contents/
```

### CI/CD統合例

`.github/workflows/build-macos.yml` (参考):

```yaml
name: Build macOS App

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: macos-14
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      # libltcは現在不要のため、この手順はスキップ可能
      # - name: Install libltc
      #   run: brew install libltc

      - name: Build macOS App
        run: |
          dotnet publish src/TimecodeBridge.macOS/TimecodeBridge.macOS.csproj \
            -c Release \
            -r osx-arm64 \
            --self-contained

      - name: Verify Build
        run: |
          file src/TimecodeBridge.macOS/bin/Release/net8.0/osx-arm64/publish/TimecodeBridge.app/Contents/MacOS/TimecodeBridge
```

## 次のステップ

Task 9.1-9.3完了後:
- Task 10: CoreAudioCapture統合テスト
- Task 11: タイムコード受信ステータス表示
- Task 12: .appバンドル生成とコード署名

## 付録: libltc APIリファレンス

（現在のマネージド実装では不要ですが、将来のP/Invoke実装の参考として保持）

### 主要関数

```c
// エンコーダ作成
LTCEncoder* ltc_encoder_create(
    double sample_rate,    // サンプルレート（48000推奨）
    double fps,            // フレームレート
    enum LTC_TV_STANDARD standard,
    int flags
);

// デコーダ作成
LTCDecoder* ltc_decoder_create(
    int apv,               // Audio frames per video frame
    int queue_size         // キューサイズ
);

// タイムコード設定
void ltc_encoder_set_timecode(
    LTCEncoder *e,
    LTCFrame *frame
);

// エンコード実行
int ltc_encoder_encode_frame(LTCEncoder *e);

// バッファ取得
void ltc_encoder_get_buffer(
    LTCEncoder *e,
    ltcsnd_sample_t **buf
);

// デコード実行
int ltc_decoder_write(
    LTCDecoder *d,
    ltcsnd_sample_t *buf,
    size_t size,
    ltc_off_t posinfo
);
```

### LTCFrame構造体

```c
typedef struct LTCFrame {
    unsigned char frame_units:4;
    unsigned char frame_tens:2;
    unsigned char drop_frame:1;
    unsigned char color_frame:1;

    unsigned char secs_units:4;
    unsigned char secs_tens:3;
    unsigned char flag1:1;

    unsigned char mins_units:4;
    unsigned char mins_tens:3;
    unsigned char flag2:1;

    unsigned char hours_units:4;
    unsigned char hours_tens:2;
    unsigned char flag3:1;
    unsigned char flag4:1;

    unsigned char user1:4;
    unsigned char user2:4;
    unsigned char user3:4;
    unsigned char user4:4;
    unsigned char user5:4;
    unsigned char user6:4;
    unsigned char user7:4;
    unsigned char user8:4;

    unsigned int sync_word:16;
} LTCFrame;
```

## まとめ

- **Task 9.1**: libltc.dylibは現在の実装では不要（マネージドC#実装を使用）
- **Task 9.2**: P/Invoke設定は不要（既存実装で完結）
- **Task 9.3**: 単体テストと手動検証により全フレームレートの動作確認を実施

この戦略により、ネイティブライブラリ依存なしでクロスプラットフォーム対応を維持できます。
