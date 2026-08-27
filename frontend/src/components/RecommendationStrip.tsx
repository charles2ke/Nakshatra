import type { Product } from '../api/client';
import ProductCard from './ProductCard';

interface Props { products: Product[]; title?: string; }

export default function RecommendationStrip({ products, title = 'Customers Also Purchased' }: Props) {
  if (!products.length) return null;
  return (
    <div className="rec-strip" data-testid="recommendation-strip">
      <h3>{title}</h3>
      <div className="rec-strip-items">
        {products.map(p => <ProductCard key={p.id} product={p} />)}
      </div>
    </div>
  );
}
