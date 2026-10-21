# Resume test fixtures

Genuine files made by real programs from **invented** resumes, so the reader is tested on what Word and Chromium actually write, not on
files built by the code under test. Regenerate them with `tools\make-resume-fixtures.ps1` (needs Word and Edge).

| File | Made by | What it exercises |
|---|---|---|
| `resume-word.docx` | Microsoft Word | a Title and Heading 1 styles, bullets from the "List Bullet" style and from direct list formatting, dates on a right tab |
| `resume-word.pdf` | Word's own PDF export of the same document | symbol-font bullets, wrapped bullets (hanging indent), a wrapped paragraph |
| `resume-word-table.docx` | Microsoft Word | a two-column layout built on a table, the name in the page header |
| `resume-word-table.pdf` | Word's PDF export of that | two columns that must be read one after the other |
| `resume-edge.pdf` | Microsoft Edge (Chromium) printing HTML | bullet dots drawn as shapes, so there is no bullet character in the text |
| `scanned-image.pdf` | Edge printing a picture of text | a PDF with no text layer (there is no OCR) |

The people in them (Alex Morgan, Jordan Rivers, Sam Okafor, Casey Lindgren) and their employers are made up.