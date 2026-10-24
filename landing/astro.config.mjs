import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
  site: 'https://mathiasgonzalez.github.io',
  base: '/UruFactura',
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
          items: [{ autogenerate: { directory: 'getting-started' } }],
        },
        {
          label: 'Guías',
          items: [{ autogenerate: { directory: 'guides' } }],
        },
        {
          label: 'Migración',
          items: [{ autogenerate: { directory: 'migration' } }],
        },
        {
          label: 'Referencia',
          items: [{ autogenerate: { directory: 'reference' } }],
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
