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

正確な依存バージョン範囲は `package.json` を参照してください。2026-11-04 にビルドと実機確認を行い、QvHold の VR ON/OFF 切り替えとデスクトップでの常時保持を確認しました。使用機器は記録されていません。その後の変更のコンパイルと実行時動作、および新規プロジェクトへの VPM リポジトリ経由のインストールは未検証です。

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

**ペン保持 / Hold pens / 펜 유지** はユーザーごとのローカル機能で、Pickup v1.0 に対応します。元の QvPen のペンは AutoHold `Sometimes` の v1.0 を使用するため Pickup の移行は不要です。実行中に設定を切り替えられるのは VR ユーザーのみです。**ON は全対象に AutoHold `Yes`、OFF は `No` を要求**し、元が `Yes` のペンも対象です。デスクトップでは常に保持が有効で、QvHold は AutoHold に書き込まず、チェックボックスは無効になり **デスクトップは常にホールド / Desktop: always hold / 데스크톱: 항상 홀드** と表示されます。元の値は保存・復元しません。保持中のペンへの変更は手放すまで待機し、チェックボックスは最新の要求を反映し、**手放すと反映 / Applies after release / 놓으면 적용** が遅延を示します。強制 Drop や描画・Use イベントの変更は行いません。設定は訪問間で保存しません（PlayerData なし）。


`QvInfoX/QvInfoX_Monitor/HoldController` の **入室時の初期状態 / Initial state on entry / 입장 시 초기 상태**（`initialState`、整数 0〜2）には次の設定があります。

| 値 | 初期状態 | 入室時の動作 |
| --- | --- | --- |
| **0（既定）** | シーンの設定のまま / Keep scene settings / 씬 설정 유지 | 書き込まず、各 AutoHold の現在値を UI に読み込みます。すべて `Yes` なら ON、すべて `Yes` 以外なら OFF、混在時は未チェックで **混在（押すとON） / Mixed (click: ON) / 혼합 (누르면 ON)** と表示。`Yes` 以外は正規化せず OFF と表示します。 |
| 1 | 保持 ON / Hold ON / 유지 ON | `Yes` を要求し、保持中のペンは保留します。 |
| 2 | 保持 OFF / Hold OFF / 유지 OFF | `No` を要求し、保持中のペンは保留します。 |

シーン設定モードで混在する場合、最初のクリックで全対象に ON、次のクリックで OFF を要求します。すべて `Yes` の場合は最初のクリックで OFF を要求します。状態行には **QvHold・自分のみ / QvHold · Local only / QvHold · 나에게만 적용** に ON/OFF または混在を追加表示します。保留は混在より優先され、要求された ON/OFF を表示します。リスポーンや Controller 再有効化では現在の要求、または要求前なら未変更のシーン設定モードを保持し、入室時の初期設定を再適用しません。

セットアップは同じ GameObject に `QvPen_Pen` と SDK3 `VRCPickup` があり、Pickup が **v1.0 または v1.1** のシーンインスタンスだけを接続します。非アクティブなペンを含み、消しゴム、他のツール、モニターの Pickup は除きます。対応対象がゼロの場合 Toggle は OFF で無効です。セットアップは対応・スキップ件数、各スキップペンのヒエラルキーパスと理由、復旧手順を Console に表示しますが、検証やビルドの要件は追加しません。既存の v1.0 / v1.1 Pickup は正規化しません。

対象を選択するには `QvInfoX/QvInfoX_Monitor/HoldController` の **Selected Pens Only** を有効にし、**Selected Pens** に QvPen のペンコンポーネントを設定して、セットアップを再実行し保存します。既定は対応する全ペンです。再実行しても選択と入室時モードは保持されます。セットアップは入室時モード（シーン設定の混在値を含む）をプレビューしますが、実行時の AutoHold 値は適用しません。Editor 側からは `QvInfoXSetupOptions.HoldInitialState` の設定、`PerformSetup` または `SetupMonitor` の `holdInitialState: 0/1/2`、`QvInfoX_HoldSetup.Setup` の `initialState: 0/1/2` を使用できます。作成引数は既存 Controller を上書きしません。ペンの追加・削除や選択・バージョン変更後はセットアップを再実行してください。プログラムアセットと UI 参照は既存インストーラーが生成するため、その Undo/Redo と取り除き操作を使用してください。

**古い bool から Controller の入室時設定を更新する場合（Pickup のバージョンとは無関係）:** 新しい入室時モードを編集する前に一度セットアップを実行し、保存・再ビルドしてください。シリアライズ済み `initiallyEnabled` bool は互換性のため非表示で保持されます。古い `true` は一度だけモード 1（ON）に移行し、`false` はモード 0（シーン設定）に対応して、従来の入室時に書き込まない動作を保持します。セットアップが旧 bool を処理するため、その後に編集した3モードの設定は再実行しても保持されます。移行前の実行時も `initialState` が 0 のとき旧 `true` を ON として扱い、明示的なモード 1 または 2 は優先されます。したがって未移行で旧値が `true` の Controller にモード 0 を設定するだけでは旧フラグを上書きできません。先にセットアップを実行してください。既存 Editor の bool 引数と `QvInfoXSetupOptions.InitiallyEnableHold` はソース互換を保ちます。新モードが 0 の場合、`true` は作成時モード 1、`false` はシーン設定を指定します。明示的な新モード 1 または 2 は bool より優先されます。bool から int への自動シリアライズ変換は前提としません。

この機能は固定されたシーン参照と常時有効な Controller を前提とします。VR では初期 ON/OFF により保持されていない対象にのみ Yes/No を直ちに適用します。シーン設定モードは最初の要求まで書き込みません。接続された非アクティブなペンも対象で、保持されていない Pickup は非アクティブ中に設定でき、有効化後も値を保持します。保留中の変更は SDK の遅延イベント予約を使わず、Update から 0.25 秒間隔で確認します。早期または古い `_ProcessPending` コールバックは何もせず Update の確認を止めません。タブ切り替え、プレイヤーやペンのリスポーン後もローカル要求を保持します。動的生成ペンおよび同じペンの AutoHold に同時書き込みするスクリプトは非対応です。シーン設定モードは外部からの書き込みを継続監視しません。Controller を無効にすると保留処理は一時停止し、再有効化で再開します。削除した場合は最後にローカル適用した値が残り、復元処理はありません。保留要求の完了まで有効にしてください。2026-11-04 の実機確認は VR の ON/OFF 切り替え、デスクトップの常時保持、ビルドを対象とし、使用機器は記録されていません。ClientSim は UI と値の確認を補助するものに限られます。

## トラブルシューティング

- **メニューがない、またはコンパイルエラー:** パッケージ依存関係を確認し、Unity のコンパイル完了後にセットアップを再実行してください。
- **セットアップ警告:** **シーンに追加・更新 (Auto Setup)** を再実行し、アクティブシーンに WorldState が1つだけあり、生成された Monitor と PlayerPool が有効であることを確認してください。
- **入室後に集計が保留:** 有効な現在の Master のスナップショットを受信して初めて Master の集計を表示します。途中参加と Master の引き継ぎは2クライアントで確認してください。
- **ペン数の不一致:** `masterPenStrokes` 配列の長さがシーンのペン数と異なる場合があります。一致する報告が届くまでペン詳細は保留されます。
- **文字やレイアウトの問題:** VRChat クライアントで3言語を切り替え、長いプレイヤー名を確認してください。Editor での検証だけでは Quest での文字表示は証明できません。
