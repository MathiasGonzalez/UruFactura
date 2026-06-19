import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import { visit } from 'unist-util-visit';

/**
 * Minimal rehype plugin: converts mermaid fenced-code blocks produced by
 * remark-rehype (<code class="language-mermaid">) into <pre class="mermaid">
 * so that the client-side Mermaid.js can pick them up and render them.
 */
function rehypePreMermaid() {
  return (tree) => {
    visit(tree, 'element', (node, index, parent) => {
      if (
        node.tagName !== 'code' ||
        !node.properties?.className?.includes('language-mermaid')
      ) {
        return;
      }
      const text = node.children
        .filter((c) => c.type === 'text')
        .map((c) => c.value)
        .join('');
      const pre = {
        type: 'element',
        tagName: 'pre',
        properties: { className: ['mermaid'] },
        children: [{ type: 'text', value: text }],
      };
      // Replace the wrapping <pre><code> pair (or bare <code>) with <pre class="mermaid">
      if (parent?.tagName === 'pre') {
        Object.assign(parent, pre);
      } else {
        parent?.children?.splice(index, 1, pre);
      }
    });
  };
}

// https://astro.build/config
export default defineConfig({
  site: 'https://mathiasgonzalez.github.io',
  base: '/UruFactura',
  markdown: {
    rehypePlugins: [rehypePreMermaid],
  },
  integrations: [
    starlight({
      components: {
        Head: './src/components/MermaidHead.astro',
      },
      title: 'UruFactura',
      description: 'La vía rápida hacia la Facturación Electrónica en Uruguay. Librería .NET open-source para integración con DGI.',
      logo: {
        light: './src/assets/logo-light.svg',
        dark: './src/assets/logo-dark.svg',
        replacesTitle: false,
      },
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/MathiasGonzalez/UruFactura' },
        { icon: 'external', label: 'NuGet', href: 'https://www.nuget.org/packages/UruFactura/' },
      ],
      editLink: {
        baseUrl: 'https://github.com/MathiasGonzalez/UruFactura/edit/main/landing/',
      },
      customCss: ['./src/styles/custom.css'],
      sidebar: [
        {
          label: '🚀 Inicio',
          link: '/',
        },
        {
          label: 'Primeros Pasos',
          autogenerate: { directory: 'getting-started' },
        },
        {
          label: 'Guías',
          autogenerate: { directory: 'guides' },
        },
        {
          label: 'Migración',
          autogenerate: { directory: 'migration' },
        },
        {
          label: 'Referencia',
          autogenerate: { directory: 'reference' },
        },
      ],
      head: [
        {
          tag: 'meta',
          attrs: {
            property: 'og:image',
            content: 'https://mathiasgonzalez.github.io/UruFactura/og-image.png',
          },
        },
      ],
    }),
  ],
});
