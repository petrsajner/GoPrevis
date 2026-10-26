"""Build the English beginner guide. Requires reportlab and Pillow.

Run: python docs/manual/build_guide.py
The editable content is guide.xml; all screenshots are real application captures.
"""
from pathlib import Path
import argparse
import html
import io
import xml.etree.ElementTree as ET

from PIL import Image as PILImage
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, PageBreak,
    Table, TableStyle, Image, KeepTogether, Flowable,
)
from reportlab.platypus.tableofcontents import TableOfContents

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
PAGE_W, PAGE_H = A4
MARGIN = 45
WIDTH = PAGE_W - MARGIN * 2
INK = colors.HexColor('#17233b')
MUTED = colors.HexColor('#58647a')
ACCENT = colors.HexColor('#684dcc')
PALE = colors.HexColor('#f1eefb')
RULE = colors.HexColor('#dce1ea')


def register_fonts():
    windows = Path('C:/Windows/Fonts')
    paths = [windows / 'segoeui.ttf', windows / 'segoeuib.ttf', windows / 'segoeuii.ttf']
    if all(p.exists() for p in paths):
        for name, path in zip(['Guide', 'Guide-Bold', 'Guide-Italic'], paths):
            pdfmetrics.registerFont(TTFont(name, str(path)))
        pdfmetrics.registerFontFamily('Guide', normal='Guide', bold='Guide-Bold', italic='Guide-Italic', boldItalic='Guide-Bold')
        return 'Guide'
    return 'Helvetica'


FONT = register_fonts()
BOLD = FONT + '-Bold'
STYLES = {
    'body': ParagraphStyle('Body', fontName=FONT, fontSize=10.2, leading=14.4,
                           textColor=INK, spaceAfter=6, allowWidows=0, allowOrphans=0),
    'h1': ParagraphStyle('Chapter', fontName=BOLD, fontSize=23, leading=28,
                         textColor=INK, spaceBefore=5, spaceAfter=14, keepWithNext=True),
    'h2': ParagraphStyle('Section', fontName=BOLD, fontSize=13.2, leading=18,
                         textColor=INK, spaceBefore=9, spaceAfter=6, keepWithNext=True),
    'caption': ParagraphStyle('Caption', fontName=FONT, fontSize=8.7, leading=12,
                              textColor=MUTED, spaceBefore=5, spaceAfter=14),
    'table': ParagraphStyle('TableBody', fontName=FONT, fontSize=9.3, leading=13,
                            textColor=INK, spaceAfter=0),
    'tablehead': ParagraphStyle('TableHead', fontName=BOLD, fontSize=9.4, leading=13,
                                textColor=colors.white),
    'note': ParagraphStyle('Note', fontName=FONT, fontSize=10, leading=14.5,
                           textColor=INK, spaceAfter=4),
    'kicker': ParagraphStyle('Kicker', fontName=BOLD, fontSize=10, leading=15,
                             textColor=ACCENT, spaceAfter=8),
}


def inner(node):
    text = html.escape(node.text or '')
    for child in node:
        # Our XML has only ReportLab-compatible inline markup inside paragraphs.
        text += ET.tostring(child, encoding='unicode', short_empty_elements=False)
    return text.strip()


class Layers(Flowable):
    def __init__(self):
        super().__init__()
        self.width = WIDTH
        self.height = 118

    def draw(self):
        c = self.canv
        labels = [
            ('1', 'Source body motion', 'Blend mocap / native clips or use a static base'),
            ('2', 'Body corrections', 'Add keyed local joint and pelvis adjustments'),
            ('3', 'Scene movement', 'Apply path travel / heading and object corrections'),
        ]
        for i, (number, title, subtitle) in enumerate(labels):
            y = 84 - i * 38
            c.setFillColor(PALE if i != 2 else colors.HexColor('#e9f3f5'))
            c.roundRect(0, y, WIDTH, 33, 5, fill=1, stroke=0)
            c.setFillColor(ACCENT)
            c.setFont(BOLD, 14)
            c.drawString(10, y + 10, number)
            c.setFillColor(INK)
            c.setFont(BOLD, 10)
            c.drawString(33, y + 18, title)
            c.setFont(FONT, 8.5)
            c.drawString(33, y + 5, subtitle)


