# Qv Scene Setup

QvFastSync と QvInfoX が使うシーン単位の導入記録です。記録 GameObject は `EditorOnly` タグでビルドから除外されます。

追加時に作成した GameObject / Component と、既存のシリアライズ値の変更前・変更後・プレハブオーバーライド状態を記録します。取り除き時は導入後から変わっていない値だけ復元します。ユーザーが変更した値は競合として表示し、batch の `-qvForce 1` で強制復元できます。

記録の無い旧シーンは安全に削除できないため、取り除き処理は停止します。

## VCC でのインストール

1. VRChat Creator Companion に `https://yakigura.github.io/vpm-qvinfox/index.json` をパッケージリポジトリとして追加します。
2. プロジェクトで **Qv Scene Setup** をインストールします。QvInfoX を導入する場合は、その依存として自動的にインストールされます。

Qv Scene Setup 自体に QvPen や QvInkPreview の依存はありません。QvInfoX の導入時は、利用可能なリポジトリから QvPen と QvInkPreview が依存として自動解決されます。QvPen は Yakigura の一覧に含まれないため、QvPen を提供するリポジトリも設定してください。
