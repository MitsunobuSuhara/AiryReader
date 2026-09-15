from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.section import WD_SECTION
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.enum.table import WD_CELL_VERTICAL_ALIGNMENT
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "share"
OUT.mkdir(parents=True, exist_ok=True)
DOCX = OUT / "AiryReader_AIとの会話から生まれたPDFアプリ.docx"

doc = Document()
sec = doc.sections[0]
sec.page_width, sec.page_height = Inches(8.5), Inches(11)
sec.top_margin, sec.bottom_margin = Inches(.72), Inches(.72)
sec.left_margin, sec.right_margin = Inches(.85), Inches(.85)

styles = doc.styles
styles["Normal"].font.name = "Yu Gothic"
styles["Normal"]._element.rPr.rFonts.set(qn("w:eastAsia"), "Yu Gothic")
styles["Normal"].font.size = Pt(10.5)
styles["Normal"].paragraph_format.space_after = Pt(7)
styles["Normal"].paragraph_format.line_spacing = 1.35
for name, size in [("Title", 27), ("Heading 1", 19), ("Heading 2", 13)]:
    s = styles[name]
    s.font.name = "Yu Gothic"
    s._element.rPr.rFonts.set(qn("w:eastAsia"), "Yu Gothic")
    s.font.color.rgb = RGBColor(0, 0, 0)
    s.font.size = Pt(size)
    s.font.bold = True
    s.paragraph_format.space_before = Pt(14)
    s.paragraph_format.space_after = Pt(8)

def shade(cell, color):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:fill"), color)
    tcPr.append(shd)

def margins(cell, top=110, start=150, bottom=110, end=150):
    tc = cell._tc.get_or_add_tcPr()
    m = tc.first_child_found_in("w:tcMar")
    if m is None:
        m = OxmlElement("w:tcMar"); tc.append(m)
    for side, val in [("top",top),("start",start),("bottom",bottom),("end",end)]:
        el=OxmlElement("w:"+side); el.set(qn("w:w"),str(val)); el.set(qn("w:type"),"dxa"); m.append(el)

def conversation(who, text, dark=False):
    table = doc.add_table(rows=1, cols=2)
    table.autofit = False
    table.columns[0].width = Inches(1.15)
    table.columns[1].width = Inches(5.8)
    left, right = table.rows[0].cells
    left.width, right.width = Inches(1.15), Inches(5.8)
    shade(left, "202020" if dark else "E7E9EC")
    shade(right, "F3F4F6" if dark else "FFFFFF")
    for c in (left,right): margins(c); c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
    p=left.paragraphs[0]; p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    r=p.add_run(who); r.bold=True; r.font.color.rgb=RGBColor(255,255,255) if dark else RGBColor(0,0,0)
    right.paragraphs[0].add_run(text)
    doc.add_paragraph().paragraph_format.space_after=Pt(1)

# Cover
p=doc.add_paragraph(style="Title"); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
p.add_run("AIとの会話から生まれたPDFアプリ")
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
r=p.add_run("AiryReaderを一緒に作った記録"); r.bold=True; r.font.size=Pt(16)
icon=ROOT/"assets"/"icons"/"AiryReader-crane.png"
if icon.exists():
    p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    p.add_run().add_picture(str(icon), width=Inches(2.0))
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
r=p.add_run("私はこれまで、プログラムコードを一度も書いたことがありません。\nこれまでのアプリも、今回のAiryReaderも、すべてAIとの会話で作りました。\nAiryReaderは、仕事の合間に話しかけ、約1日でほぼ形になりました。")
r.font.size=Pt(13)
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
p.paragraph_format.space_before=Pt(22)
p.add_run("2026年9月  制作記録").italic=True

doc.add_page_break()
doc.add_heading("始まりは仕事の困りごと", level=1)
doc.add_paragraph("私は、仕事で使う1/5000の図面を正確に出力したいと考えていました。Adobe Acrobat Readerでは、1/5000の図面を原寸どおりに印刷でき、図面上の1cmが現地の50mを表すスケールも正確に合っていました。ただ、私の環境では立ち上がりが重く感じられ、案内や広告も多いことが気になっていました。反対に、ブラウザは立ち上がりが速く、軽くて使いやすいのですが、原寸どおりに出力する設定を選んでも、印刷した紙の1cmが正確に合わないことが何度もありました。そこで、Adobeのように寸法を正確に出力でき、ブラウザのように軽くて簡単な、自分向けのオリジナルな道具が欲しいと思いました。そこでAIに、仕様書ではなく普段の言葉で相談しました。")
doc.add_heading("昨日から今日までの約1日", level=2)
doc.add_paragraph("このアプリは、昨日から今日までの約1日で、ほぼ使える形になりました。私が一日中パソコンに向かってプログラムを書いたわけではありません。仕事や用事の合間に、気づいたことをチャットで少し話しました。その間にAIが作成や修正、確認を進め、できたものを私が触って、また感想を返すという流れでした。私の感覚では、片手間に何度か話しかけていたら、使えるアプリが少しずつ出来上がってきた、というのが近いです。")
conversation("私", "Adobe Acrobat Readerと同じくらい、寸法に正確に出力できるPDFリーダーを作りたいです。", True)
conversation("AI", "まず、原寸や指定倍率では勝手に縮小せず、用紙からはみ出す場合はプレビューで分かるようにしましょう。")
conversation("私", "はい。新しいPDFは100％で始めたいです。複数ページはスクロールでも見たいです。", True)
doc.add_paragraph("私はプログラムコードを一度も書いたことがなく、アプリの作り方も、正確な頼み方も知りませんでした。最初から全体を決めたわけではなく、できた画面を触り、気づいたことをそのまま話していきました。")

