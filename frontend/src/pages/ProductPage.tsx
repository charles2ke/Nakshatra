import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { getProduct, getAlsoPurchased, addToCart } from '../api/client';
import type { Product } from '../api/client';
import RecommendationStrip from '../components/RecommendationStrip';
import { usePersona } from '../context/PersonaContext';

export default function ProductPage() {
  const { id } = useParams<{ id: string }>();
  const { currentUser } = usePersona();
  const [product, setProduct] = useState<Product | null>(null);
  const [recs, setRecs] = useState<Product[]>([]);
  const [toast, setToast] = useState('');

  useEffect(() => {
    if (!id) return;
    getProduct(id).then(setProduct).catch(() => {});
    getAlsoPurchased(id).then(setRecs).catch(() => {});
  }, [id]);

  async function handleAddToCart() {
    if (!product || !currentUser) return;
    try {
      await addToCart(currentUser.id, product.id, 1);
      setToast('Added to cart!');
      setTimeout(() => setToast(''), 3000);
    } catch { setToast('Failed'); }
  }

  if (!product) return <div data-testid="product-loading">Loading product…</div>;

  return (
    <div data-testid="product-page">
      <img src={product.imageUrl || 'https://placehold.co/400x300'} alt={product.name} className="product-detail-img" />
      <h1 data-testid="product-name">{product.name}</h1>
      <div data-testid="product-price">{product.currency} {product.price.toFixed(2)}</div>
      <p data-testid="product-desc">{product.description}</p>
      <div>Category: {product.category}</div>
      <div>Stock: {product.stock}</div>
      {toast && <div className="toast">{toast}</div>}
      <button data-testid="product-add-cart" onClick={handleAddToCart} disabled={!currentUser}>Add to Cart</button>
      <RecommendationStrip products={recs} />
    </div>
  );
}
