# SwitchBotMeter

SwitchBot の温湿度計（Bluetooth LE）をスキャンして、温度・湿度をリアルタイム表示・グラフ化・記録する Windows デスクトップアプリです。

## 対応デバイス

BLEアドバタイズを直接解析しているため、専用アプリやクラウドを介さずに動作します。

- SwitchBot Meter
- SwitchBot Meter Plus
- SwitchBot Outdoor Meter
- SwitchBot Meter Pro
- SwitchBot Hub2（内蔵の温湿度センサー値。Hub2 経由で他のデバイスの値を取得することはできません）

## 主な機能

### デバイス一覧・スキャン
- 起動すると自動的に BLE スキャンを開始し、範囲内の対応デバイスを一覧表示
- デバイスごとに別名（ニックネーム）を設定可能（再起動後も保持）
- 検出した温度・湿度・バッテリー残量・最終受信日時をリアルタイム表示

### グラフ表示
- 温度・湿度をそれぞれ専用のグラフに上下2分割で表示
- 表示範囲を 30分・1時間・2時間・4時間・6時間・12時間・1日・1週間・1ヶ月・半年・1年 から選択可能
- グラフの一時停止／再開（一時停止中も裏でデータ記録は継続）
- 縦軸（上限・下限）はテキスト入力または ▲▼ ボタンで自由に設定可能（設定値は保存される）
- デバイスごとにグラフの表示色を16色から選択可能（デバイス一覧・グラフの線色に反映）

### 履歴の記録
- 受信したデータを1分間隔に間引いてデバイスごとの CSV ファイルへ自動保存
- 起動直後（スキャン開始前）でも、保存済みの履歴から直近の状態を復元して表示
- 別名・グラフ表示色・履歴 CSV の保存先フォルダはアプリ内から任意の場所に変更可能（実行ファイルの再ビルド・削除に影響されない場所を選べます）

### バックグラウンド監視（OBS 等への値渡し用）
- 指定した1台のデバイスの温度・湿度を、指定したテキストファイルへ継続的に書き出す機能
- 出力先ファイルパスは保存され、値が変化した時のみファイルを更新
- 監視対象デバイスが設定済みの場合、起動時に自動でスキャン・監視を開始

### その他
- ダークテーマ UI（タイトルバーを含む）
- ウィンドウサイズ・位置を記憶
- タイトルバーにバージョン番号を表示（ビルドごとにリビジョン番号を自動加算）
- スキャン受信状況を表示する折りたたみ式デバッグログ

## 動作環境

- Windows 10 Build 19041 以降（Bluetooth LE 対応必須）
- .NET 8

## ビルドに必要な SDK

- .NET SDK 8.0
  - https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0

## ビルド・発行

通常ビルド:
```
dotnet build -c Release
```

配布用に自己完結型のシングル exe を発行する場合:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 使い方

1. アプリを起動すると自動的にスキャンが始まり、範囲内のデバイスがデバイス一覧に表示されます。
2. デバイスを選択すると、右側の「デバイス情報」欄で別名やグラフ表示色を設定できます。
3. デバイス一覧のチェックボックスで、グラフに表示するデバイスを選べます。
4. 特定の1台の値を外部ファイルへ書き出したい場合（例: OBS の配信画面に温度・湿度を重ねて表示する等）は、「バックグラウンド監視」欄でデバイスを選択し、出力先ファイルを指定して監視を開始してください。

## 使用ライブラリ

- [LiveChartsCore.SkiaSharpView.WPF](https://livecharts.dev/) — グラフ描画
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 実装

## 参考

以下の情報を参考にしました。ありがとうございます。

- https://github.com/OpenWonderLabs/python-host/wiki/Meter-BLE-open-API
- https://github.com/sblibs/pySwitchbot
- https://qiita.com/warpzone/items/11ec9bef21f5b965bce3

## License

MIT License. 詳細は [LICENSE](LICENSE) を参照してください。
