"""Create the Windows-cell control from the generated outline-free BlankGlyphFont.

Run with Python and fonttools installed. No system font files are used.
"""
from pathlib import Path
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parent
font = TTFont(root / "BlankGlyphFont.ttf", recalcTimestamp=False)
os2 = font["OS/2"]
os2.version = 4
os2.fsSelection = 0x80  # USE_TYPO_METRICS
os2.sTypoAscender = 700
os2.sTypoDescender = -200
os2.sTypoLineGap = 0
os2.usWinAscent = 900
os2.usWinDescent = 300
font.save(root / "WindowsCellMetricsFont.ttf")
