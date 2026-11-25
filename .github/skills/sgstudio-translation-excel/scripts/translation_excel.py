#!/usr/bin/env python3
"""Inspect and patch SGStudio Language.xlsx catalogs without rebuilding them."""

import argparse
from collections import Counter
from io import BytesIO
import json
from pathlib import Path
import posixpath
import re
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr
import zipfile


LANGUAGES = {
    'BNC_en': ('en',), 'neutral_cn': ('cn',), 'neutral_en': ('en',),
    'standard_cn': ('cn', 'en', 'ru'),
    'standard_en': ('en', 'ru'), 'standard_ru': ('en', 'ru'),
}
NS = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
REL = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
ROWS = re.compile(r'<row\b[^>]*?(?:/>|>.*?</row>)', re.S)
CELLS = re.compile(r'<c\b[^>]*?(?:/>|>.*?</c>)', re.S)
PLACEHOLDERS = re.compile(r'%(?:L?[1-9][0-9]*|n)')


def column(reference):
    return re.match(r'[A-Z]+', reference).group()


def column_number(letters):
    result = 0
    for letter in letters:
        result = result * 26 + ord(letter) - ord('A') + 1
    return result


def attribute(tag, name):
    match = re.search(r'\s' + name + r'="([^"]*)"', tag)
    return match.group(1) if match else None


def text_cell(reference, value, template='', preserve_attributes=False):
    if preserve_attributes:
        opening = template.split('>', 1)[0].rstrip('/')
        opening = re.sub(r'\st="[^"]*"', '', opening)
    else:
        style = attribute(template.split('>', 1)[0], 's')
        opening = '<c r=' + quoteattr(reference)
        if style is not None:
            opening += ' s=' + quoteattr(style)
    return (opening + ' t="inlineStr"><is><t xml:space="preserve">'
            + escape(value).replace('\r', '&#13;') + '</t></is></c>')


def load_entries(path):
    items = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(items, list):
        raise ValueError('entries must be a JSON array')
    entries = {}
    for item in items:
        if not isinstance(item, dict) or set(item) - {'source', 'cn', 'en', 'ru'}:
            raise ValueError('each entry accepts source, cn, en and ru only')
        source = item.get('source')
        if not isinstance(source, str) or not source.strip():
            raise ValueError('each entry needs a non-empty source')
        translations = {k: v for k, v in item.items() if k != 'source'}
        if not translations:
            raise ValueError(f'{source!r}: no translations supplied')
        merged = entries.setdefault(source, {})
        for language, value in translations.items():
            if not isinstance(value, str):
                raise ValueError(f'{source!r}/{language}: translation must be text')
            if Counter(PLACEHOLDERS.findall(source)) != Counter(PLACEHOLDERS.findall(value)):
                raise ValueError(f'{source!r}/{language}: Qt placeholders differ')
            if language in merged and merged[language] != value:
                raise ValueError(f'{source!r}/{language}: conflicting input translations')
            merged[language] = value
    return entries


