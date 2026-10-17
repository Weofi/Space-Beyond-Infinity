"""Small, dependency-free renderer for the Markdown subset used by AGENTS.md."""

import html
import json
import re


def load_player_guide(root):
    guide = json.loads((root / 'Tools/Docs/player_guide.json').read_text(encoding='utf-8'))
    ids = [page['id'] for page in guide['pages']]
    if len(ids) != len(set(ids)) or any(not re.fullmatch(r'[a-z][a-z-]+', key) for key in ids):
        raise ValueError('Player guide IDs must be unique URL-safe names')
    for page in guide['pages']:
        for field in ('title', 'summary', 'eyebrow', 'goal', 'sources', 'sections'):
            if not page.get(field):
                raise ValueError(f'Player guide {page["id"]} needs {field}')
        section_ids = [section['id'] for section in page['sections']]
        if len(section_ids) != len(set(section_ids)) or any(not re.fullmatch(r'[a-z][a-z-]+', key) for key in section_ids):
            raise ValueError(f'Duplicate or invalid player section IDs: {page["id"]}')
        for source in page['sources']:
            path = (root / source).resolve()
            if not path.is_relative_to(root.resolve()) or not path.is_file():
                raise ValueError(f'Missing player guide evidence: {source}')
        for section in page['sections']:
            targets = section.get('related', []) + [card['page'] for card in section.get('cards', []) if 'page' in card]
            if any(target not in ids for target in targets):
                raise ValueError(f'Unknown player guide link in {page["id"]}/{section["id"]}')
            for image in ([section['image']] if 'image' in section else []):
                path = (root / image['path']).resolve()
                if not path.is_relative_to(root.resolve()) or not path.is_file():
                    raise ValueError(f'Missing player guide image: {image["path"]}')
                if not image.get('alt', '').strip() or not image.get('caption', '').strip():
                    raise ValueError('Player guide images need alt text and captions')
    return guide


def player_markdown(guide):
    outputs = {}
    for page in guide['pages']:
        parts = ['<!-- Generated from Tools/Docs/player_guide.json. Do not edit. -->',
                 f'# {page["title"]}', '[Visuelles Wiki](index.html) | [Alle Kapitel](README.md)',
                 page['summary'], f'**Dein Ziel:** {page["goal"]}']
        for section in page['sections']:
            parts.append(f'## {section["title"]}')
            if section.get('text'):
                parts.append(section['text'])
            for number, step in enumerate(section.get('steps', []), 1):
                parts.append(f'{number}. **{step["title"]}.** {step["text"]}')
            for card in section.get('cards', []):
                parts.extend([f'### {card["title"]}', card['text']])
                if card.get('page'):
                    parts.append(f'[Anleitung öffnen]({card["page"]}.md)')
            if section.get('controls'):
                parts.append('| Taste / Begriff | Bedeutung |\n| --- | --- |\n' + '\n'.join(
                    f'| {key} | {value} |' for key, value in section['controls']))
            if section.get('flow'):
                parts.append(' → '.join(section['flow']))
            if section.get('result'):
                parts.append(f'**Das solltest du sehen:** {section["result"]}')
            if section.get('note'):
                parts.append(f'> {section["note"]}')
            if section.get('image'):
                image = section['image']
                parts.extend([f'![{image["alt"]}](../../{image["path"]})', image['caption']])
            if section.get('related'):
                titles = {p['id']: p['title'] for p in guide['pages']}
                parts.append('Weiterlesen: ' + ' · '.join(f'[{titles[key]}]({key}.md)' for key in section['related']))
        parts.extend(['---', f'Quellenabgleich: {guide["checked"]}. Kein neuer vollständiger Spieltest.',
                      ' · '.join(f'[{source.rsplit("/", 1)[-1]}](../../{source})' for source in page['sources'])])
        outputs[page['id'] + '.md'] = '\n\n'.join(parts) + '\n'
    return outputs


def inline(text):
    tokens = []

    def code(match):
        tokens.append('<code>' + html.escape(match[1]) + '</code>')
        return f'\x00{len(tokens) - 1}\x00'

    text = re.sub(r'`([^`]+)`', code, text)
    text = html.escape(text)

    def link(match):
        url = html.unescape(match[2])
        if re.match(r'(?:javascript|data|vbscript):', url, re.I):
            return match[1]
        if re.fullmatch(r'[\w-]+\.md', url):
            url = '#' + ('home' if url == 'README.md' else url[:-3])
        return f'<a href="{html.escape(url, quote=True)}">{match[1]}</a>'

    text = re.sub(r'\[([^\]]+)\]\(([^\s)]+)\)', link, text)
    text = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', text)
    text = re.sub(r'(?<!\*)\*([^*]+)\*(?!\*)', r'<em>\1</em>', text)
    return re.sub(r'\x00(\d+)\x00', lambda m: tokens[int(m[1])], text)