class GuideDoc(BaseDocTemplate):
    def __init__(self, target, version):
        super().__init__(str(target), pagesize=A4,
                         leftMargin=MARGIN, rightMargin=MARGIN,
                         topMargin=53, bottomMargin=48,
                         title='GoPrevis - English User Guide',
                         author='Petr Sajner', subject='Beginner manual for GoPrevis ' + version,
                         creator='GoPrevis manual builder', lang='en-GB')
        self.version = version
        self.chapter_title = ''
        frame = Frame(MARGIN, 48, WIDTH, PAGE_H - 101, leftPadding=0,
                      rightPadding=0, topPadding=0, bottomPadding=0, id='normal')
        self.addPageTemplates(PageTemplate(id='guide', frames=[frame], onPage=self.page_chrome,
                                          onPageEnd=self.page_header))

    def beforeDocument(self):
        self.chapter_title = 'Contents'

    def page_header(self, c, doc):
        c.saveState()
        if doc.page > 1:
            c.setStrokeColor(RULE)
            c.setLineWidth(.5)
            c.line(MARGIN, PAGE_H - 34, PAGE_W - MARGIN, PAGE_H - 34)
            c.setFillColor(MUTED)
            c.setFont(FONT, 8)
            c.drawString(MARGIN, PAGE_H - 26, 'GoPrevis | User guide')
            title = self.chapter_title
            if len(title) > 64:
                title = title[:61] + '...'
            c.drawRightString(PAGE_W - MARGIN, PAGE_H - 26, title)
        c.restoreState()

    def page_chrome(self, c, doc):
        c.saveState()
        c.setStrokeColor(RULE)
        c.line(MARGIN, 35, PAGE_W - MARGIN, 35)
        c.setFillColor(MUTED)
        c.setFont(FONT, 8)
        c.drawString(MARGIN, 22, f'Version {self.version} | Copyright Petr Sajner 2026')
        c.drawRightString(PAGE_W - MARGIN, 22, str(doc.page))
        c.restoreState()

    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and hasattr(flowable, 'chapter_id'):
            title = flowable.getPlainText()
            key = flowable.chapter_id
            self.chapter_title = title
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(title, key, level=0, closed=False)
            self.notify('TOCEntry', (0, title, self.page, key))


def picture(path, width=WIDTH, crop=None):
    with PILImage.open(path) as source:
        if crop:
            source = source.crop(tuple(map(int, crop.split(','))))
        w, h = source.size
        memory = io.BytesIO()
        source.save(memory, 'PNG')
    memory.seek(0)
    return Image(memory, width=width, height=width * h / w, hAlign='LEFT')


def table(node):
    widths = [float(n) * WIDTH for n in node.attrib.get('widths', '.3,.7').split(',')]
    rows = []
    for i, row in enumerate(node.findall('row')):
        rows.append([Paragraph(inner(cell), STYLES['tablehead' if i == 0 else 'table'])
                     for cell in row.findall('cell')])
    result = Table(rows, colWidths=widths, repeatRows=1, hAlign='LEFT')
    result.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), INK),
        ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.white, colors.HexColor('#f4f6fa')]),
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('LEFTPADDING', (0, 0), (-1, -1), 9),
        ('RIGHTPADDING', (0, 0), (-1, -1), 9),
        ('TOPPADDING', (0, 0), (-1, -1), 5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
        ('LINEBELOW', (0, 0), (-1, 0), .5, INK),
    ]))
    result.spaceAfter = 10
    return [result]


