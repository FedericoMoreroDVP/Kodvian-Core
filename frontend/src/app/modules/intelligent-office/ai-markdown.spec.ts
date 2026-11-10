import { renderAiMarkdown } from './ai-markdown';

describe('renderAiMarkdown', () => {
  it('escapa el HTML antes de dar formato', () => {
    const html = renderAiMarkdown('Hola <script>alert(1)</script> <img src=x onerror=alert(1)>');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;script&gt;');
  });

  it('convierte negritas, código, listas y títulos', () => {
    const html = renderAiMarkdown('## Resumen\n**ARS**: ingresos `1000`\n\n- Tarea A\n- Tarea B\n\n1. Uno\n2. Dos');
    expect(html).toContain('<p class="md-heading">Resumen</p>');
    expect(html).toContain('<strong>ARS</strong>');
    expect(html).toContain('<code>1000</code>');
    expect(html).toContain('<ul><li>Tarea A</li><li>Tarea B</li></ul>');
    expect(html).toContain('<ol><li>Uno</li><li>Dos</li></ol>');
  });

  it('arma tablas ignorando la fila separadora', () => {
    const html = renderAiMarkdown('| Moneda | Resultado |\n|---|---|\n| ARS | 100 |\n| USD | 5 |');
    expect(html).toContain('<thead><tr><th>Moneda</th><th>Resultado</th></tr></thead>');
    expect(html).toContain('<tr><td>USD</td><td>5</td></tr>');
    expect(html).not.toContain('---');
  });

  it('mantiene los saltos de línea dentro de un párrafo', () => {
    expect(renderAiMarkdown('línea 1\nlínea 2')).toBe('<p>línea 1<br>línea 2</p>');
  });
});