class TranslationBook:
    def __init__(self, path, variant):
        self.path = path
        self.variant = variant
        with zipfile.ZipFile(BytesIO(path.read_bytes())) as archive:
            self.infos = archive.infolist()
            self.comment = archive.comment
            self.parts = {info.filename: archive.read(info) for info in self.infos}
        shared = ET.fromstring(self.parts['xl/sharedStrings.xml']) if 'xl/sharedStrings.xml' in self.parts else []
        self.strings = [''.join(t.text or '' for t in si.iter(NS + 't')) for si in shared]
        relationships = ET.fromstring(self.parts['xl/_rels/workbook.xml.rels'])
        targets = {r.get('Id'): r.get('Target') for r in relationships}
        sheets = ET.fromstring(self.parts['xl/workbook.xml']).find(NS + 'sheets')
        candidates = []
        for sheet in sheets:
            target = targets[sheet.get(REL + 'id')]
            part = target.lstrip('/') if target.startswith('/') else posixpath.normpath('xl/' + target)
            root = ET.fromstring(self.parts[part])
            rows = root.find(NS + 'sheetData')
            if rows is None or not len(rows):
                continue
            headers = {column(c.get('r')): self.value(c) for c in rows[0]}
            sources = [c for c, v in headers.items() if v.casefold() == 'source']
            if len(sources) == 1:
                candidates.append((sheet.get('name'), part, root, rows, headers, sources[0]))
        if len(candidates) != 1:
            raise ValueError(f'{path}: expected one Source translation table')
        self.sheet, self.part, self.root, self.rows, self.headers, self.source_column = candidates[0]
        self.values = self.cell_values(self.root)
        self.sources = {}
        for row in list(self.rows)[1:]:
            number = int(row.get('r'))
            source = self.values.get(self.source_column + str(number), '')
            if source:
                self.sources.setdefault(source, []).append(number)
        if len(self.sources.get('Localization', [])) != 1:
            raise ValueError(f'{path}: expected one Localization row')
        local_row = self.sources['Localization'][0]
        codes = {'zh_CN': 'cn', 'cn': 'cn', 'en': 'en', 'ru': 'ru'}
        self.columns = {c: codes[self.values.get(c + str(local_row))] for c in self.headers
                        if self.values.get(c + str(local_row)) in codes
                        and codes[self.values[c + str(local_row)]] in LANGUAGES[variant]}
        if set(self.columns.values()) != set(LANGUAGES[variant]):
            raise ValueError(f'{path}: language columns do not match {LANGUAGES[variant]}')
        self.xml = self.parts[self.part].decode('utf-8')
        self.row_xml = {int(attribute(m.group(), 'r')): m for m in ROWS.finditer(self.xml)}
        if len(self.row_xml) != len(self.rows):
            raise ValueError(f'{path}: unsupported worksheet row encoding')
        self.last_row = max(self.row_xml)
        self.tail = self.row_xml[self.last_row].group()
        self.styles = {column(attribute(c.group(), 'r')): c.group() for c in CELLS.finditer(self.tail)}

    def value(self, cell):
        if cell.get('t') == 'inlineStr':
            return ''.join(t.text or '' for t in cell.iter(NS + 't'))
        value = cell.find(NS + 'v')
        if value is None:
            return ''
        return self.strings[int(value.text)] if cell.get('t') == 's' else (value.text or '')

    def cell_values(self, root):
        return {c.get('r'): self.value(c) for row in root.find(NS + 'sheetData') for c in row}

    def summary(self):
        return {'variant': self.variant, 'sheet': self.sheet, 'rows': self.last_row,
                'headers': self.headers, 'write_columns': self.columns,
                'tail': [self.values.get(self.source_column + str(n), '')
                         for n in sorted(self.row_xml)[-3:]]}

    def patch_row(self, original, updates):
        pending = dict(updates)
        position = original.index('>') + 1
        output = original[:position]
        for match in CELLS.finditer(original):
            reference = attribute(match.group(), 'r')
            output += original[position:match.start()]
            before = sorted((r for r in pending if column_number(column(r)) < column_number(column(reference))),
                            key=lambda r: column_number(column(r)))
            for new in before:
                output += text_cell(new, pending.pop(new), self.styles.get(column(new), ''))
            output += (text_cell(reference, pending.pop(reference), match.group(), True)
                       if reference in pending else match.group())
            position = match.end()
        for reference in sorted(pending, key=lambda r: column_number(column(r))):
            output += text_cell(reference, pending[reference], self.styles.get(column(reference), ''))
        return output + original[position:]

    def plan(self, entries, existing):
        expected = dict(self.values)
        row_updates, appended, skipped = {}, [], 0
        for source, translations in entries.items():
            matches = self.sources.get(source, [])
            if matches and existing == 'skip':
                skipped += 1
                continue
            if not matches:
                missing = set(self.columns.values()) - translations.keys()
                if missing:
                    raise ValueError(f'{self.path}: {source!r} missing translations: {sorted(missing)}')
                number = self.last_row + len(appended) + 1
                appended.append((number, source))
                expected[self.source_column + str(number)] = source
                matches = [number]
            for number in matches:
                for col, language in self.columns.items():
                    reference = col + str(number)
                    if language in translations and self.values.get(reference) != translations[language]:
                        row_updates.setdefault(number, {})[reference] = translations[language]
                        expected[reference] = translations[language]

        xml = self.xml
        for number in sorted(row_updates, reverse=True):
            if number <= self.last_row:
                match = self.row_xml[number]
                xml = xml[:match.start()] + self.patch_row(match.group(), row_updates[number]) + xml[match.end():]
        additions = ''
        for number, source in appended:
            opening = re.sub(r'\sr="[^"]*"', ' r=' + quoteattr(str(number)), self.tail.split('>', 1)[0]) + '>'
            updates = {self.source_column + str(number): source, **row_updates.get(number, {})}
            additions += self.patch_row(opening + '</row>', updates)
        if additions:
            xml = xml.replace('</sheetData>', additions + '</sheetData>', 1)
            dimension = self.root.find(NS + 'dimension').get('ref')
            expanded = re.sub(r'\d+$', str(self.last_row + len(appended)), dimension)
            xml = re.sub(r'(<dimension\b[^>]*\sref=")[^"]*"',
                         lambda m: m.group(1) + expanded + '"', xml, count=1)
        if self.cell_values(ET.fromstring(xml)) != expected:
            raise ValueError(f'{self.path}: worksheet value verification failed')
        report = {'append': len(appended), 'update_cells': sum(len(v) for n, v in row_updates.items() if n <= self.last_row),
                  'skip': skipped, 'new_sources': [s for _, s in appended]}
        if xml == self.xml:
            return None, report
        buffer = BytesIO()
        with zipfile.ZipFile(buffer, 'w') as archive:
            archive.comment = self.comment
            for info in self.infos:
                archive.writestr(info, xml.encode('utf-8') if info.filename == self.part else self.parts[info.filename])
        output = buffer.getvalue()
        with zipfile.ZipFile(BytesIO(output)) as archive:
            for name, content in self.parts.items():
                expected_part = xml.encode('utf-8') if name == self.part else content
                if archive.read(name) != expected_part:
                    raise ValueError(f'{self.path}: ZIP part verification failed: {name}')
        return output, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('inspect', 'apply'))
    parser.add_argument('--repo', type=Path, required=True, help='SGStudio checkout root')
    parser.add_argument('--variants', nargs='+', choices=tuple(LANGUAGES), default=list(LANGUAGES))
    parser.add_argument('--entries', type=Path, help='UTF-8 JSON array of source/cn/en/ru objects')
    parser.add_argument('--sources', nargs='+', help='inspect exact Source keys and their current cells')
    parser.add_argument('--existing', choices=('update', 'skip'), default='update')
    args = parser.parse_args()
    if args.command == 'apply' and not args.entries:
        parser.error('apply requires --entries')
    if args.sources and args.command != 'inspect':
        parser.error('--sources is only available for inspect')
    try:
        entries = load_entries(args.entries) if args.entries else None
        plans = []
        for variant in dict.fromkeys(args.variants):
            book = TranslationBook(args.repo / 'configuration_files' / variant / 'Language.xlsx', variant)
            output, report = book.plan(entries, args.existing) if entries is not None else (None, {})
            if args.sources:
                report['entries'] = [{'source': source, 'matches': [
                    {'row': row, 'cells': {col: book.values.get(col + str(row), '') for col in book.headers}}
                    for row in book.sources.get(source, [])]} for source in args.sources]
            plans.append((book, output, report))
        # Resolve every input before writing any brand file.
        for book, output, report in plans:
            if args.command == 'apply' and output is not None:
                book.path.write_bytes(output)
                if book.path.read_bytes() != output:
                    raise OSError(f'{book.path}: saved bytes differ')
            print(json.dumps({**book.summary(), **report}, ensure_ascii=False))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        parser.exit(1, f'{error}\n')


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    main()