def render_node(node):
    if node.tag == 'page':
        return [PageBreak()]
    if node.tag in ['p', 'h2']:
        return [Paragraph(inner(node), STYLES['body' if node.tag == 'p' else 'h2'])]
    if node.tag in ['ul', 'steps']:
        result = []
        for i, item in enumerate(node.findall('li'), 1):
            marker = str(i) + '.' if node.tag == 'steps' else '\u2022'
            row = Table([[Paragraph(marker, STYLES['body']), Paragraph(inner(item), STYLES['body'])]],
                        colWidths=[22, WIDTH - 22], hAlign='LEFT')
            row.setStyle(TableStyle([('VALIGN', (0, 0), (-1, -1), 'TOP'),
                                    ('LEFTPADDING', (0, 0), (-1, -1), 0),
                                    ('RIGHTPADDING', (0, 0), (-1, -1), 0),
                                    ('TOPPADDING', (0, 0), (-1, -1), 0),
                                    ('BOTTOMPADDING', (0, 0), (-1, -1), 3)]))
            result.append(row)
        if result:
            result[-1].spaceAfter = 3
        return result
    if node.tag in ['note', 'example']:
        title = '<b>' + html.escape(node.attrib['title']) + '</b><br/>'
        box = Table([[Paragraph(title + inner(node), STYLES['note'])]], colWidths=[WIDTH])
        box.setStyle(TableStyle([('BACKGROUND', (0, 0), (-1, -1), PALE),
                                ('BOX', (0, 0), (-1, -1), .5, colors.HexColor('#ded6f7')),
                                ('LEFTPADDING', (0, 0), (-1, -1), 12),
                                ('RIGHTPADDING', (0, 0), (-1, -1), 12),
                                ('TOPPADDING', (0, 0), (-1, -1), 10),
                                ('BOTTOMPADDING', (0, 0), (-1, -1), 8)]))
        box.spaceAfter = 12
        return [box]
    if node.tag == 'table':
        return table(node)
    if node.tag == 'figure':
        im = picture(ROOT / 'images' / node.attrib['src'],
                     min(WIDTH, float(node.attrib.get('width', WIDTH))), node.attrib.get('crop'))
        return [KeepTogether([im, Paragraph(node.attrib['caption'], STYLES['caption'])])]
    if node.tag == 'diagram':
        return [Layers(), Spacer(1, 9)]
    if node.tag == 'passes':
        labels = ['Color', 'Depth', 'Pose']
        images = [picture(ROOT / 'images' / (name.lower() + '-000216.png'), (WIDTH - 14) / 3)
                  for name in labels]
        result = Table([[Paragraph('<b>' + name + '</b>', STYLES['caption']) for name in labels],
                        images], colWidths=[WIDTH / 3] * 3)
        result.setStyle(TableStyle([('LEFTPADDING', (0, 0), (-1, -1), 0),
                                    ('RIGHTPADDING', (0, 0), (-1, -1), 0)]))
        return [KeepTogether([result, Paragraph('Three synchronized passes from the same street-shot frame. '
                      'Use the control format required by your destination tool.', STYLES['caption'])])]
    if node.tag == 'filmstrip':
        result = []
        for first, second in [(0, 72), (144, 216), (288, 359)]:
            row = Table([[picture(ROOT / 'images' / f'street-{n:06}.png', (WIDTH - 10) / 2)
                          for n in [first, second]]], colWidths=[WIDTH / 2] * 2)
            row.setStyle(TableStyle([('LEFTPADDING', (0, 0), (-1, -1), 0),
                                    ('RIGHTPADDING', (0, 0), (-1, -1), 0),
                                    ('BOTTOMPADDING', (0, 0), (-1, -1), 7)]))
            result.append(row)
        result.append(Paragraph('Actual active-camera frames at 0, 3, 6, 9, 12 and 14.96 seconds. '
                                'The flight and look direction are edited independently.', STYLES['caption']))
        return [KeepTogether(result)]
    raise ValueError('Unsupported content: ' + node.tag)


def build(target):
    guide = ET.parse(ROOT / 'guide.xml').getroot()
    version = guide.attrib['version']
    target.parent.mkdir(parents=True, exist_ok=True)
    doc = GuideDoc(target, version)
    story = [Spacer(1, 24), Paragraph('GOPREVIS / ENGLISH USER GUIDE', STYLES['kicker'])]
    cover = ParagraphStyle('Cover', fontName=BOLD, fontSize=37, leading=43, textColor=INK, spaceAfter=15)
    subtitle = ParagraphStyle('Subtitle', fontName=FONT, fontSize=16, leading=23, textColor=MUTED, spaceAfter=21)
    story += [Paragraph('Plan the scene.<br/>Direct the shot.', cover),
              Paragraph('A practical beginner guide to 3D previs<br/>for AI video workflows', subtitle),
              picture(ROOT / 'images' / 'street-000359.png'), Spacer(1, 18),
              Paragraph('Build a scene, pose and animate its subjects, frame a camera and export synchronized guidance.', STYLES['body']),
              Paragraph('<b>Complete manual workflow. Optional AI assistance.</b><br/>'
                        'No API key is needed for scene editing, animation, camera work or exports.', STYLES['body']),
              Spacer(1, 12), Paragraph(f'GoPrevis {version} | {guide.attrib["date"]}<br/>Windows x64 edition', STYLES['caption']), PageBreak()]
    story += [Paragraph('Contents', STYLES['h1']), Paragraph('Click a chapter title to jump to it. PDF bookmarks provide the same chapter navigation.', STYLES['body'])]
    toc = TableOfContents()
    toc.levelStyles = [ParagraphStyle('ContentsEntry', fontName=FONT, fontSize=10.2,
                                      leading=14, textColor=INK, leftIndent=0,
                                      firstLineIndent=0, spaceBefore=2, rightIndent=25)]
    story += [toc, PageBreak()]
    for number, chapter in enumerate(guide.findall('chapter'), 1):
        heading = Paragraph(f'{number:02d}  {chapter.attrib["title"]}', STYLES['h1'])
        heading.chapter_id = chapter.attrib['id']
        story.append(heading)
        for node in chapter:
            story.extend(render_node(node))
        if number < len(guide.findall('chapter')):
            story.append(PageBreak())
    doc.multiBuild(story)
    print(str(target))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=REPO / 'output/pdf/GoPrevis-User-Guide-EN.pdf')
    build(parser.parse_args().output.resolve())
