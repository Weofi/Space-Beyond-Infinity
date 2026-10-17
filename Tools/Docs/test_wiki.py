"""Checks for documentation drift, image metadata and safe reference rendering."""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from wiki_reader import article, inline, load_player_guide, player_markdown


ROOT = Path(__file__).resolve().parents[2]


class WikiChecks(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        source_dir = self.root / 'Tools/Docs'
        source_dir.mkdir(parents=True)
        for name in ('build_wiki.py', 'wiki_reader.py', 'reader.html', 'wiki_content.json', 'player_guide.json'):
            shutil.copyfile(ROOT / 'Tools/Docs' / name, source_dir / name)
        shutil.copyfile(ROOT / 'AGENTS.md', self.root / 'AGENTS.md')
        self.editorial = json.loads((source_dir / 'wiki_content.json').read_text())
        image_paths = [self.editorial['home']['image']]
        image_paths.extend(i['path'] for entry in self.editorial.values() for i in entry.get('images', []))
        for image in image_paths:
            path = self.root / image
            path.parent.mkdir(parents=True, exist_ok=True)
            path.touch()
        self.guide = json.loads((source_dir / 'player_guide.json').read_text())
        for page in self.guide['pages']:
            paths = page['sources'] + [section['image']['path'] for section in page['sections'] if 'image' in section]
            for relative in paths:
                path = self.root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.touch()

    def run_builder(self, *args):
        return subprocess.run([sys.executable, str(self.root / 'Tools/Docs/build_wiki.py'), *args],
                              capture_output=True, text=True)

    def test_missing_pages_and_template_drift(self):
        self.assertEqual(self.run_builder('--check').returncode, 1)
        self.assertEqual(self.run_builder().returncode, 0)
        self.assertEqual(self.run_builder('--check').returncode, 0)
        generated = self.root / 'Docs/Wiki/index.html'
        before = generated.read_bytes()
        template = self.root / 'Tools/Docs/reader.html'
        template.write_text(template.read_text() + '\n<!-- changed template -->\n')
        self.assertEqual(self.run_builder('--check').returncode, 1)
        self.assertEqual(generated.read_bytes(), before, '--check must never write')

    def test_missing_image_fails(self):
        (self.root / self.editorial['home']['image']).unlink()
        result = self.run_builder()
        self.assertEqual(result.returncode, 1)
        self.assertIn('image:', result.stderr)

    def test_image_without_caption_fails(self):
        self.editorial['journey']['images'][0]['caption'] = ''
        (self.root / 'Tools/Docs/wiki_content.json').write_text(json.dumps(self.editorial))
        result = self.run_builder()
        self.assertEqual(result.returncode, 1)
        self.assertIn('alt text and captions', result.stderr)

    def test_new_source_section_requires_mapping(self):
        path = self.root / 'AGENTS.md'
        path.write_text(path.read_text() + '\n## New subsystem\n\nNew facts.\n')
        result = self.run_builder()
        self.assertEqual(result.returncode, 1)
        self.assertIn('Unmapped AGENTS.md sections', result.stderr)

    def test_inline_code_is_literal_and_html_is_escaped(self):
        self.assertEqual(inline('`<tag> **literal**`'), '<code>&lt;tag&gt; **literal**</code>')
        self.assertEqual(inline('<script>'), '&lt;script&gt;')
        self.assertEqual(inline('[page](journey.md)'), '<a href="#journey">page</a>')
        self.assertNotIn('href', inline('[bad](javascript:alert)'))

    def test_dense_reference_keeps_details_and_outline(self):
        source = '# Page\n\n[Wiki home](README.md)\n\n## Systems\n\n- Warp: ' + 'Detail. ' * 80
        rendered, toc = article(source)
        self.assertIn('<details', rendered)
        self.assertEqual(rendered.count('Detail.'), 80)
        self.assertEqual([entry['title'] for entry in toc], ['Systems', 'Warp'])

    def test_player_guides_have_steps_evidence_and_markdown(self):
        guide = load_player_guide(ROOT)
        outputs = player_markdown(guide)
        self.assertEqual(len(outputs), 7)
        self.assertIn('## 2. Erst einmal an Bord umsehen', outputs['first-steps.md'])
        self.assertIn('Load Exotic Matter', outputs['equipment.md'])
        self.assertIn('kein freies Quicksave-System', outputs['help.md'])

    def test_broken_player_guide_link_fails(self):
        self.guide['pages'][0]['sections'][0]['related'] = ['missing-page']
        (self.root / 'Tools/Docs/player_guide.json').write_text(json.dumps(self.guide))
        result = self.run_builder()
        self.assertEqual(result.returncode, 1)
        self.assertIn('Unknown player guide link', result.stderr)

    def test_removed_generated_pages_are_detected_and_cleaned(self):
        self.assertEqual(self.run_builder().returncode, 0)
        retired = self.root / 'Docs/Wiki/retired-chapter.md'
        retired.write_text('<!-- Generated from a previous source. -->\nOld instructions.\n')
        handwritten = self.root / 'Docs/Wiki/review-notes.md'
        handwritten.write_text('# Handwritten review notes\nKeep this file.\n')
        result = self.run_builder('--check')
        self.assertEqual(result.returncode, 1)
        self.assertIn('Obsolete generated wiki pages', result.stderr)
        self.assertTrue(retired.exists(), '--check must not delete files')
        self.assertEqual(self.run_builder().returncode, 0)
        self.assertFalse(retired.exists())
        self.assertTrue(handwritten.exists())
        self.assertEqual(self.run_builder('--check').returncode, 0)


if __name__ == '__main__':
    unittest.main()
