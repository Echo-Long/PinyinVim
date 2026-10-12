"""Build the two original fixed-metric outline icons; fontTools is build-only."""
from pathlib import Path
from math import sin, cos, pi
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

folder = Path(__file__).parent / 'assets'
folder.mkdir(exist_ok=True)

def polygon(pen, points):
    pen.moveTo(points[0])
    for point in points[1:]:
        pen.lineTo(point)
    pen.closePath()

def gear():
    pen = TTGlyphPen(None)
    # Eight teeth with a generously sized aperture, matching the arrow weight.
    points = []
    for i in range(64):
        angle = 2*pi*i/64
        radius = 255 if i % 8 in (1, 2, 3, 4) else 205
        points.append((round(300+radius*cos(angle)), round(350+radius*sin(angle))))
    polygon(pen, points)
    polygon(pen, [(round(300+112*cos(-2*pi*i/48)), round(350+112*sin(-2*pi*i/48))) for i in range(48)])
    return pen.glyph()

def enter():
    pen = TTGlyphPen(None)
    polygon(pen, [(500,600),(550,600),(550,258),(143,258),(260,141),(225,106),(48,283),(225,460),(260,425),(143,308),(500,308)])
    return pen.glyph()

font = FontBuilder(1000, isTTF=True)
font.setupGlyphOrder(['.notdef','gear','enter'])
font.setupCharacterMap({0xE001:'gear', 0xE002:'enter'})
font.setupGlyf({'.notdef':TTGlyphPen(None).glyph(),'gear':gear(),'enter':enter()})
font.setupHorizontalMetrics({name:(600,0) for name in ['.notdef','gear','enter']})
font.setupHorizontalHeader(ascent=800, descent=-200)
font.setupNameTable({'familyName':'PinyinVim Icons','styleName':'Regular','uniqueFontIdentifier':'PinyinVimIcons-1.0','fullName':'PinyinVim Icons','psName':'PinyinVimIcons','version':'Version 1.0','copyright':'Original PinyinVim icons. CC0-1.0.'})
font.setupOS2(sTypoAscender=800,sTypoDescender=-200,usWinAscent=800,usWinDescent=200)
font.setupPost(isFixedPitch=1)
font.setupMaxp()
font.save(folder/'PinyinVimIcons.ttf')
print(folder/'PinyinVimIcons.ttf')
