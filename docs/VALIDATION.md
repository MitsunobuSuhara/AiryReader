# 試作0.1の検証記録

実施日: 2026-09-13

## 結果

- Releaseビルド: 警告0・エラー0。
- PDF・印刷計算・保存・Windows印刷経路: 33項目PASS。
- WPF画面・設定変更時の印刷無効化: 5項目PASS。
- 実行用の.NET同梱exeからも自動テスト成功（終了コード0）。
- 表示画面、印刷画面、2アップのスクリーンショットを目視。ボタンの隠れを修正して左下へ固定。

## 寸法の確認

自動生成したA4のPDFに縦横100mmの線を描き、Microsoft Print to PDFへ出力。出力PDFを4px/mmで再描画し、線の黒い外形の連続画素数を計測。

| 出力 | 用紙 | 横の検出長 | 縦の検出長 |
|---|---|---|---|
| 50% | A4縦 | 50.25mm | 50.25mm |
| 100% | A4縦 | 100.25mm | 100.25mm |
| 200% | A3横 | 200.25mm | 200.25mm |

計測は線幅と画素化を含む。これは紙上の誤差0.25mmを意味せず、合格判定は設計値に対する±4画素（このテストだけの検出許容値）。実物の許容誤差は未合意。Apeos C3571とAcrobat Readerによる紙上比較は未実施。

A3横向きでPageSettings.PrintableAreaが縦向きの幅を返し、配置がずれる不具合を検出。印刷設定を渡したCreateMeasurementGraphicsからDCの物理寸法・余白を取得する方式へ変更し、200%の縦横テストで再発を確認する。

## 再現

`scripts/build.ps1 -Test -Publish`。テストの原本・出力・ログ・画像はartifactsへ作成。物理プリンターには送信しない。ドライバーや利用者のPDFはGitへ保存しない。

## 取得済みドライバー

[富士フイルム公式ダウンロードページ](https://www.fujifilm.com/fb/ja/support/multifunction-printers/color/download-00851)のApeosシリーズ用ART EX 7.1.10。Windows 11 64ビット、Apeos C3571に対応する版を選択。

`artifacts/drivers/ffopkplw250330w646fml.exe`、29,382,112バイト、署名Valid、発行元FUJIFILM Business Innovation Corp.。未インストール・接続先未設定。

SHA256: `12F6C2105308E7B21BF191BB6FBA79905E2880E65A11950AD958E89137B9B9C4`

## 技術資料

- [PDFium配布元・固定版8044](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium/8044)
- [PDFium API](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h)
- [Microsoft PrintableArea](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.pagesettings.printablearea?view=windowsdesktop-10.0)

## 今後の確認

Apeos実機、両面と小冊子の実際の向き、切り取り線と紙上番号、フォーム・署名・複雑な図面、代表PDFでの起動時間とズーム性能。現在の結果は、Acrobat Reader同等の完成判定ではない。
