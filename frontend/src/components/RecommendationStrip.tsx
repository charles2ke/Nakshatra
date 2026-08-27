import { useLocale } from '../i18n/LocaleContext';
import type { Product } from '../api/client';
import ProductCard from './ProductCard';

interface Props { products: Product[]; titleKey?: string; }

export default function RecommendationStrip({ products, titleKey = 'recs.title' }: Props) {
  const { t } = useLocale();
  if (!products.length) return null;
  return (
    <div className="rec-strip" data-testid="recommendation-strip">
      <h3>{t(titleKey)}</h3>
      <div className="rec-strip-items">
        {products.map(p => <ProductCard key={p.id} product={p} />)}
      </div>
    </div>
  );
}
