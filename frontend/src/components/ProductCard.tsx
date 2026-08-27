import { Link } from 'react-router-dom';
import { useLocale } from '../i18n/LocaleContext';
import type { Product } from '../api/client';

interface Props { product: Product; onAddToCart?: (p: Product) => void; }

export default function ProductCard({ product, onAddToCart }: Props) {
  const { t, formatCurrency } = useLocale();
  return (
    <div className="product-card" data-testid={`product-card-${product.id}`}>
      <Link to={`/product/${product.id}`}>
        <img src={product.imageUrl || 'https://placehold.co/200x150'} alt={product.name} className="product-img" />
        <div className="product-name">{product.name}</div>
        <div className="product-price">{formatCurrency(product.price, product.currency)}</div>
        <div className="product-category">{product.category}</div>
      </Link>
      {onAddToCart && (
        <button className="btn-add-cart" data-testid={`add-to-cart-${product.id}`} onClick={() => onAddToCart(product)}>
          {t('product.addToCart')}
        </button>
      )}
    </div>
  );
}