doc.add_heading("画面を見ながら少しずつ整える", level=1)
conversation("私", "幅に合わせるは『画面幅に合わせる』に。回転は文字ではなくアイコンに。上のボタンもシンプルにしてください。", True)
conversation("AI", "開く、印刷、倍率、回転、画面幅の操作を中心に整理します。ショートカットも表示します。")
conversation("私", "倍率は手入力できるように。Ctrlとスクロールでは必ず100％に止まってほしいです。", True)
conversation("AI", "倍率をまたいだときに100％で一度止まるようにします。プラスとマイナスも同じ動きに揃えます。")
doc.add_paragraph("私が伝えたのは、『何となく使いづらい』『ここが隠れる』『前の色の方がよい』といった感想です。スクリーンショットを見せると、AIが修正版を作り、私はもう一度使って確かめました。")

doc.add_page_break()
doc.add_heading("実際の仕事で試して完成度を上げる", level=1)
conversation("私", "会社の複合機で印刷チェックをします。このPCを持って行きます。", True)
conversation("AI", "原本の用紙サイズと向きを引き継ぎ、印刷倍率100％で確認できるようにします。")
conversation("私", "会社で印刷しました。0.1mmくらいずれて見えますが、私の用途では完璧です。", True)
doc.add_paragraph("画面上で動くだけでは、仕事の道具として十分か分かりません。実際の複合機で印刷し、自分の用途に必要な精度を満たすか確認したことで、AiryReaderは『試作品』から『使えるアプリ』へ進みました。")

doc.add_heading("会話は見た目や名前にも及んだ", level=1)
conversation("私", "黒背景で、グラデーションなし。アイコンは少しかわいくしたいです。", True)
conversation("AI", "羽根、勾玉、紙などを試し、最後に折り紙のツルをシンボルとして提案します。")
conversation("私", "ツル、採用。左上やタスクバーでもはっきり見えるようにしてください。", True)
conversation("AI", "小さい表示専用に首と面を太くし、アプリ、タスクバー、エクスプローラーで揃えます。")
doc.add_paragraph("機能だけでなく、色、言葉、ボタンの位置、アイコンまで会話の中で変わりました。私にも好みや使い勝手は判断できたので、それを伝え、AIが形にする往復が続きました。")

doc.add_heading("やってみて感じたこと", level=1)
items = [
    ("専門用語をほとんど使わなかった", "私が使ったのは、『ボタンが隠れる』『少しぼやける』『閉じるのがひと手間』といった普段の言葉でした。"),
    ("途中で考えが変わった", "色やアイコンは何度か試し、前の案へ戻したこともあります。それでも作業は続けられました。"),
    ("画像が会話の助けになった", "説明しにくいときは、画面のスクリーンショットや対象のPDF、プリンター名を見せました。"),
    ("分からないままでも相談できた", "コードを書くことも技術的な方法を指定することもなく、どんな使い方をしたいか、何が気になるかを伝えました。"),
    ("実際に使うことが判断材料になった", "会社の複合機で印刷し、自分の仕事に十分な精度だと確認できたことで、ひとつの区切りがつきました。"),
]
for title, body in items:
    p=doc.add_paragraph(); r=p.add_run(title+"  "); r.bold=True; p.add_run(body)

doc.add_heading("私の場合はこんな始まり方だった", level=1)
doc.add_paragraph("立派な企画書や完成図があったわけではありません。仕事で困っていたことと、こうなれば便利だという希望を話しただけでした。そこから実物ができ、使った感想を返すたびに少しずつ変わっていきました。")
conversation("最初の相談", "仕事で使うPDFを、寸法どおりに印刷できるアプリが欲しいです。", True)

doc.add_heading("現在のAiryReader", level=1)
doc.add_paragraph("AiryReaderは、閲覧、複数ページのスクロール、倍率調整、原寸を意識した印刷、入力、注釈、検索、しおり、電子署名などを備えたWindowsアプリになりました。私はコードを一度も書いたことがありません。Forest Cruiseをはじめ、これまでのアプリもすべてAIとの会話で作りました。AiryReaderも、空いた時間に話しかけ、約1日でほぼ形になりました。")
p=doc.add_paragraph(); p.alignment=WD_ALIGN_PARAGRAPH.CENTER
r=p.add_run("これはチャットの逐語録ではなく、実際の開発経過をもとに読みやすく再構成した記録です。")
r.font.size=Pt(8.5); r.font.color.rgb=RGBColor(90,90,90)

# footer
for section in doc.sections:
    footer=section.footer.paragraphs[0]; footer.alignment=WD_ALIGN_PARAGRAPH.CENTER
    footer.add_run("AiryReader  AIとの会話から生まれたPDFアプリ").font.size=Pt(8)

doc.core_properties.title = "AIとの会話から生まれたPDFアプリ"
doc.core_properties.subject = "AiryReader制作記録"
doc.save(DOCX)
print(DOCX)
