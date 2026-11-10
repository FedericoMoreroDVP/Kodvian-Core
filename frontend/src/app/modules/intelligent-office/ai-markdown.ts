/**
 * Render mínimo y seguro de Markdown para las respuestas del asistente.
 * Primero se escapa todo el HTML y después se aplican solo estos formatos:
 * títulos, negritas, cursivas, código en línea, listas, tablas y saltos de línea.
 * No genera enlaces, imágenes ni atributos, así que el resultado no puede inyectar HTML.
 * Angular además vuelve a sanitizar el valor al usarlo con [innerHTML].
 */
export function renderAiMarkdown(text: string): string {
  const lines = escapeHtml(text ?? '').replace(/\r\n?/g, '\n').split('\n');
  const html: string[] = [];
  let paragraph: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;
  let table: string[][] = [];

  const flushParagraph = (): void => {
    if (paragraph.length) html.push(`<p>${paragraph.map(inline).join('<br>')}</p>`);
    paragraph = [];
  };
  const flushList = (): void => {
    if (list) {
      const tag = list.ordered ? 'ol' : 'ul';
      html.push(`<${tag}>${list.items.map(item => `<li>${inline(item)}</li>`).join('')}</${tag}>`);
    }
    list = null;
  };
  const flushTable = (): void => {
    if (table.length) {
      const [header, ...rows] = table;
      const head = `<thead><tr>${header.map(cell => `<th>${inline(cell)}</th>`).join('')}</tr></thead>`;
      const body = rows.length ? `<tbody>${rows.map(row => `<tr>${row.map(cell => `<td>${inline(cell)}</td>`).join('')}</tr>`).join('')}</tbody>` : '';
      html.push(`<div class="md-table"><table>${head}${body}</table></div>`);
    }
    table = [];
  };
  const flushAll = (): void => { flushParagraph(); flushList(); flushTable(); };

  for (const rawLine of lines) {
    const line = rawLine.trimEnd();
    const trimmed = line.trim();

    if (!trimmed) { flushAll(); continue; }

    if (/^\|.*\|$/.test(trimmed)) {
      flushParagraph(); flushList();
      if (/^\|[\s:|-]+\|$/.test(trimmed)) continue; // fila separadora |---|---|
      table.push(trimmed.slice(1, -1).split('|').map(cell => cell.trim()));
      continue;
    }
    flushTable();

    const heading = /^(#{1,4})\s+(.*)$/.exec(trimmed);
    if (heading) { flushParagraph(); flushList(); html.push(`<p class="md-heading">${inline(heading[2])}</p>`); continue; }

    const bullet = /^[-*•]\s+(.*)$/.exec(trimmed);
    const numbered = /^\d+[.)]\s+(.*)$/.exec(trimmed);
    if (bullet || numbered) {
      flushParagraph();
      const ordered = !!numbered;
      if (!list || list.ordered !== ordered) { flushList(); list = { ordered, items: [] }; }
      list.items.push((bullet ?? numbered)![1]);
      continue;
    }

    flushList();
    paragraph.push(trimmed);
  }
  flushAll();
  return html.join('');
}

function inline(value: string): string {
  return value
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/__([^_]+)__/g, '<strong>$1</strong>')
    .replace(/(^|[\s(])\*([^*\s][^*]*)\*(?=[\s).,;:!?]|$)/g, '$1<em>$2</em>');
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