def article(markdown):
    # Remove generated navigation; the reader supplies its own navigation.
    markdown = markdown.split('[Wiki home]', 1)[1].split('\n', 1)[1].split('\n---\n', 1)[0]
    markdown = markdown.split('\n## Visual examples\n', 1)[0]
    chunks = []
    toc = []
    paragraph = []
    list_type = None
    fence = None

    def flush():
        if paragraph:
            chunks.append('<p>' + inline(' '.join(paragraph)) + '</p>')
            paragraph.clear()

    def close_list():
        nonlocal list_type
        if list_type:
            chunks.append(f'</{list_type}>')
            list_type = None

    for line in markdown.splitlines():
        if line.startswith('```'):
            flush()
            close_list()
            if fence is None:
                fence = []
            else:
                chunks.append('<pre><code>' + html.escape('\n'.join(fence)) + '</code></pre>')
                fence = None
            continue
        if fence is not None:
            fence.append(line)
            continue
        heading = re.match(r'^(#{2,4}) (.+)', line)
        item = re.match(r'^(?:[-*] |\d+\. )(.+)', line)
        if heading:
            flush()
            close_list()
            anchor = f'ref-{len(toc)}'
            title = re.sub(r'[`*]', '', heading[2])
            toc.append({'id': anchor, 'title': title})
            chunks.append(f'<h2 id="{anchor}">{inline(heading[2])}</h2>')
        elif item:
            flush()
            text = item[1]
            # Dense handoff bullets become named, expandable reference blocks.
            lead = re.match(r'^(.{1,125}?):\s(.+)', text)
            bold_lead = re.match(r'^\*\*(.{1,160}?)\*\*\s+(.+)', text)
            if bold_lead:
                lead = bold_lead
            if len(text) > 480 and lead:
                close_list()
                title = re.sub(r'[`*]', '', lead[1])
                anchor = f'ref-{len(toc)}'
                toc.append({'id': anchor, 'title': title})
                chunks.append(f'<details id="{anchor}"><summary>{inline(lead[1])}</summary>'
                              f'<div class="detail-body"><p>{inline(lead[2])}</p></div></details>')
            else:
                kind = 'ol' if re.match(r'^\d+\.', line) else 'ul'
                if list_type != kind:
                    close_list()
                    chunks.append(f'<{kind}>')
                    list_type = kind
                chunks.append('<li>' + inline(text) + '</li>')
        elif not line.strip():
            flush()
            close_list()
        else:
            close_list()
            paragraph.append(line.strip())
    flush()
    close_list()
    if fence is not None:
        raise ValueError('Unclosed code block in wiki source')
    return ''.join(chunks), toc


def render_reader(root, markdown_pages, pages):
    content_path = root / 'Tools/Docs/wiki_content.json'
    editorial = json.loads(content_path.read_text(encoding='utf-8'))
    expected_ids = {'home'} | {name[:-3] for name, _, _ in pages}
    if set(editorial) != expected_ids:
        raise ValueError('wiki_content.json must contain home and every wiki page exactly once')
    for key, entry in editorial.items():
        images = entry.get('images', [])
        if key == 'home':
            images = [{'path': entry['image']}]
        for image in images:
            path = (root / image['path']).resolve()
            if not path.is_relative_to(root.resolve()) or not path.is_file():
                raise ValueError(f'Missing or invalid wiki image: {image["path"]}')
            if key != 'home' and (not image.get('alt', '').strip() or not image.get('caption', '').strip()):
                raise ValueError(f'Wiki images need alt text and captions: {image["path"]}')
    guide = load_player_guide(root)
    if {page['id'] for page in guide['pages']} & (expected_ids | {'developer'}):
        raise ValueError('Player guide routes must not collide with developer routes')
    model = {'home': editorial['home'], 'guide': guide, 'pages': []}
    for name, _, _ in pages:
        key = name[:-3]
        rendered, toc = article(markdown_pages[name])
        model['pages'].append(dict(editorial[key], id=key, audience='developer', html=rendered, toc=toc,
                                   search=markdown_pages[name]))
    for page in guide['pages']:
        model['pages'].append(dict(page, audience='player', search=markdown_pages[page['id'] + '.md']))
    payload = json.dumps(model, ensure_ascii=False).replace('<', '\\u003c')
    template = (root / 'Tools/Docs/reader.html').read_text(encoding='utf-8')
    if template.count('/* WIKI_DATA */') != 1:
        raise ValueError('Reader template must contain one WIKI_DATA marker')
    return template.replace('/* WIKI_DATA */', payload)
