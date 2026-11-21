import type { ReactNode } from 'react';
import { useLocale } from '../i18n/LocaleContext';
import type { Product } from '../api/client';
import ProductCard from './ProductCard';

interface Props { products: Product[]; titleKey?: string; testId?: string; children?: ReactNode; }

/** A titled, horizontally scrolling row of products; `children` are rendered after the title (e.g. a "see all" link). */
export default function RecommendationStrip({ products, titleKey = 'recs.title', testId = 'recommendation-strip', children }: Props) {
  const { t } = useLocale();
  if (!products.length) return null;
  return (
    <div className="rec-strip" data-testid={testId}>
      <div className="rec-strip-header">
        <h3>{t(titleKey)}</h3>
        {children}
      </div>
      <div className="rec-strip-items">
        {products.map(p => <ProductCard key={p.id} product={p} />)}
      </div>
    </div>
  );
}
