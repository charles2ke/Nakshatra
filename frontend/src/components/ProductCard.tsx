import { Link } from 'react-router-dom';
import { useLocale } from '../i18n/LocaleContext';
import type { Product } from '../api/client';
import PriceTag from './PriceTag';
import StarRating from './StarRating';
import StockNotice from './StockNotice';

interface Props { product: Product; onAddToCart?: (p: Product) => void; }

export default function ProductCard({ product, onAddToCart }: Props) {
  const { t } = useLocale();
  const reviewCount = product.reviewCount ?? 0;
  return (
    <div className="product-card" data-testid={`product-card-${product.id}`}>
      <Link to={`/product/${product.id}`}>
        <img src={product.imageUrl || 'https://placehold.co/200x150'} alt={product.name} className="product-img" />
        <div className="product-name">{product.name}</div>
        {reviewCount > 0 && <StarRating rating={product.averageRating ?? 0} count={reviewCount} testId={`card-rating-${product.id}`} />}
        <PriceTag product={product} testId={`card-price-${product.id}`} />
        <div className="product-category">{product.category}</div>
        <StockNotice stock={product.stock} onlyWhenLow testId={`card-stock-${product.id}`} />
      </Link>
      {onAddToCart && (
        <button className="btn-add-cart" data-testid={`add-to-cart-${product.id}`} onClick={() => onAddToCart(product)} disabled={product.stock <= 0}>
          {t('product.addToCart')}
        </button>
      )}
    </div>
  );
}
