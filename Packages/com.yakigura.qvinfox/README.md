[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md)

# QvInfoX

QvInfoX は QvPen ワールド向けの無料で非侵襲的なモニターです。プレイヤーの同期進捗、全体の完了予測、ペンごとの詳細、各ペンの現在のインクのプレビューを表示します。モニターは日本語・英語・韓国語に対応しています。QvInfoX は QvPen 本体を変更しません。

## インストール前：Cyan PlayerObjectPool リポジトリを追加

QvInfoX はサードパーティ製の **Cyan PlayerObjectPool** (`com.cyan.playerobjectpool`) に依存します。これは VCC の既定の一覧にも Yakigura リポジトリにも含まれません。先にリポジトリを VCC に追加しないと依存関係を解決できません。

1. VRChat Creator Companion の Settings > Packages > Add Repository で `https://cyanlaser.github.io/CyanPlayerObjectPool/index.json` をパッケージリポジトリとして追加します。
2. 次に以下の手順で Yakigura リポジトリを追加し、QvInfoX をインストールします。QvInkPreview と Qv Scene Setup は Yakigura リポジトリから自動的にインストールされます。

## VCC でのインストール

1. VRChat Creator Companion に `https://yakigura.github.io/vpm-qvinfox/index.json` をパッケージリポジトリとして追加します。
2. プロジェクトに **QvInfoX** をインストールします。設定済みのリポジトリからパッケージを取得できる場合、VCC は QvPen、QvInkPreview、Qv Scene Setup などの宣言済み依存関係を自動的に解決します。QvPen は Yakigura の一覧に含まれないため、QvPen を提供するリポジトリも設定してください。

## 動作要件

- Unity 2022.3 と VRChat Worlds SDK（`com.vrchat.worlds` および `com.vrchat.base`、宣言されたメジャーバージョン範囲内で 3.9.0 以上）
- QvPen（`net.ureishi.qvpen` 3.3.0 以上）
- QvInkPreview（`com.yakigura.qvinkpreview` 1.0.0 以上）と Qv Scene Setup（`com.yakigura.qvscenesetup` 1.0.0 以上）
- Cyan PlayerObjectPool（`com.cyan.playerobjectpool`、宣言されたメジャーバージョン範囲内で 1.1.2 以上）

正確な依存バージョン範囲は `package.json` を参照してください。

## ワールドに QvInfoX を追加

1. VPM クライアントからパッケージと依存関係をインストールします。ローカルコピーを使う場合は `Packages/com.yakigura.qvinfox` を依存関係とともに Unity プロジェクトに追加します。
2. QvPen を含む保存済みワールドシーンを開き、**Tools → Yakigura → QvInfoX → シーンに追加・更新 (Auto Setup)** を選びます。
3. Console で結果と警告を確認します。指示が出た場合は Unity のスクリプトコンパイル完了後に **シーンに追加・更新 (Auto Setup)** を再実行します。
4. シーンを保存してワールドをビルドします。セットアップにより、ペン詳細用のワールド固定 `QvInfoX_WorldState`、ヘッダーと進捗用の小さな `QvInfoX_MasterTotals`、モニター、Cyan PlayerObjectPool、検出された各ペン用の Indicator がそれぞれ作成されます。

新しいモニターは最初のスポーンの前方 2 m、スポーンの高さ + 0.9 m に配置されます。スポーンがない場合は (0, 0.9, 2) を使用し、再実行時は既存モニターの位置を保持します。

**シーンから取り除く (Remove from Scene)** はシーンの導入記録を使用し、QvPalette など他のツールが参照する `QvInkPreview_*` オブジェクトを保持します。生成されたアセットは他のシーンから引き続き利用できます。

既存の QvInfoX シーンではパッケージを更新し、**シーンに追加・更新 (Auto Setup)** を再実行して Console を確認し、保存して再ビルドします。これにより Master の集計が PlayerSlot から単一の WorldState に移行されます。再実行時には選択済みのモニター言語と Indicator の先端設定を保持します。

## ワールド内の操作

ペン詳細はセットアップで保存されたヒエラルキー順に表示されます。ペンのヒエラルキーを変更した後はセットアップを再実行してシーンを保存してください。

モニター右下にはパッケージバージョンを表示します。パッケージ更新後に表示を更新するにはセットアップを再実行してください。

モニター中央下の青緑色のハンドルをつかむとパネルを移動できます。モニターのタブでプレイヤーとペンの詳細を表示します。共通フッターには更新、言語、位置リセット、Indicator、QvHold の操作があります。

Indicator はペンに追従し、描画中や消去中は非表示になります。ペンモデルの先端方向が異なる場合は Indicator の `tailAxis` を `NegativeY`、`NegativeZ`、`Custom` のいずれかに設定し、Inspector で `positionOffset` を調整します。プレビュー位置はクライアントで目視確認が必要です。

## QvHold

**ペン保持 / Hold pens / 펜 유지** は、VR でペンを持ち続ける設定を ON / OFF で切り替える機能です。モニターのチェックボックスで切り替えます。デスクトップでは、元の QvPen と同じく、常にペンを持ち続けます。この設定はユーザーごとのもので、ほかの人には影響しません。

## トラブルシューティング

- **メニューがない、またはコンパイルエラー:** パッケージ依存関係を確認し、Unity のコンパイル完了後にセットアップを再実行してください。
- **セットアップ警告:** **シーンに追加・更新 (Auto Setup)** を再実行し、アクティブシーンに WorldState が1つだけあり、生成された Monitor と PlayerPool が有効であることを確認してください。
- **入室後に集計が保留:** 有効な現在の Master のスナップショットを受信して初めて Master の集計を表示します。途中参加と Master の引き継ぎは2クライアントで確認してください。
- **ペン数の不一致:** `masterPenStrokes` 配列の長さがシーンのペン数と異なる場合があります。一致する報告が届くまでペン詳細は保留されます。
- **文字やレイアウトの問題:** VRChat クライアントで3言語を切り替え、長いプレイヤー名を確認してください。
