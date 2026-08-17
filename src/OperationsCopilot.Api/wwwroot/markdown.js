/*
  A deliberately small Markdown renderer for the subset the agent actually produces:
  headings, tables, lists, bold, italic, inline code, and [1] citation markers.

  Security: the answer is model output and is therefore untrusted. Every character is HTML-escaped
  *first*, and only then are known-safe structures reintroduced. Nothing here ever inserts raw
  source into the DOM, which is why this exists instead of pulling in a general Markdown library
  that would also need an HTML sanitiser behind it.
*/

// Attached to window explicitly rather than left as a top-level const: that keeps the
// dependency visible from app.js and survives the file being loaded as a module.
window.Markdown = (() => {
  'use strict';

  const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

  /** Placeholder for a stashed code span. Printable, and applied after escaping. */
  const CODE_OPEN = '%%CODE';
  const CODE_CLOSE = '%%';

  function escapeHtml(text) {
    return String(text).replace(/[&<>"']/g, (character) => ESCAPES[character]);
  }

  /** Cells that hold a number are right-aligned and tabular, so columns scan cleanly. */
  function isNumeric(cell) {
    const plain = cell.replace(/<[^>]+>/g, '').trim();
    return plain !== '' && /^[-+(]?[\d,]+(\.\d+)?\)?%?$/.test(plain);
  }

  function splitRow(line) {
    return line
      .replace(/^\s*\|/, '')
      .replace(/\|\s*$/, '')
      .split('|')
      .map((cell) => cell.trim());
  }

  const isTableDivider = (line) =>
    /^\s*\|?[\s:|-]*-[\s:|-]*\|?\s*$/.test(line) && line.includes('-');

  const isTableRow = (line) => line.trim().startsWith('|');

  const startsBlock = (line) =>
    isTableRow(line) ||
    /^\s*[-*]\s+/.test(line) ||
    /^\s*\d+[.)]\s+/.test(line) ||
    /^#{1,6}\s/.test(line);

  /**
   * Inline formatting. Code spans are stashed behind a placeholder before the emphasis pass, so
   * that a double asterisk inside backticks is left alone.
   */
  function inline(text) {
    const codeSpans = [];

    let output = text.replace(/`([^`]+)`/g, (_, code) => {
      codeSpans.push(code);
      return CODE_OPEN + (codeSpans.length - 1) + CODE_CLOSE;
    });

    output = output
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/(^|[\s(])\*([^*\n]+)\*/g, '$1<em>$2</em>')
      // Citation markers become controls that highlight the matching entry in the rail.
      .replace(
        /\[(\d{1,2})\]/g,
        (_, reference) =>
          '<button type="button" class="cite" data-citation="' +
          reference +
          '" title="Show citation ' +
          reference +
          '">' +
          reference +
          '</button>',
      );

    return output.replace(
      /%%CODE(\d+)%%/g,
      (_, index) => '<code>' + codeSpans[Number(index)] + '</code>',
    );
  }

  function renderTable(lines, start) {
    const header = splitRow(lines[start]).map(inline);
    const body = [];
    let index = start + 2;

    while (index < lines.length && isTableRow(lines[index])) {
      body.push(splitRow(lines[index]).map(inline));
      index++;
    }

    const head =
      '<thead><tr>' +
      header.map((cell) => '<th scope="col">' + cell + '</th>').join('') +
      '</tr></thead>';

    const rows = body
      .map((cells) => {
        const tds = cells
          .map((cell) => '<td' + (isNumeric(cell) ? ' data-numeric="true"' : '') + '>' + cell + '</td>')
          .join('');
        return '<tr>' + tds + '</tr>';
      })
      .join('');

    // Wide tables scroll inside their own container rather than the page.
    return {
      html: '<div class="table-scroll"><table>' + head + '<tbody>' + rows + '</tbody></table></div>',
      next: index,
    };
  }

  function renderList(lines, start, ordered) {
    const pattern = ordered ? /^\s*\d+[.)]\s+(.*)$/ : /^\s*[-*]\s+(.*)$/;
    const items = [];
    let index = start;

    while (index < lines.length) {
      const match = lines[index].match(pattern);
      if (!match) {
        break;
      }

      items.push('<li>' + inline(match[1]) + '</li>');
      index++;
    }

    const tag = ordered ? 'ol' : 'ul';
    return { html: '<' + tag + '>' + items.join('') + '</' + tag + '>', next: index };
  }

  function renderParagraph(lines, start) {
    const buffer = [];
    let index = start;

    while (
      index < lines.length &&
      lines[index].trim() !== '' &&
      !(index > start && startsBlock(lines[index]))
    ) {
      buffer.push(lines[index].trim());
      index++;
    }

    return { html: '<p>' + inline(buffer.join('<br>')) + '</p>', next: index };
  }

  /** Renders Markdown to a safe HTML string. */
  function render(source) {
    if (!source) {
      return '';
    }

    const lines = escapeHtml(source).replace(/\r\n?/g, '\n').split('\n');
    const blocks = [];
    let index = 0;

    while (index < lines.length) {
      const line = lines[index];

      if (line.trim() === '') {
        index++;
        continue;
      }

      const heading = line.match(/^(#{1,6})\s+(.*)$/);
      if (heading) {
        // Levelled down: the page already owns h1 and h2.
        const level = Math.min(6, heading[1].length + 2);
        blocks.push('<h' + level + '>' + inline(heading[2]) + '</h' + level + '>');
        index++;
        continue;
      }

      if (isTableRow(line) && index + 1 < lines.length && isTableDivider(lines[index + 1])) {
        const table = renderTable(lines, index);
        blocks.push(table.html);
        index = table.next;
        continue;
      }

      if (/^\s*[-*]\s+/.test(line)) {
        const list = renderList(lines, index, false);
        blocks.push(list.html);
        index = list.next;
        continue;
      }

      if (/^\s*\d+[.)]\s+/.test(line)) {
        const list = renderList(lines, index, true);
        blocks.push(list.html);
        index = list.next;
        continue;
      }

      const paragraph = renderParagraph(lines, index);
      blocks.push(paragraph.html);
      index = paragraph.next;
    }

    return blocks.join('');
  }

  return { render, escapeHtml };
})();
